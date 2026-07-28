using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace GlobalTranslator
{
    internal enum OcrCapabilityKind
    {
        EnglishBasic,
        EnglishOcr
    }

    internal enum OcrCapabilityState
    {
        Unknown,
        NotInstalled,
        Installed
    }

    internal enum OcrComponentAction
    {
        InstallRequired,
        RemoveOcr,
        RemoveAll
    }

    internal enum OcrComponentTaskStage
    {
        Idle,
        WaitingForElevation,
        Checking,
        InstallingBasic,
        InstallingOcr,
        RemovingOcr,
        CheckingDependencies,
        RemovingBasic,
        Verifying,
        Completed,
        Failed,
        Cancelled
    }

    internal sealed class OcrComponentProgress
    {
        public string TaskId = "";
        public OcrComponentAction Action;
        public OcrComponentTaskStage Stage;
        public int Percent = -1;
        public string Message = "";
        public DateTime StartedUtc;
        public TimeSpan Elapsed;
        public OcrCapabilityState BasicState;
        public OcrCapabilityState OcrState;
        public bool IsCompleted;
        public bool Success;
        public bool Cancelled;

        public OcrComponentProgress Copy()
        {
            return (OcrComponentProgress)MemberwiseClone();
        }
    }

    internal sealed class OcrComponentProgressEventArgs : EventArgs
    {
        public OcrComponentProgressEventArgs(OcrComponentProgress progress)
        {
            Progress = progress;
        }

        public OcrComponentProgress Progress { get; private set; }
    }

    internal sealed class OcrPackInstallResult
    {
        public bool Installed;
        public bool Cancelled;
        public int ExitCode;
        public string Message = "";
        public OcrComponentAction Action;
        public OcrCapabilityState BasicState;
        public OcrCapabilityState OcrState;
    }

    internal static class OcrLanguagePackManager
    {
        private const string BasicPattern = "Language.Basic*en-US*";
        private const string OcrPattern = "Language.OCR*en-US*";
        private static readonly object Sync = new object();
        private static readonly Regex PercentPattern = new Regex(
            @"(?<!\d)(\d{1,3}(?:[.,]\d+)?)\s*%",
            RegexOptions.Compiled);
        private static readonly string TaskFolder = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GlobalTranslator", "OcrTasks");
        private static readonly string DescriptorPath =
            Path.Combine(TaskFolder, "active.task");
        private static OcrComponentProgress _current;
        private static Task<OcrPackInstallResult> _runningTask;

        public static event EventHandler<OcrComponentProgressEventArgs>
            ProgressChanged;
        public static event EventHandler<OcrComponentProgressEventArgs>
            OperationCompleted;

        public static bool IsRunning
        {
            get
            {
                lock (Sync)
                    return _runningTask != null &&
                           !_runningTask.IsCompleted;
            }
        }

        public static OcrComponentProgress CurrentProgress
        {
            get
            {
                lock (Sync)
                    return _current == null
                        ? CreateIdleProgress()
                        : _current.Copy();
            }
        }

        public static bool IsEnglishInstalled()
        {
            return !string.IsNullOrEmpty(GetInstalledEnglishTag());
        }

        public static string GetInstalledEnglishTag()
        {
            try
            {
                foreach (Language language in
                    OcrEngine.AvailableRecognizerLanguages)
                    if (language.LanguageTag.StartsWith(
                        "en", StringComparison.OrdinalIgnoreCase))
                        return language.LanguageTag;
            }
            catch { }
            return "";
        }

        public static OcrComponentProgress GetInstalledSnapshot()
        {
            OcrComponentProgress progress = CreateIdleProgress();
            bool ocr = IsEnglishInstalled();
            progress.OcrState = ocr
                ? OcrCapabilityState.Installed
                : OcrCapabilityState.NotInstalled;
            progress.BasicState = ocr
                ? OcrCapabilityState.Installed
                : OcrCapabilityState.Unknown;
            progress.Message = ocr
                ? "English Basic 与 OCR 已可用。"
                : "OCR 未安装；Basic 状态将在管理员操作时确认。";
            return progress;
        }

        public static string ManualInstallCommand
        {
            get
            {
                return "$basic = Get-WindowsCapability -Online | " +
                       "Where-Object { $_.Name -Like '" +
                       BasicPattern + "' } | Select-Object -First 1\n" +
                       "if ($basic.State -ne 'Installed') { " +
                       "Add-WindowsCapability -Online -Name $basic.Name }\n\n" +
                       "$ocr = Get-WindowsCapability -Online | " +
                       "Where-Object { $_.Name -Like '" +
                       OcrPattern + "' } | Select-Object -First 1\n" +
                       "if ($ocr.State -ne 'Installed') { " +
                       "Add-WindowsCapability -Online -Name $ocr.Name }";
            }
        }

        public static Task<OcrPackInstallResult> InstallEnglishAsync()
        {
            return StartAsync(OcrComponentAction.InstallRequired);
        }

        public static Task<OcrPackInstallResult> StartAsync(
            OcrComponentAction action)
        {
            lock (Sync)
            {
                if (_runningTask != null && !_runningTask.IsCompleted)
                    return _runningTask;
                _runningTask = RunOperationAsync(action, null);
                return _runningTask;
            }
        }

        public static bool TryReconnect()
        {
            lock (Sync)
            {
                if (_runningTask != null && !_runningTask.IsCompleted)
                    return true;
                ActiveDescriptor descriptor = ReadDescriptor();
                if (descriptor == null ||
                    string.IsNullOrEmpty(descriptor.StatusPath))
                    return false;
                bool alive = IsProcessAlive(descriptor.ProcessId);
                if (!alive && !File.Exists(descriptor.StatusPath))
                    return false;
                _runningTask = RunOperationAsync(
                    descriptor.Action, descriptor);
                return true;
            }
        }

        internal static int ParseDismPercent(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return -1;
            Match match = PercentPattern.Match(line);
            if (!match.Success) return -1;
            double value;
            if (!double.TryParse(
                match.Groups[1].Value.Replace(',', '.'),
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out value))
                return -1;
            return Math.Max(
                0,
                Math.Min(
                    100,
                    (int)Math.Round(
                        value,
                        MidpointRounding.AwayFromZero)));
        }

        private static async Task<OcrPackInstallResult> RunOperationAsync(
            OcrComponentAction action, ActiveDescriptor reconnect)
        {
            string taskId = reconnect == null
                ? Guid.NewGuid().ToString("N")
                : reconnect.TaskId;
            DateTime started = reconnect == null
                ? DateTime.UtcNow
                : reconnect.StartedUtc;
            string statusPath = reconnect == null
                ? Path.Combine(TaskFolder, taskId + ".status")
                : reconnect.StatusPath;
            string logPath = reconnect == null
                ? Path.Combine(TaskFolder, taskId + ".log")
                : reconnect.LogPath;
            var progress = new OcrComponentProgress
            {
                TaskId = taskId,
                Action = action,
                Stage = reconnect == null
                    ? OcrComponentTaskStage.WaitingForElevation
                    : OcrComponentTaskStage.Checking,
                Percent = -1,
                Message = reconnect == null
                    ? "正在等待管理员授权…"
                    : "已重新连接 Windows 组件任务。",
                StartedUtc = started,
                BasicState = OcrCapabilityState.Unknown,
                OcrState = IsEnglishInstalled()
                    ? OcrCapabilityState.Installed
                    : OcrCapabilityState.NotInstalled
            };
            SetProgress(progress);

            Process process = null;
            int processId = reconnect == null
                ? 0
                : reconnect.ProcessId;
            int exitCode = -1;
            try
            {
                Directory.CreateDirectory(TaskFolder);
                if (reconnect == null)
                {
                    string script = BuildOperationScript(
                        action, statusPath, logPath);
                    string encoded = Convert.ToBase64String(
                        Encoding.Unicode.GetBytes(script));
                    string powershell = Path.Combine(
                        Environment.GetFolderPath(
                            Environment.SpecialFolder.Windows),
                        "System32", "WindowsPowerShell", "v1.0",
                        "powershell.exe");
                    var start = new ProcessStartInfo
                    {
                        FileName = powershell,
                        Arguments =
                            "-NoProfile -ExecutionPolicy Bypass " +
                            "-EncodedCommand " + encoded,
                        UseShellExecute = true,
                        Verb = "runas",
                        WindowStyle = ProcessWindowStyle.Hidden
                    };
                    process = Process.Start(start);
                    if (process == null)
                        throw new InvalidOperationException(
                            "无法启动 Windows 组件管理程序。");
                    processId = process.Id;
                    WriteDescriptor(new ActiveDescriptor
                    {
                        TaskId = taskId,
                        Action = action,
                        ProcessId = processId,
                        StartedUtc = started,
                        StatusPath = statusPath,
                        LogPath = logPath
                    });
                }

                bool processAlive = true;
                while (processAlive)
                {
                    ApplyStatusFile(statusPath, progress);
                    progress.Elapsed = DateTime.UtcNow - started;
                    SetProgress(progress);
                    await Task.Delay(450);
                    processAlive = process != null
                        ? !process.HasExited
                        : IsProcessAlive(processId);
                }
                if (process != null)
                    exitCode = process.ExitCode;
                ApplyStatusFile(statusPath, progress);
                bool ocrInstalled = IsEnglishInstalled();
                progress.OcrState = ocrInstalled
                    ? OcrCapabilityState.Installed
                    : OcrCapabilityState.NotInstalled;
                if (ocrInstalled)
                    progress.BasicState = OcrCapabilityState.Installed;
                if (progress.Stage != OcrComponentTaskStage.Failed)
                {
                    progress.Success = exitCode == 0 ||
                        reconnect != null;
                    progress.Stage = progress.Success
                        ? OcrComponentTaskStage.Completed
                        : OcrComponentTaskStage.Failed;
                }
                progress.IsCompleted = true;
                progress.Percent = progress.Success ? 100 : -1;
                if (string.IsNullOrWhiteSpace(progress.Message))
                    progress.Message = progress.Success
                        ? CompletionMessage(action)
                        : "Windows 组件操作失败，退出代码：" +
                          exitCode;
                SetProgress(progress);
                RaiseCompleted(progress);
                return ToResult(progress, exitCode);
            }
            catch (Win32Exception ex)
            {
                bool cancelled = ex.NativeErrorCode == 1223;
                progress.Stage = cancelled
                    ? OcrComponentTaskStage.Cancelled
                    : OcrComponentTaskStage.Failed;
                progress.Cancelled = cancelled;
                progress.IsCompleted = true;
                progress.Message = cancelled
                    ? "已取消管理员授权，系统未做更改。"
                    : "无法启动组件管理程序：" + ex.Message;
                SetProgress(progress);
                RaiseCompleted(progress);
                return ToResult(progress, ex.NativeErrorCode);
            }
            catch (Exception ex)
            {
                progress.Stage = OcrComponentTaskStage.Failed;
                progress.IsCompleted = true;
                progress.Message = "Windows 组件操作失败：" + ex.Message;
                SetProgress(progress);
                RaiseCompleted(progress);
                return ToResult(progress, exitCode);
            }
            finally
            {
                if (process != null) process.Dispose();
                TryDelete(DescriptorPath);
            }
        }

        private static string BuildOperationScript(
            OcrComponentAction action,
            string statusPath,
            string logPath)
        {
            string escapedStatus = PsQuote(statusPath);
            string escapedLog = PsQuote(logPath);
            string actionName = action.ToString();
            var script = new StringBuilder();
            script.Append("$ErrorActionPreference='Stop';");
            script.Append("$status='").Append(escapedStatus).Append("';");
            script.Append("$log='").Append(escapedLog).Append("';");
            script.Append("$action='").Append(actionName).Append("';");
            script.Append(
                "function Emit([string]$k,[string]$v){" +
                "$safe=if($null -eq $v){''}else{$v.Replace(\"`r\",' ').Replace(\"`n\",' ')};" +
                "Add-Content -LiteralPath $status -Value ($k+'|'+$safe) -Encoding UTF8};");
            script.Append(
                "function State([string]$stage,[string]$message){" +
                "Emit 'STAGE' $stage;Emit 'MESSAGE' $message};");
            script.Append(
                "function Capability([string]$pattern){" +
                "Get-WindowsCapability -Online | Where-Object {$_.Name -Like $pattern} | Select-Object -First 1};");
            script.Append(
                "function RunDism([string[]]$args){" +
                "Emit 'PROGRESS' '-1';" +
                "& dism.exe @args 2>&1 | ForEach-Object {" +
                "$line=$_.ToString();Add-Content -LiteralPath $log -Value $line -Encoding UTF8;" +
                "if($line -match '(?<!\\d)(\\d{1,3}(?:[\\.,]\\d+)?)\\s*%'){" +
                "Emit 'PROGRESS' ($matches[1].Replace(',','.'))}};" +
                "if($LASTEXITCODE -ne 0){throw ('DISM 失败，退出代码：'+$LASTEXITCODE)}};");
            script.Append(
                "try{New-Item -ItemType Directory -Force -Path (Split-Path $status)|Out-Null;" +
                "Set-Content -LiteralPath $status -Value 'STATE|Running' -Encoding UTF8;" +
                "State 'Checking' '正在检查 English Basic 与 OCR…';" +
                "$basic=Capability '").Append(BasicPattern).Append("';" +
                "$ocr=Capability '").Append(OcrPattern).Append("';" +
                "if($null -eq $basic){throw 'Windows 未提供 en-US Basic 组件。'};" +
                "if($null -eq $ocr){throw 'Windows 未提供 en-US OCR 组件。'};" +
                "Emit 'BASIC' $basic.State;Emit 'OCR' $ocr.State;");
            script.Append(
                "if($action -eq 'InstallRequired'){" +
                "if($basic.State -ne 'Installed'){State 'InstallingBasic' '正在下载并安装 English Basic…';" +
                "RunDism @('/Online','/Add-Capability',('/CapabilityName:'+$basic.Name),'/NoRestart','/English')};" +
                "$basic=Capability '").Append(BasicPattern).Append("';Emit 'BASIC' $basic.State;" +
                "if($ocr.State -ne 'Installed'){State 'InstallingOcr' '正在下载并安装 English OCR…';" +
                "RunDism @('/Online','/Add-Capability',('/CapabilityName:'+$ocr.Name),'/NoRestart','/English')}" +
                "}else{" +
                "if($ocr.State -eq 'Installed'){State 'RemovingOcr' '正在卸载 English OCR…';" +
                "RunDism @('/Online','/Remove-Capability',('/CapabilityName:'+$ocr.Name),'/NoRestart','/English')};" +
                "if($action -eq 'RemoveAll'){State 'CheckingDependencies' '正在检查 English Basic 依赖…';" +
                "$deps=Get-WindowsCapability -Online|Where-Object{" +
                "$_.State -eq 'Installed' -and $_.Name -like 'Language.*~~~en-US*' -and " +
                "$_.Name -notlike 'Language.Basic*' -and $_.Name -notlike 'Language.OCR*'};" +
                "if($deps){$names=($deps|ForEach-Object{$_.Name}) -join ', ';" +
                "Emit 'DEPENDENCIES' $names;throw ('Basic 仍被其他英文组件使用：'+$names)};" +
                "$basic=Capability '").Append(BasicPattern).Append("';" +
                "if($basic.State -eq 'Installed'){State 'RemovingBasic' '正在卸载 English Basic…';" +
                "RunDism @('/Online','/Remove-Capability',('/CapabilityName:'+$basic.Name),'/NoRestart','/English')}}};" +
                "State 'Verifying' '正在验证 Windows 组件状态…';" +
                "$basic=Capability '").Append(BasicPattern).Append("';" +
                "$ocr=Capability '").Append(OcrPattern).Append("';" +
                "Emit 'BASIC' $basic.State;Emit 'OCR' $ocr.State;" +
                "Emit 'MESSAGE' 'Windows 组件操作完成。';Emit 'STATE' 'Completed';exit 0" +
                "}catch{Emit 'MESSAGE' $_.Exception.Message;Emit 'STAGE' 'Failed';Emit 'STATE' 'Failed';exit 1}");
            return script.ToString();
        }

        private static void ApplyStatusFile(
            string path, OcrComponentProgress progress)
        {
            if (!File.Exists(path)) return;
            string[] lines;
            try
            {
                using (var stream = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    lines = reader.ReadToEnd().Split(
                        new[] { '\r', '\n' },
                        StringSplitOptions.RemoveEmptyEntries);
            }
            catch { return; }
            foreach (string line in lines)
            {
                int separator = line.IndexOf('|');
                if (separator <= 0) continue;
                string key = line.Substring(0, separator);
                string value = line.Substring(separator + 1);
                if (key == "STAGE")
                {
                    OcrComponentTaskStage stage;
                    if (Enum.TryParse(value, true, out stage))
                        progress.Stage = stage;
                }
                else if (key == "MESSAGE")
                    progress.Message = value;
                else if (key == "PROGRESS")
                {
                    double numeric;
                    progress.Percent = double.TryParse(
                        value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out numeric)
                        ? Math.Max(0, Math.Min(
                            100,
                            (int)Math.Round(
                                numeric,
                                MidpointRounding.AwayFromZero)))
                        : -1;
                }
                else if (key == "BASIC")
                    progress.BasicState = ParseCapabilityState(value);
                else if (key == "OCR")
                    progress.OcrState = ParseCapabilityState(value);
                else if (key == "STATE" &&
                         string.Equals(
                             value, "Failed",
                             StringComparison.OrdinalIgnoreCase))
                    progress.Stage = OcrComponentTaskStage.Failed;
            }
        }

        private static OcrCapabilityState ParseCapabilityState(
            string state)
        {
            if (string.Equals(
                state, "Installed",
                StringComparison.OrdinalIgnoreCase))
                return OcrCapabilityState.Installed;
            if (string.Equals(
                state, "NotPresent",
                StringComparison.OrdinalIgnoreCase))
                return OcrCapabilityState.NotInstalled;
            return OcrCapabilityState.Unknown;
        }

        private static void SetProgress(OcrComponentProgress progress)
        {
            OcrComponentProgress copy = progress.Copy();
            lock (Sync) _current = copy;
            EventHandler<OcrComponentProgressEventArgs> handler =
                ProgressChanged;
            if (handler != null)
                handler(null,
                    new OcrComponentProgressEventArgs(copy.Copy()));
        }

        private static void RaiseCompleted(
            OcrComponentProgress progress)
        {
            EventHandler<OcrComponentProgressEventArgs> handler =
                OperationCompleted;
            if (handler != null)
                handler(null,
                    new OcrComponentProgressEventArgs(progress.Copy()));
        }

        private static OcrComponentProgress CreateIdleProgress()
        {
            return new OcrComponentProgress
            {
                Stage = OcrComponentTaskStage.Idle,
                Percent = -1,
                StartedUtc = DateTime.UtcNow
            };
        }

        private static OcrPackInstallResult ToResult(
            OcrComponentProgress progress, int exitCode)
        {
            return new OcrPackInstallResult
            {
                Installed =
                    progress.OcrState == OcrCapabilityState.Installed,
                Cancelled = progress.Cancelled,
                ExitCode = exitCode,
                Message = progress.Message,
                Action = progress.Action,
                BasicState = progress.BasicState,
                OcrState = progress.OcrState
            };
        }

        private static string CompletionMessage(
            OcrComponentAction action)
        {
            if (action == OcrComponentAction.InstallRequired)
                return "English Basic 与 OCR 安装完成。";
            if (action == OcrComponentAction.RemoveOcr)
                return "English OCR 已卸载。";
            return "English OCR 与 Basic 已卸载。";
        }

        private static bool IsProcessAlive(int processId)
        {
            if (processId <= 0) return false;
            try
            {
                using (Process process =
                    Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch { return false; }
        }

        private static string PsQuote(string value)
        {
            return (value ?? "").Replace("'", "''");
        }

        private static void WriteDescriptor(
            ActiveDescriptor descriptor)
        {
            File.WriteAllText(
                DescriptorPath,
                descriptor.TaskId + "\n" +
                descriptor.Action + "\n" +
                descriptor.ProcessId.ToString(
                    CultureInfo.InvariantCulture) + "\n" +
                descriptor.StartedUtc.Ticks.ToString(
                    CultureInfo.InvariantCulture) + "\n" +
                descriptor.StatusPath + "\n" +
                descriptor.LogPath,
                Encoding.UTF8);
        }

        private static ActiveDescriptor ReadDescriptor()
        {
            try
            {
                if (!File.Exists(DescriptorPath)) return null;
                string[] values = File.ReadAllLines(
                    DescriptorPath, Encoding.UTF8);
                if (values.Length < 6) return null;
                OcrComponentAction action;
                int processId;
                long ticks;
                if (!Enum.TryParse(values[1], out action) ||
                    !int.TryParse(values[2], out processId) ||
                    !long.TryParse(values[3], out ticks))
                    return null;
                return new ActiveDescriptor
                {
                    TaskId = values[0],
                    Action = action,
                    ProcessId = processId,
                    StartedUtc = new DateTime(
                        ticks, DateTimeKind.Utc),
                    StatusPath = values[4],
                    LogPath = values[5]
                };
            }
            catch { return null; }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private sealed class ActiveDescriptor
        {
            public string TaskId;
            public OcrComponentAction Action;
            public int ProcessId;
            public DateTime StartedUtc;
            public string StatusPath;
            public string LogPath;
        }
    }
}
