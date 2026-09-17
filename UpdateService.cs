using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;

namespace GlobalTranslator
{
    internal sealed class UpdateInfo
    {
        public string Version;
        public string Title;
        public string Notes;
        public string ReleaseUrl;
        public string ExeUrl;
        public string HashUrl;
    }

    internal sealed class UpdateService : IDisposable
    {
        // Set this once the public binary-only GitHub repository has been
        // created. Keeping it here makes the release endpoint auditable.
        internal const string RepositoryOwner = "gefa250";
        internal const string RepositoryName = "Sharkey-Releases";
        internal const string ExeAssetName = "Sharkey-win-x64.exe";
        internal const string HashAssetName = "Sharkey-win-x64.exe.sha256";

        private static readonly TimeSpan AutomaticCheckInterval =
            TimeSpan.FromHours(24);
        private static readonly string StatePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "GlobalTranslator",
            "update-check.txt");

        private readonly HttpClient _http;
        private readonly string _latestReleaseUrl;

        public UpdateService()
            : this(null, BuildLatestReleaseUrl())
        {
        }

        internal UpdateService(
            HttpMessageHandler handler,
            string latestReleaseUrl)
        {
            _http = handler == null
                ? new HttpClient()
                : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(60);
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "Sharkey-Update/" + VersionInfo.SemanticVersion);
            _http.DefaultRequestHeaders.TryAddWithoutValidation(
                "Accept",
                "application/vnd.github+json");
            _latestReleaseUrl = latestReleaseUrl ?? "";
        }

        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(RepositoryOwner) &&
                       Uri.IsWellFormedUriString(
                           _latestReleaseUrl,
                           UriKind.Absolute);
            }
        }

        public async Task<UpdateInfo> CheckAsync(
            bool force,
            CancellationToken token)
        {
            if (!IsConfigured)
                throw new InvalidOperationException(
                    "发布仓库尚未配置 GitHub 用户名。");
            if (!force && !AutomaticCheckIsDue())
                return null;

            string json = await ReadStringAsync(
                _latestReleaseUrl,
                token);
            UpdateInfo info = ParseLatestReleaseJson(
                json,
                VersionInfo.SemanticVersion);
            RecordSuccessfulCheck();
            return info;
        }

        public async Task<string> DownloadAndVerifyAsync(
            UpdateInfo update,
            IProgress<int> progress,
            CancellationToken token)
        {
            if (update == null)
                throw new ArgumentNullException("update");
            string expectedHash = NormalizeSha256(
                await ReadStringAsync(update.HashUrl, token));

            string folder = Path.Combine(
                Path.GetTempPath(),
                "SharkeyUpdate",
                update.Version + "-" +
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string destination = Path.Combine(
                folder,
                "Sharkey-update.exe");
            try
            {
                using (HttpResponseMessage response = await _http.SendAsync(
                    new HttpRequestMessage(
                        HttpMethod.Get,
                        update.ExeUrl),
                    HttpCompletionOption.ResponseHeadersRead,
                    token))
                {
                    response.EnsureSuccessStatusCode();
                    long total = response.Content.Headers.ContentLength ?? -1;
                    if (total > 100L * 1024 * 1024)
                        throw new InvalidOperationException(
                            "更新文件超过允许的大小。");
                    using (Stream input =
                        await response.Content.ReadAsStreamAsync())
                    using (var output = new FileStream(
                        destination,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None))
                    {
                        var buffer = new byte[81920];
                        long received = 0;
                        while (true)
                        {
                            int count = await input.ReadAsync(
                                buffer,
                                0,
                                buffer.Length,
                                token);
                            if (count == 0) break;
                            await output.WriteAsync(
                                buffer,
                                0,
                                count,
                                token);
                            received += count;
                            if (received > 100L * 1024 * 1024)
                                throw new InvalidOperationException(
                                    "更新文件超过允许的大小。");
                            if (progress != null && total > 0)
                                progress.Report((int)Math.Min(
                                    100,
                                    received * 100 / total));
                        }
                    }
                }

                VerifySha256(destination, expectedHash);

                FileVersionInfo version =
                    FileVersionInfo.GetVersionInfo(destination);
                string productVersion = NormalizeVersionText(
                    version.ProductVersion);
                if (!string.Equals(
                        productVersion,
                        update.Version,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "更新文件版本与发布信息不一致。");
                return destination;
            }
            catch
            {
                TryDeleteDirectory(folder);
                throw;
            }
        }

        public static void LaunchUpdater(string downloadedExe)
        {
            string target = Process.GetCurrentProcess().MainModule.FileName;
            int oldProcessId = Process.GetCurrentProcess().Id;
            Process.Start(new ProcessStartInfo
            {
                FileName = downloadedExe,
                Arguments = "--apply-update " +
                    QuoteArgument(target) + " " +
                    oldProcessId.ToString(CultureInfo.InvariantCulture),
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(target)
            });
        }

        internal static UpdateInfo ParseLatestReleaseJson(
            string json,
            string currentVersion)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json)
                       as System.Collections.Generic.Dictionary<string, object>;
            if (root == null)
                throw new InvalidOperationException(
                    "GitHub 返回了无法识别的版本信息。");
            if (GetBoolean(root, "draft") ||
                GetBoolean(root, "prerelease"))
                throw new InvalidOperationException(
                    "GitHub 最新版本不是正式发布版本。");

            string version = NormalizeVersionText(
                GetString(root, "tag_name"));
            if (string.IsNullOrWhiteSpace(version))
                throw new InvalidOperationException(
                    "GitHub Release 缺少版本标签。");
            if (CompareVersions(version, currentVersion) <= 0)
                return null;

            string exeUrl = "";
            string hashUrl = "";
            object assetsValue;
            object[] assets = root.TryGetValue(
                "assets", out assetsValue)
                ? assetsValue as object[]
                : null;
            if (assets != null)
            {
                foreach (object item in assets)
                {
                    var asset = item as
                        System.Collections.Generic.Dictionary<string, object>;
                    if (asset == null) continue;
                    string name = GetString(asset, "name");
                    string url = GetString(
                        asset,
                        "browser_download_url");
                    if (string.Equals(
                            name,
                            ExeAssetName,
                            StringComparison.OrdinalIgnoreCase))
                        exeUrl = url;
                    else if (string.Equals(
                                 name,
                                 HashAssetName,
                                 StringComparison.OrdinalIgnoreCase))
                        hashUrl = url;
                }
            }
            if (!Uri.IsWellFormedUriString(exeUrl, UriKind.Absolute) ||
                !Uri.IsWellFormedUriString(hashUrl, UriKind.Absolute))
                throw new InvalidOperationException(
                    "GitHub Release 缺少更新程序或 SHA-256 文件。");

            return new UpdateInfo
            {
                Version = version,
                Title = GetString(root, "name"),
                Notes = GetString(root, "body"),
                ReleaseUrl = GetString(root, "html_url"),
                ExeUrl = exeUrl,
                HashUrl = hashUrl
            };
        }

        internal static string NormalizeSha256(string content)
        {
            string value = (content ?? "").Trim();
            int whitespace = value.IndexOfAny(
                new[] { ' ', '\t', '\r', '\n' });
            if (whitespace >= 0)
                value = value.Substring(0, whitespace);
            if (value.Length != 64)
                throw new InvalidOperationException(
                    "SHA-256 文件格式不正确。");
            foreach (char character in value)
                if (!Uri.IsHexDigit(character))
                    throw new InvalidOperationException(
                        "SHA-256 文件格式不正确。");
            return value.ToLowerInvariant();
        }

        internal static int CompareVersions(
            string first,
            string second)
        {
            Version firstVersion;
            Version secondVersion;
            if (!Version.TryParse(
                    NormalizeVersionText(first), out firstVersion) ||
                !Version.TryParse(
                    NormalizeVersionText(second), out secondVersion))
                throw new InvalidOperationException(
                    "版本号格式不正确。");
            return firstVersion.CompareTo(secondVersion);
        }

        internal static string ComputeSha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(input);
                var text = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                    text.Append(value.ToString("x2"));
                return text.ToString();
            }
        }

        internal static void VerifySha256(
            string path,
            string expectedHash)
        {
            string expected = NormalizeSha256(expectedHash);
            string actual = ComputeSha256(path);
            if (!string.Equals(
                    actual,
                    expected,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "更新文件校验失败，已取消安装。");
        }

        private async Task<string> ReadStringAsync(
            string url,
            CancellationToken token)
        {
            using (var request = new HttpRequestMessage(
                HttpMethod.Get,
                url))
            using (HttpResponseMessage response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseContentRead,
                token))
            {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static string BuildLatestReleaseUrl()
        {
            return "https://api.github.com/repos/" +
                   RepositoryOwner + "/" + RepositoryName +
                   "/releases/latest";
        }

        private static string NormalizeVersionText(string value)
        {
            string normalized = (value ?? "").Trim();
            if (normalized.StartsWith(
                    "v", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring(1);
            int suffix = normalized.IndexOf('-');
            if (suffix >= 0)
                normalized = normalized.Substring(0, suffix);
            int metadata = normalized.IndexOf('+');
            if (metadata >= 0)
                normalized = normalized.Substring(0, metadata);
            return normalized;
        }

        private static string GetString(
            System.Collections.Generic.Dictionary<string, object> source,
            string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value)
                ? value as string ?? ""
                : "";
        }

        private static bool GetBoolean(
            System.Collections.Generic.Dictionary<string, object> source,
            string key)
        {
            object value;
            return source != null && source.TryGetValue(key, out value) &&
                   value is bool && (bool)value;
        }

        private static bool AutomaticCheckIsDue()
        {
            try
            {
                DateTime checkedUtc;
                if (!File.Exists(StatePath) ||
                    !DateTime.TryParse(
                        File.ReadAllText(StatePath),
                        CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal |
                        DateTimeStyles.AssumeUniversal,
                        out checkedUtc))
                    return true;
                return DateTime.UtcNow - checkedUtc.ToUniversalTime() >=
                       AutomaticCheckInterval;
            }
            catch { return true; }
        }

        private static void RecordSuccessfulCheck()
        {
            try
            {
                Directory.CreateDirectory(
                    Path.GetDirectoryName(StatePath));
                File.WriteAllText(
                    StatePath,
                    DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                    Encoding.UTF8);
            }
            catch
            {
                // A read-only profile should not make version checks fail.
            }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }

        private static void TryDeleteDirectory(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, true);
            }
            catch { }
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }

    internal static class UpdateBootstrapper
    {
        public static bool TryRun(string[] arguments)
        {
            if (arguments == null || arguments.Length == 0)
                return false;
            if (string.Equals(
                    arguments[0],
                    "--apply-update",
                    StringComparison.OrdinalIgnoreCase))
            {
                Apply(arguments);
                return true;
            }
            if (string.Equals(
                    arguments[0],
                    "--cleanup-update",
                    StringComparison.OrdinalIgnoreCase))
            {
                Cleanup(arguments);
                return false;
            }
            return false;
        }

        private static void Apply(string[] arguments)
        {
            string target = "";
            int oldProcessId = 0;
            try
            {
                if (arguments.Length != 3)
                    throw new InvalidOperationException(
                        "更新参数不完整。");
                string source = Process.GetCurrentProcess()
                    .MainModule.FileName;
                target = Path.GetFullPath(arguments[1]);
                if (!int.TryParse(
                        arguments[2],
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out oldProcessId))
                    throw new InvalidOperationException(
                        "更新进程编号无效。");

                try
                {
                    using (Process oldProcess =
                        Process.GetProcessById(oldProcessId))
                        if (!oldProcess.WaitForExit(60000))
                            throw new TimeoutException(
                                "等待旧版鲨译退出超时。");
                }
                catch (ArgumentException)
                {
                    // The old process already exited.
                }

                string backup = target + ".update-backup";
                ReplaceExecutable(source, target, backup);
                int updaterProcessId = Process.GetCurrentProcess().Id;
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    Arguments = "--cleanup-update " +
                        QuoteArgument(source) + " " +
                        QuoteArgument(backup) + " " +
                        updaterProcessId.ToString(
                            CultureInfo.InvariantCulture),
                    UseShellExecute = true,
                    WorkingDirectory = Path.GetDirectoryName(target)
                });
            }
            catch (Exception error)
            {
                if (!string.IsNullOrWhiteSpace(target) &&
                    File.Exists(target) &&
                    !IsProcessRunning(oldProcessId))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = target,
                            UseShellExecute = true,
                            WorkingDirectory = Path.GetDirectoryName(target)
                        });
                    }
                    catch { }
                }
                MessageBox.Show(
                    "鲨译更新失败：" + error.Message,
                    "Sharkey",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        internal static void ReplaceExecutable(
            string source,
            string target,
            string backup)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException(
                    "未找到下载的更新文件。",
                    source);
            if (!File.Exists(target))
                throw new FileNotFoundException(
                    "未找到当前鲨译程序。",
                    target);

            if (File.Exists(backup)) File.Delete(backup);
            File.Copy(target, backup, true);
            try
            {
                File.Copy(source, target, true);
            }
            catch
            {
                File.Copy(backup, target, true);
                throw;
            }
        }

        private static void Cleanup(string[] arguments)
        {
            if (arguments.Length != 4) return;
            int updaterProcessId;
            if (int.TryParse(
                    arguments[3],
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out updaterProcessId))
            {
                try
                {
                    using (Process updater =
                        Process.GetProcessById(updaterProcessId))
                        updater.WaitForExit(10000);
                }
                catch { }
            }
            TryDelete(arguments[1]);
            TryDelete(arguments[2]);
            try
            {
                string folder = Path.GetDirectoryName(arguments[1]);
                if (!string.IsNullOrEmpty(folder) &&
                    Directory.Exists(folder) &&
                    Directory.GetFileSystemEntries(folder).Length == 0)
                    Directory.Delete(folder);
            }
            catch { }
        }

        private static void TryDelete(string path)
        {
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                catch
                {
                    Thread.Sleep(150);
                }
            }
        }

        private static bool IsProcessRunning(int processId)
        {
            if (processId <= 0) return false;
            try
            {
                using (Process process = Process.GetProcessById(processId))
                    return !process.HasExited;
            }
            catch { return false; }
        }

        private static string QuoteArgument(string value)
        {
            return "\"" + (value ?? "").Replace("\"", "\\\"") + "\"";
        }
    }
}
