using System;
using System.Drawing;
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
        private OcrService _ocr;
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
        private bool _ocrBusy;

        [STAThread]
        public static void Main()
        {
            NativeMethods.EnablePerMonitorDpiAwareness();
            bool created;
            var mutex = new Mutex(
                true, "Sharkey.SingleInstance.75C36309", out created);
            if (!created)
            {
                System.Windows.MessageBox.Show(
                    "鲨译 Sharkey 已在运行。", "Sharkey");
                return;
            }

            var app = new TranslatorApplication { _mutex = mutex };
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.Run();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            _client = new TranslationClient();
            _ocr = new OcrService();
            _popup = new PopupWindow();
            _popup.OcrRecaptureRequested += delegate
            {
                CaptureScreenshotAndTranslate();
            };
            _popup.ModelSettingsRequested += delegate
            {
                ShowModelSettings();
            };
            _settingsWindow = new SettingsWindow(_settings);
            _settingsWindow.SettingsSaved += delegate
            {
                RegisterConfiguredHotkeys();
                UpdateTrayLabels();
                RefreshStartupMenu();
            };

            // Selection is captured only when the user presses F8. Do not install the
            // global mouse hook: selecting text by itself must never trigger translation.
            _monitor = new SelectionMonitor(Dispatcher) { Enabled = true };
            _monitor.TextSelected += delegate(object sender, SelectedTextEventArgs args)
            {
                DiagnosticLog.Write("Selection captured; characters=" + args.Text.Length);
                _popup.Translate(args.Text, args.X, args.Y, _settings, _client);
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
        }

        private bool HasConfiguredProvider()
        {
            if (_settings.Provider == "GoogleFree" || _settings.Provider == "MicrosoftFree") return true;
            if (_settings.Provider == "ModelApi")
                return new ModelConnectionSettings
                {
                    BaseUrl = _settings.ModelBaseUrl,
                    Model = _settings.ModelName,
                    ApiKey = _settings.ModelApiKey
                }.IsUsable(_settings.ModelVendor);
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

        private void RegisterConfiguredHotkeys()
        {
            if (_messageWindow == null) return;
            IntPtr handle = _messageWindow.Handle;
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_TRANSLATE);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_SCREENSHOT);
            NativeHotKey.Unregister(
                handle, NativeMethods.HOTKEY_SETTINGS);

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
            if (!translateRegistered ||
                !screenshotRegistered ||
                !settingsRegistered)
            {
                string message = "";
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
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(_startupMenuItem);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出鲨译", null, delegate { Shutdown(); });
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate { ShowSettings(); };
            UpdateTrayLabels();
        }

        private void UpdateTrayLabels()
        {
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
            await Task.Delay(60);
            await _monitor.CaptureAtCursorAsync();
        }

        private async void CaptureScreenshotAndTranslate()
        {
            if (_ocrBusy) return;
            _ocrBusy = true;
            await Task.Delay(60);
            _popup.Hide();
            Bitmap image = null;
            try
            {
                using (var selector = new ScreenshotSelector())
                {
                    if (selector.ShowDialog() != DialogResult.OK) return;
                    image = selector.SelectedBitmap;
                }
                if (image == null) return;

                OcrRecognitionResult result = null;
                string aiFailure = "";
                if (_settings.OcrAiFallback)
                {
                    try
                    {
                        string aiText = await _client.RecognizeImageAsync(
                            image, _settings, CancellationToken.None);
                        if (!string.IsNullOrWhiteSpace(aiText))
                        {
                            result = new OcrRecognitionResult
                            {
                                Text = aiText,
                                Engine = "DeepSeek Vision · " +
                                    _settings.OcrVisionModel,
                                QualityScore = 1,
                                IsLowQuality = false,
                                UsedAi = true
                            };
                        }
                    }
                    catch (Exception aiError)
                    {
                        DiagnosticLog.Write(
                            "AI vision OCR failed; type=" +
                            aiError.GetType().Name);
                        aiFailure = "DeepSeek Vision 不可用，已尝试本地 OCR：" +
                            aiError.Message;
                    }
                }

                if (result == null && _settings.OcrLocalFallback)
                {
                    try
                    {
                        var options = new OcrOptions
                        {
                            // Local OCR is an explicit offline fallback. Do not
                            // require or select a language pack from settings.
                            LanguageTag = "auto",
                            AutoEnhance = _settings.OcrAutoEnhance
                        };
                        result = await _ocr.RecognizeAsync(image, options);
                        if (!string.IsNullOrWhiteSpace(aiFailure))
                            result.Warning = AppendWarning(
                                result.Warning, aiFailure);
                    }
                    catch (Exception localError)
                    {
                        if (!string.IsNullOrWhiteSpace(aiFailure))
                            throw new InvalidOperationException(
                                aiFailure + "；本地 OCR 回退也不可用：" +
                                localError.Message);
                        throw;
                    }
                }
                if (result == null)
                {
                    throw new InvalidOperationException(
                        string.IsNullOrWhiteSpace(aiFailure)
                            ? "DeepSeek Vision 未返回识别文字。"
                            : aiFailure + " 请检查 OCR 设置中的模型与 API Key。" );
                }

                DiagnosticLog.Write(
                    "OCR completed; engine=" + result.Engine +
                    "; quality=" + result.QualityScore.ToString("0.00") +
                    "; characters=" + result.Text.Length);
                if (string.IsNullOrWhiteSpace(result.Text))
                {
                    System.Windows.MessageBox.Show(
                        "选定区域中没有识别到文字。" +
                        (string.IsNullOrEmpty(result.Warning)
                            ? ""
                            : "\n\n" + result.Warning),
                        "鲨译",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                NativeMethods.POINT point;
                NativeMethods.GetCursorPos(out point);
                _popup.TranslateOcr(
                    result, point.X, point.Y, _settings, _client);
                if (!string.IsNullOrEmpty(result.Warning))
                    _tray.ShowBalloonTip(
                        4000, "鲨译 OCR 提示", result.Warning,
                        ToolTipIcon.Warning);
            }
            catch (Exception ex)
            {
                DiagnosticLog.Write("OCR failed; type=" + ex.GetType().Name);
                System.Windows.MessageBox.Show(
                    "截图识别失败：" + ex.Message, "鲨译",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (image != null) image.Dispose();
                _ocrBusy = false;
            }
        }

        private static string AppendWarning(string current, string addition)
        {
            if (string.IsNullOrWhiteSpace(current)) return addition;
            return current + "\n" + addition;
        }

        private void ShowSettings()
        {
            if (!_settingsWindow.IsVisible) _settingsWindow.Show();
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
        }

        private void ShowOcrSettings()
        {
            _settingsWindow.ShowOcrSettings();
            ShowSettings();
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

        protected override void OnExit(ExitEventArgs e)
        {
            if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
            if (_messageWindow != null)
            {
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_TRANSLATE);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_SCREENSHOT);
                NativeHotKey.Unregister(_messageWindow.Handle, NativeMethods.HOTKEY_SETTINGS);
                _messageWindow.Dispose();
            }
            if (_monitor != null) _monitor.Dispose();
            if (_client != null) _client.Dispose();
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
