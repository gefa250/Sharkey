using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Application = System.Windows.Application;

namespace GlobalTranslator
{
    internal sealed class TranslatorApplication : Application
    {
        private readonly AppSettings _settings = AppSettings.Load();
        private TranslationClient _client;
        private SelectionMonitor _monitor;
        private PopupWindow _popup;
        private SettingsWindow _settingsWindow;
        private NotifyIcon _tray;
        private ToolStripMenuItem _settingsMenuItem;
        private ToolStripMenuItem _translateMenuItem;
        private ToolStripMenuItem _ocrMenuItem;
        private ToolStripMenuItem _startupMenuItem;
        private HwndSource _messageWindow;
        private Mutex _mutex;
        private int _ocrRequestId;
        private CancellationTokenSource _ocrCancellation;
        private CancellationTokenSource _updateCancellation;
        private UpdateService _updateService;
        private UpdateWindow _updateWindow;
        private System.Windows.Threading.DispatcherTimer _updateTimer;
        private bool _exiting;
        private bool _updateCheckInProgress;
        private bool _screenshotSelecting;
        private WritingWindow _writingWindow;
        private ToolStripMenuItem _writingMenuItem;

        private void ShowWriting()
        {
            if (_screenshotSelecting) return;
            if (_writingWindow != null && _writingWindow.IsCapturing) return;
            if (_writingWindow == null)
            {
                _writingWindow = new WritingWindow(_settings, _client,
                    ShowModelSettings);
                _writingWindow.Closed += delegate { _writingWindow = null; };
            }
            _writingWindow.RefreshModelSummary();
            if (_writingWindow.WindowState == WindowState.Minimized)
                _writingWindow.WindowState = WindowState.Normal;
            _writingWindow.Show();
            _writingWindow.Activate();
        }

        [STAThread]
        public static void Main()
        {
            Mutex mutex = null;
            try
            {
                string[] commandLine = Environment.GetCommandLineArgs();
                var updateArguments = new string[Math.Max(
                    0,
                    commandLine.Length - 1)];
                if (updateArguments.Length > 0)
                    Array.Copy(
                        commandLine,
                        1,
                        updateArguments,
                        0,
                        updateArguments.Length);
                if (UpdateBootstrapper.TryRun(updateArguments))
                    return;
                DiagnosticLog.WriteStartup();
                AppDomain.CurrentDomain.UnhandledException += delegate(
                    object sender, UnhandledExceptionEventArgs args)
                {
                    DiagnosticLog.WriteException(
                        "Unhandled AppDomain exception",
                        args.ExceptionObject as Exception);
                };
                NativeMethods.EnablePerMonitorDpiAwareness();
                bool created;
                mutex = new Mutex(
                    true, "Sharkey.SingleInstance.75C36309", out created);
                if (!created)
                {
                    mutex.Dispose();
                    System.Windows.MessageBox.Show(
                        "鲨译 Sharkey 已在运行。", "Sharkey");
                    return;
                }

                var app = new TranslatorApplication { _mutex = mutex };
                mutex = null;
                app.DispatcherUnhandledException += delegate(
                    object sender,
                    System.Windows.Threading.DispatcherUnhandledExceptionEventArgs args)
                {
                    DiagnosticLog.WriteException(
                        "Unhandled dispatcher exception", args.Exception);
                };
                app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
                app.Run();
            }
            catch (Exception error)
            {
                DiagnosticLog.WriteException("Fatal startup failure", error);
                System.Windows.MessageBox.Show(
                    "鲨译启动失败，诊断日志已保存到：\n" +
                    DiagnosticLog.FilePath + "\n\n" + error.Message,
                    "Sharkey 启动失败",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
            finally
            {
                if (mutex != null) mutex.Dispose();
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _client = new TranslationClient();
            _updateService = new UpdateService();
            _popup = new PopupWindow();
            _popup.OcrRecaptureRequested += delegate
            {
                CaptureScreenshotAndTranslate();
            };
            _popup.ModelSettingsRequested += delegate
            {
                ShowModelSettings();
            };
            _popup.SettingsRequested += delegate
            {
                ShowSettings();
            };
            _popup.VisibilityChanged += delegate
            {
                RefreshDismissHotkey();
            };
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.SettingsSaved += delegate
            {
                RegisterConfiguredHotkeys();
                RefreshDismissHotkey();
                UpdateTrayLabels();
                RefreshStartupMenu();
                if (_writingWindow != null)
                    _writingWindow.RefreshModelSummary();
                if (_updateService != null)
                    _updateService.AutomaticCheckHours = _settings.UpdateCheckHours;
            };
            _settingsWindow.UpdateCheckRequested += async delegate
            {
                await CheckForUpdatesAsync(true);
            };

            // Selection is captured only when the user presses F8. Do not install the
            // global mouse hook: selecting text by itself must never trigger translation.
            _monitor = new SelectionMonitor(Dispatcher) { Enabled = true };
            _monitor.TextSelected += delegate(object sender, SelectedTextEventArgs args)
            {
                DiagnosticLog.Write("Selection captured; characters=" + args.Text.Length);
                _popup.Translate(args.Text, args.X, args.Y, _settings, _client);
            };
            _monitor.CaptureFailed += delegate(
                object sender, SelectionCaptureFailedEventArgs args)
            {
                DiagnosticLog.Write(
                    "Selection capture failed; reason=" + args.Reason);
                _popup.ShowTransientNotice(args.Message, args.X, args.Y);
            };

            CreateTray();
            CreateMessageWindow();
            if (_settings.StartWithWindows)
            {
                try { StartupManager.SetEnabled(true); }
                catch (Exception startupError)
                {
                    DiagnosticLog.Write(
                        "Startup registration refresh failed; type=" +
                        startupError.GetType().Name);
                }
            }
            bool settingsRequested = Array.IndexOf(e.Args, "--settings") >= 0;
            if (settingsRequested ||
                !AppSettings.HasSavedSettings ||
                !HasConfiguredProvider())
            {
                _settingsWindow.Show();
            }
            BeginAutomaticUpdateCheck();
            _updateTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(1)
            };
            _updateTimer.Tick += async delegate { await CheckForUpdatesAsync(false); };
            _updateTimer.Start();
        }

        private async void BeginAutomaticUpdateCheck()
        {
            try
            {
                await Task.Delay(1500);
                await CheckForUpdatesAsync(false);
            }
            catch
            {
                // CheckForUpdatesAsync handles and logs expected failures.
            }
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (_exiting || _updateCheckInProgress) return;
            if (!manual && _settings.UpdateCheckHours == 0) return;
            if (_updateService == null || !_updateService.IsConfigured)
            {
                if (manual && _settingsWindow != null)
                    _settingsWindow.SetUpdateStatus(
                        "发布仓库尚未配置",
                        true);
                return;
            }

            _updateCheckInProgress = true;
            _updateService.AutomaticCheckHours = _settings.UpdateCheckHours;
            if (manual && _settingsWindow != null)
                _settingsWindow.SetUpdateStatus(
                    "正在检查更新…",
                    false);
            var cancellation = new CancellationTokenSource();
            _updateCancellation = cancellation;
            try
            {
                UpdateInfo update = await _updateService.CheckAsync(
                    manual,
                    cancellation.Token);
                if (update == null)
                {
                    if (manual && _settingsWindow != null)
                        _settingsWindow.SetUpdateStatus(
                            "当前已是最新版本",
                            true);
                    return;
                }

                if (_settingsWindow != null)
                    _settingsWindow.SetUpdateStatus(
                        "发现新版本 " + update.Version,
                        true);
                ShowUpdateWindow(update);
            }
            catch (OperationCanceledException)
            {
                if (manual && _settingsWindow != null)
                    _settingsWindow.SetUpdateStatus(
                        "检查已取消",
                        true);
            }
            catch (Exception error)
            {
                DiagnosticLog.Write(
                    "Update check failed; type=" +
                    error.GetType().Name);
                if (manual && _settingsWindow != null)
                    _settingsWindow.SetUpdateStatus(
                        "检查失败：" + CompactError(error.Message),
                        true);
            }
            finally
            {
                if (ReferenceEquals(_updateCancellation, cancellation))
                    _updateCancellation = null;
                cancellation.Dispose();
                _updateCheckInProgress = false;
            }
        }

        private void ShowUpdateWindow(UpdateInfo update)
        {
            if (_updateWindow != null && _updateWindow.IsVisible)
            {
                _updateWindow.Activate();
                return;
            }
            _updateWindow = new UpdateWindow(update, _updateService);
            if (_settingsWindow != null && _settingsWindow.IsVisible)
                _updateWindow.Owner = _settingsWindow;
            _updateWindow.Closed += delegate { _updateWindow = null; };
            _updateWindow.Show();
            _updateWindow.Activate();
        }

        private bool HasConfiguredProvider()
        {
            if (_settings.Provider == "GoogleFree" || _settings.Provider == "MicrosoftFree") return true;
            if (_settings.Provider == "ModelApi")
                return _settings.GetModelConnection(
                    _settings.ModelVendor).IsUsable(
                        _settings.ModelVendor);
            return _settings.Provider == "Google"
                ? !string.IsNullOrWhiteSpace(_settings.GoogleApiKey)
                : !string.IsNullOrWhiteSpace(_settings.MicrosoftApiKey);
        }

        private void CreateMessageWindow()
        {
            var parameters = new HwndSourceParameters(
                "SharkeyMessageWindow")
            {
                Width = 0, Height = 0, WindowStyle = 0
            };
            _messageWindow = new HwndSource(parameters);
            _messageWindow.AddHook(WindowProc);
            RegisterConfiguredHotkeys();
        }

        private void RefreshDismissHotkey()
        {
            if (_messageWindow == null || _popup == null) return;
            IntPtr handle = _messageWindow.Handle;
            NativeHotKey.Unregister(handle, NativeMethods.HOTKEY_DISMISS);
            if (!_popup.IsVisible) return;
            bool registered = NativeHotKey.Register(
                handle,
                NativeMethods.HOTKEY_DISMISS,
                NativeMethods.MOD_NOREPEAT,
                NativeMethods.VK_ESCAPE);
            DiagnosticLog.Write(
                "Popup dismiss hotkey " + (registered ? "registered" : "unavailable"));
        }

        private void RegisterConfiguredHotkeys()
        {
            if (_messageWindow == null) return;
            IntPtr handle = _messageWindow.Handle;
            NativeHotKey.Unregister(handle, NativeMethods.HOTKEY_WRITING);
            HotkeyGesture writing = ParsedHotkey(_settings.WritingHotkey, "F7");
            bool writingRegistered = NativeHotKey.Register(handle, NativeMethods.HOTKEY_WRITING,
                writing.Modifiers | NativeMethods.MOD_NOREPEAT, writing.VirtualKey);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_TRANSLATE);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_SCREENSHOT);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_SETTINGS);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_DISMISS);

            HotkeyGesture translate = ParsedHotkey(
                _settings.TranslateHotkey, "F8");
            HotkeyGesture screenshot = ParsedHotkey(
                _settings.OcrHotkey, "F9");
            HotkeyGesture settings = ParsedHotkey(
                _settings.SettingsHotkey, "F10");
            bool translateRegistered = NativeHotKey.Register(
                handle,
                NativeMethods.HOTKEY_TRANSLATE,
                translate.Modifiers | NativeMethods.MOD_NOREPEAT,
                translate.VirtualKey);
            bool screenshotRegistered = NativeHotKey.Register(
                handle,
                NativeMethods.HOTKEY_SCREENSHOT,
                screenshot.Modifiers | NativeMethods.MOD_NOREPEAT,
                screenshot.VirtualKey);
            bool settingsRegistered = NativeHotKey.Register(
                handle,
                NativeMethods.HOTKEY_SETTINGS,
                settings.Modifiers | NativeMethods.MOD_NOREPEAT,
                settings.VirtualKey);
            DiagnosticLog.Write(
                "Hotkeys registered; translate=" +
                translateRegistered +
                "; ocr=" + screenshotRegistered +
                "; settings=" + settingsRegistered);
            if (!writingRegistered || !translateRegistered ||
                !screenshotRegistered ||
                !settingsRegistered)
            {
                string message = "";
                if (!writingRegistered) message += writing.Display + " 外贸沟通助手不可用。请在设置中更换快捷键。\n";
                if (!translateRegistered)
                    message += translate.Display + " 选中翻译不可用。";
                if (!screenshotRegistered)
                    message +=
                        (message.Length == 0 ? "" : "\n") +
                        screenshot.Display + " OCR 翻译不可用。";
                if (!settingsRegistered)
                    message +=
                        (message.Length == 0 ? "" : "\n") +
                        settings.Display + " 打开设置不可用。";
                _tray.ShowBalloonTip(
                    4500,
                    "Sharkey 快捷键被占用",
                    message,
                    ToolTipIcon.Warning);
            }
        }

        private static HotkeyGesture ParsedHotkey(
            string value, string fallback)
        {
            HotkeyGesture gesture;
            string error;
            if (HotkeyGesture.TryParse(
                value, out gesture, out error))
                return gesture;
            HotkeyGesture.TryParse(
                fallback, out gesture, out error);
            return gesture;
        }

        private IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == NativeMethods.WM_HOTKEY &&
                wParam.ToInt32() == NativeMethods.HOTKEY_TRANSLATE)
            {
                DiagnosticLog.Write("Hotkey received");
                CaptureCurrentSelection();
                handled = true;
            }
            else if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == NativeMethods.HOTKEY_WRITING)
            {
                ShowWriting();
                handled = true;
            }
            else if (message == NativeMethods.WM_HOTKEY &&
                     wParam.ToInt32() == NativeMethods.HOTKEY_SCREENSHOT)
            {
                DiagnosticLog.Write("Screenshot hotkey received");
                CaptureScreenshotAndTranslate();
                handled = true;
            }
            else if (message == NativeMethods.WM_HOTKEY &&
                     wParam.ToInt32() == NativeMethods.HOTKEY_SETTINGS)
            {
                DiagnosticLog.Write("Settings hotkey received");
                ShowSettings();
                handled = true;
            }
            else if (message == NativeMethods.WM_HOTKEY &&
                     wParam.ToInt32() == NativeMethods.HOTKEY_DISMISS)
            {
                DiagnosticLog.Write("Popup dismiss hotkey received");
                if (_popup != null) _popup.HandleEscape();
                handled = true;
            }
            return IntPtr.Zero;
        }

        private void CreateTray()
        {
            _tray = new NotifyIcon
            {
                Text = "鲨译 Sharkey · 全局翻译",
                Icon = LoadTrayIcon(),
                Visible = true
            };
            var menu = new ContextMenuStrip();
            _settingsMenuItem = new ToolStripMenuItem(
                "鲨译设置",
                null,
                delegate { ShowSettings(); });
            _translateMenuItem = new ToolStripMenuItem(
                "翻译当前选区",
                null,
                delegate { CaptureCurrentSelection(); });
            _ocrMenuItem = new ToolStripMenuItem(
                "截图 OCR 翻译",
                null,
                delegate { CaptureScreenshotAndTranslate(); });
            _startupMenuItem = new ToolStripMenuItem(
                "开机自启动",
                null,
                delegate { ToggleStartupFromTray(); })
            {
                CheckOnClick = false
            };
            menu.Items.Add(_settingsMenuItem);
            menu.Items.Add(_translateMenuItem);
            menu.Items.Add(_ocrMenuItem);
            _writingMenuItem = new ToolStripMenuItem("外贸沟通助手", null, delegate { ShowWriting(); });
            menu.Items.Add(_writingMenuItem);
            menu.Items.Add("最近翻译…", null, delegate { new HistoryWindow(_settings, _client).Show(); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出鲨译", null, delegate { ExitApplication(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowSettings(); };
            UpdateTrayLabels();
        }

        private void UpdateTrayLabels()
        {
            if (_writingMenuItem != null) _writingMenuItem.Text = "外贸沟通助手  " + ParsedHotkey(_settings.WritingHotkey, "F7").Display;
            if (_settingsMenuItem == null) return;
            _settingsMenuItem.Text =
                "鲨译设置    " +
                ParsedHotkey(
                    _settings.SettingsHotkey, "F10").Display;
            _translateMenuItem.Text =
                "翻译当前选区    " +
                ParsedHotkey(
                    _settings.TranslateHotkey, "F8").Display;
            _ocrMenuItem.Text =
                "截图 OCR 翻译    " +
                ParsedHotkey(
                    _settings.OcrHotkey, "F9").Display;
            RefreshStartupMenu();
        }

        private void RefreshStartupMenu()
        {
            if (_startupMenuItem == null) return;
            _startupMenuItem.Checked = StartupManager.IsEnabled();
        }

        private void ToggleStartupFromTray()
        {
            bool enable = !StartupManager.IsEnabled();
            try
            {
                StartupManager.SetEnabled(enable);
                _settings.StartWithWindows = enable;
                _settings.Save();
                RefreshStartupMenu();
                if (_settingsWindow != null)
                    _settingsWindow.RefreshStartupState();
                _tray.ShowBalloonTip(
                    2500,
                    "鲨译",
                    enable
                        ? "已启用开机自启动。"
                        : "已关闭开机自启动。",
                    ToolTipIcon.Info);
            }
            catch (Exception ex)
            {
                RefreshStartupMenu();
                _tray.ShowBalloonTip(
                    4500,
                    "开机自启动设置失败",
                    ex.Message,
                    ToolTipIcon.Warning);
            }
        }

        private async void CaptureCurrentSelection()
        {
            // Invalidate any OCR result that is still being prepared so an
            // older F9 request cannot replace a newer F8 card.
            _ocrRequestId++;
            if (_ocrCancellation != null) _ocrCancellation.Cancel();
            if (_popup != null) _popup.DismissImmediately();
            await Task.Delay(60);
            await _monitor.CaptureAtCursorAsync();
        }

        private async void CaptureScreenshotAndTranslate()
        {
            if (_screenshotSelecting) return;
            if (_ocrCancellation != null) _ocrCancellation.Cancel();
            var activeCancellation = new CancellationTokenSource();
            _ocrCancellation = activeCancellation;
            int requestId = ++_ocrRequestId;
            Bitmap image = null;
            NativeMethods.RECT screenshotBounds = new NativeMethods.RECT();
            try
            {
                await Task.Delay(60, activeCancellation.Token);
                _popup.DismissImmediately();
                _screenshotSelecting = true;
                try
                {
                    using (var selector = new ScreenshotSelector())
                    {
                        if (selector.ShowDialog() != DialogResult.OK) return;
                        activeCancellation.Token.ThrowIfCancellationRequested();
                        image = selector.SelectedBitmap;
                        System.Drawing.Rectangle selectedBounds =
                            ReadSelectionBounds(selector);
                        System.Drawing.Rectangle virtualScreen =
                            SystemInformation.VirtualScreen;
                        screenshotBounds = new NativeMethods.RECT
                        {
                            Left = selectedBounds.Left + virtualScreen.Left,
                            Top = selectedBounds.Top + virtualScreen.Top,
                            Right = selectedBounds.Right + virtualScreen.Left,
                            Bottom = selectedBounds.Bottom + virtualScreen.Top
                        };
                    }
                }
                finally
                {
                    _screenshotSelecting = false;
                }
                if (image == null) return;
                if (!_settings.OcrAiFallback)
                    throw new InvalidOperationException(
                        "请在 OCR 设置中启用 AI 视觉识别。");

                EnsureOcrConsent();

                string aiText = await _client.RecognizeImageAsync(
                    image, _settings, activeCancellation.Token);
                var result = new OcrRecognitionResult
                {
                    Text = aiText,
                    Engine = "AI · " + _settings.ModelVendor + " · " +
                        _settings.GetActiveAiConnection().Model,
                };

                if (requestId != _ocrRequestId) return;

                DiagnosticLog.Write(
                    "OCR completed; engine=" + result.Engine +
                    "; characters=" + result.Text.Length);
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    NativeMethods.POINT emptyPoint;
                    NativeMethods.GetCursorPos(out emptyPoint);
                    _popup.ShowTransientNotice(
                        "选定区域中没有识别到文字。",
                        emptyPoint.X,
                        emptyPoint.Y);
                    return;
                }
                NativeMethods.POINT point;
                NativeMethods.GetCursorPos(out point);
                _popup.TranslateOcrAtBounds(
                    result,
                    screenshotBounds.Right > screenshotBounds.Left
                        ? screenshotBounds
                        : new NativeMethods.RECT
                        {
                            Left = point.X,
                            Top = point.Y,
                            Right = point.X,
                            Bottom = point.Y
                        },
                    _settings,
                    _client);
            }
            catch (OperationCanceledException)
            {
                DiagnosticLog.Write("OCR request canceled");
            }
            catch (Exception ex)
            {
                if (requestId != _ocrRequestId) return;
                DiagnosticLog.Write("OCR failed; type=" + ex.GetType().Name);
                NativeMethods.POINT errorPoint;
                NativeMethods.GetCursorPos(out errorPoint);
                _popup.ShowTransientNotice(
                    "截图识别失败：" + CompactError(ex.Message),
                    errorPoint.X,
                    errorPoint.Y);
            }
            finally
            {
                if (image != null) image.Dispose();
                if (ReferenceEquals(_ocrCancellation, activeCancellation))
                    _ocrCancellation = null;
                activeCancellation.Dispose();
            }
        }

        private static string CompactError(string message)
        {
            string value = message ?? "未知错误。";
            return value.Length > 120 ? value.Substring(0, 120) + "…" : value;
        }

        private static System.Drawing.Rectangle ReadSelectionBounds(
            ScreenshotSelector selector)
        {
            if (selector == null) return System.Drawing.Rectangle.Empty;
            FieldInfo field = typeof(ScreenshotSelector).GetField(
                "_selection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null) return System.Drawing.Rectangle.Empty;
            try
            {
                object value = field.GetValue(selector);
                return value is System.Drawing.Rectangle
                    ? (System.Drawing.Rectangle)value
                    : System.Drawing.Rectangle.Empty;
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write(
                    "Screenshot selection bounds unavailable; type=" +
                    ex.GetType().Name);
                return System.Drawing.Rectangle.Empty;
            }
        }

        private void ShowSettings()
        {
            if (_popup != null) _popup.Dismiss();
            if (!_settingsWindow.IsVisible) _settingsWindow.Show();
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }

        private void ShowOcrSettings()
        {
            _settingsWindow.ShowOcrSettings();
            ShowSettings();
        }

        private void EnsureOcrConsent()
        {
            string target = _settings.GetOcrConsentTarget();
            if (_settings.OcrAiConsentGranted &&
                string.Equals(_settings.OcrConsentTarget, target,
                    StringComparison.Ordinal))
                return;
            MessageBoxResult consent = System.Windows.MessageBox.Show(
                "截图将发送到 " + _settings.ModelVendor +
                " 的 AI 服务进行识别。截图可能包含私人信息。\n\n" +
                "允许上传本次和以后发往该服务的截图吗？",
                "确认截图上传", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (consent != MessageBoxResult.Yes)
                throw new OperationCanceledException();
            _settings.OcrAiConsentGranted = true;
            _settings.OcrConsentTarget = target;
            _settings.Save();
        }

        private void ShowModelSettings()
        {
            _settingsWindow.ShowModelSettings();
            ShowSettings();
        }

        private static Icon LoadTrayIcon()
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                var resource = Application.GetResourceStream(
                    new Uri("pack://application:,,,/assets/shark-logo.png"));
                if (resource == null) return SystemIcons.Information;
                using (resource.Stream)
                using (var bitmap = new Bitmap(resource.Stream))
                {
                    handle = bitmap.GetHicon();
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
            }
            catch
            {
                return SystemIcons.Information;
            }
            finally
            {
                if (handle != IntPtr.Zero) DestroyIcon(handle);
            }
        }

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(IntPtr handle);

        internal void ExitApplication()
        {
            if (_writingWindow != null)
                _writingWindow.CloseForExit();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _exiting = true;
            if (_updateTimer != null) _updateTimer.Stop();
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_popup != null) _popup.Close();
            if (_messageWindow != null)
            {
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_TRANSLATE);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_WRITING);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_SCREENSHOT);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_SETTINGS);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_DISMISS);
                _messageWindow.Dispose();
            }
            if (_monitor != null) _monitor.Dispose();
            if (_ocrCancellation != null) _ocrCancellation.Cancel();
            if (_updateCancellation != null) _updateCancellation.Cancel();
            if (_client != null) _client.Dispose();
            if (_updateService != null) _updateService.Dispose();
            if (_mutex != null) _mutex.Dispose();
            DiagnosticLog.Write("Application stopped");
            base.OnExit(e);
        }
    }

    internal static class NativeHotKey
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr window, int id, uint modifiers, uint key);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr window, int id);

        public static bool Register(IntPtr window, int id, uint modifiers, uint key)
        {
            return RegisterHotKey(window, id, modifiers, key);
        }
        public static bool Unregister(IntPtr window, int id)
        {
            return UnregisterHotKey(window, id);
        }
    }
}
