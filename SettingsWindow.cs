using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace GlobalTranslator
{
    internal sealed class SettingsWindow : Window
    {
        private static readonly Brush Navy = Brush("#0B2942");
        private static readonly Brush Ocean = Brush("#087EA4");
        private static readonly Brush Muted = Brush("#65798B");
        private static readonly Brush Line = Brush("#DCE9EF");
        private static readonly Brush Page = Brush("#F2F7FA");

        private readonly AppSettings _settings;
        private ComboBox _language;
        private TextBlock _targetRule;
        private TabControl _tabs;
        private Button _googleFree;
        private Button _microsoftFree;
        private Button _microsoftOfficial;
        private Button _googleOfficial;
        private Button _modelApi;
        private PasswordBox _googleKey;
        private PasswordBox _microsoftKey;
        private TextBox _region;
        private TextBox _modelBaseUrl;
        private PasswordBox _modelApiKey;
        private TextBox _modelName;
        private ComboBox _modelVendor;
        private CheckBox _ocrAiFallback;
        private CheckBox _ocrLocalFallback;
        private Button _copyDiagnostics;
        private Button _openDiagnosticFolder;
        private TextBox _ocrVisionModel;
        private Button _cancelSettings;
        private TextBox _translateHotkey;
        private TextBox _ocrHotkey;
        private TextBox _settingsHotkey;
        private CheckBox _startWithWindows;
        private ComboBox _popupFontSize;
        private bool _loadingValues;
        private string _pendingProvider = "GoogleFree";
        private string _pendingModelVendor = "Custom";
        private string _browsedProvider = "GoogleFree";
        private string _activeConfigSection = "Free";
        private Grid _providerPickerGrid;
        private ContentControl _providerConfigHost;
        private TextBlock _providerConfigTitle;
        private TextBlock _providerConfigState;
        private Button _activateProviderButton;
        private TextBlock _headerCurrentEngine;
        private Button _freeConfigButton;
        private Button _officialConfigButton;
        private Button _modelConfigButton;
        private readonly Dictionary<string, Button>
            _providerBrowseButtons =
                new Dictionary<string, Button>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, UIElement>
            _providerConfigViews =
                new Dictionary<string, UIElement>(
                    StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ModelConnectionSettings>
            _modelDrafts =
                new Dictionary<string, ModelConnectionSettings>(
                    StringComparer.OrdinalIgnoreCase);
        private string _activeModelVendor = "";

        public event EventHandler SettingsSaved;

        public SettingsWindow(AppSettings settings)
        {
            _settings = settings;
            Title = "鲨译 Sharkey · 设置";
            Width = 760;
            Height = 820;
            MinHeight = 690;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanMinimize;
            Background = Page;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            KeyDown += SettingsWindowKeyDown;

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Content = layout;

            Border header = BuildHeader();
            Grid.SetRow(header, 0);
            layout.Children.Add(header);

            _tabs = BuildProviderTabs();
            Grid.SetRow(_tabs, 1);
            layout.Children.Add(_tabs);

            Border footer = BuildFooter();
            Grid.SetRow(footer, 2);
            layout.Children.Add(footer);

            LoadValues();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            e.Cancel = true;
            DiscardAndHide();
            base.OnClosing(e);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (_startWithWindows != null && !_loadingValues)
                _startWithWindows.IsChecked =
                    StartupManager.IsEnabled();
        }

        public void RefreshStartupState()
        {
            if (_startWithWindows != null)
                _startWithWindows.IsChecked =
                    StartupManager.IsEnabled();
        }

        public void ShowOcrSettings()
        {
            _tabs.SelectedIndex = 1;
        }

        public void ShowModelSettings()
        {
            _tabs.SelectedIndex = 0;
            SelectConfigSection("Model");
            BrowseProvider(
                "ModelApi:" + NormalizeModelVendor(
                    _pendingModelVendor));
            if (_modelBaseUrl != null) _modelBaseUrl.Focus();
        }

        private Border BuildHeader()
        {
            var header = new Border
            {
                Margin = new Thickness(18, 16, 18, 10),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(18, 12, 18, 12),
                Background = new LinearGradientBrush(
                    Color.FromRgb(8, 42, 67), Color.FromRgb(7, 122, 155), 15)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = grid;

            var logo = new Image
            {
                Source = new BitmapImage(new Uri(
                    "pack://application:,,,/Sharkey;component/assets/shark-logo.png")),
                Width = 46,
                Height = 46,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(1, 0, 13, 0)
            };
            grid.Children.Add(logo);

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            title.Children.Add(new TextBlock
            {
                Text = "鲨译 · Sharkey",
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            });
            title.Children.Add(new TextBlock
            {
                Text = "全局翻译助手",
                FontSize = 11.5,
                Foreground = Brush("#BEEBF2"),
                Margin = new Thickness(1, 2, 0, 0)
            });
            grid.Children.Add(title);

            var engine = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(engine, 2);
            engine.Children.Add(new TextBlock
            {
                Text = "当前翻译引擎",
                Foreground = Brush("#BEEBF2"),
                FontSize = 10.5,
                HorizontalAlignment = HorizontalAlignment.Right
            });
            _headerCurrentEngine = new TextBlock
            {
                Text = "Google 免费",
                Foreground = Brushes.White,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 2, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Right
            };
            engine.Children.Add(_headerCurrentEngine);
            grid.Children.Add(engine);
            return header;
        }

        private Border BuildCommonSettings()
        {
            var card = Card();
            card.Padding = new Thickness(16, 13, 16, 13);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(245) });
            card.Child = grid;

            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock
            {
                Text = "翻译目标",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            _targetRule = new TextBlock
            {
                Text = "中文原文 → English · 其他语言 → 简体中文",
                FontSize = 11,
                Foreground = Muted,
                Margin = new Thickness(0, 3, 0, 0)
            };
            copy.Children.Add(_targetRule);
            grid.Children.Add(copy);

            _language = new ComboBox
            {
                Height = 38,
                Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new[]
                {
                    "自动（推荐）",
                    "简体中文  (zh-Hans)", "繁體中文  (zh-Hant)", "English  (en)",
                    "日本語  (ja)", "한국어  (ko)", "Français  (fr)",
                    "Deutsch  (de)", "Español  (es)"
                }
            };
            _language.SelectionChanged += delegate
            {
                UpdateTargetRule();
            };
            Grid.SetColumn(_language, 1);
            grid.Children.Add(_language);
            return card;
        }

        private TabControl BuildProviderTabs()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(18, 0, 18, 10),
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                FontSize = 13,
                TabStripPlacement = Dock.Left,
                Padding = new Thickness(0)
            };
            tabs.Items.Add(Tab("翻译", BuildTranslationTab()));
            tabs.Items.Add(Tab("OCR", BuildOcrTab()));
            tabs.Items.Add(Tab("快捷键", BuildShortcutTab()));
            tabs.Items.Add(Tab("常规", BuildGeneralTab()));
            return tabs;
        }

        private UIElement BuildTranslationTab()
        {
            var root = new Grid
            {
                Margin = new Thickness(22, 18, 18, 18),
                Background = Brushes.White
            };
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

            StackPanel intro = Intro("翻译", "");
            Grid.SetRow(intro, 0);
            root.Children.Add(intro);
            Border common = BuildCommonSettings();
            common.Margin = new Thickness(0, 10, 0, 10);
            Grid.SetRow(common, 1);
            root.Children.Add(common);

            Border picker = BuildProviderPicker();
            Grid.SetRow(picker, 2);
            root.Children.Add(picker);

            Border configuration = BuildProviderConfiguration();
            configuration.Margin = new Thickness(0, 10, 0, 0);
            Grid.SetRow(configuration, 3);
            root.Children.Add(configuration);
            return root;
        }

        private Border BuildProviderPicker()
        {
            var card = new Border
            {
                Background = Brushes.Transparent
            };
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = "引擎类型",
                Foreground = Navy,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(1, 0, 0, 7)
            });

            _providerPickerGrid = new Grid();
            for (int i = 0; i < 3; i++)
                _providerPickerGrid.ColumnDefinitions.Add(
                    new ColumnDefinition
                    {
                        Width = new GridLength(
                            1, GridUnitType.Star)
                    });
            _freeConfigButton =
                ConfigSectionButton("免费", "Free");
            _officialConfigButton =
                ConfigSectionButton("官方 API", "Official");
            _modelConfigButton =
                ConfigSectionButton("AI 模型", "Model");
            Button[] buttons =
            {
                _freeConfigButton,
                _officialConfigButton,
                _modelConfigButton
            };
            for (int i = 0; i < buttons.Length; i++)
            {
                buttons[i].Margin = new Thickness(
                    i == 0 ? 0 : 4,
                    0,
                    i == buttons.Length - 1 ? 0 : 4,
                    0);
                Grid.SetColumn(buttons[i], i);
                _providerPickerGrid.Children.Add(buttons[i]);
            }
            content.Children.Add(_providerPickerGrid);
            card.Child = content;
            return card;
        }

        private Border BuildProviderConfiguration()
        {
            BuildProviderConfigViews();

            var card = new Border
            {
                Background = Brushes.Transparent
            };
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            layout.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });

            var providerHeader = new Grid
            {
                Margin = new Thickness(0, 1, 0, 7)
            };
            providerHeader.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            providerHeader.Children.Add(new TextBlock
            {
                Text = "具体服务",
                Foreground = Navy,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            });
            layout.Children.Add(providerHeader);

            var serviceBar = new Border
            {
                Background = Brush("#F3F7F9"),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 9)
            };
            var serviceButtons = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            serviceBar.Child = serviceButtons;
            Grid.SetRow(serviceBar, 1);
            layout.Children.Add(serviceBar);

            var configCard = Card();
            configCard.Padding = new Thickness(16, 13, 16, 15);
            var configLayout = new Grid();
            configLayout.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            configLayout.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            var configHeader = new Grid();
            configHeader.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            configHeader.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            var configHeading = new StackPanel();
            _providerConfigTitle = new TextBlock
            {
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            };
            _providerConfigState = new TextBlock
            {
                FontSize = 11,
                Foreground = Muted,
                Margin = new Thickness(0, 2, 0, 0)
            };
            configHeading.Children.Add(_providerConfigTitle);
            configHeading.Children.Add(_providerConfigState);
            configHeader.Children.Add(configHeading);
            _activateProviderButton =
                PrimaryButton("设为当前引擎");
            _activateProviderButton.Width = 126;
            _activateProviderButton.Height = 34;
            _activateProviderButton.Click += ActivateBrowsedProviderClick;
            Grid.SetColumn(_activateProviderButton, 1);
            configHeader.Children.Add(_activateProviderButton);
            configLayout.Children.Add(configHeader);

            _providerConfigHost = new ContentControl
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
                Margin = new Thickness(0, 13, 0, 0)
            };
            _providerConfigHost.Tag = serviceButtons;
            Grid.SetRow(_providerConfigHost, 1);
            configLayout.Children.Add(_providerConfigHost);
            configCard.Child = configLayout;
            Grid.SetRow(configCard, 2);
            layout.Children.Add(configCard);
            card.Child = layout;
            SelectConfigSection("Free");
            return card;
        }

        private void BuildProviderConfigViews()
        {
            var microsoftContent = new StackPanel();
            _microsoftKey = AddPassword(microsoftContent, "API Key", "粘贴 Microsoft Translator 密钥");
            _microsoftKey.PasswordChanged += delegate
            {
                UpdateProviderSelection();
            };
            _region = AddText(microsoftContent, "Region", "例如 eastasia");
            var googleContent = new StackPanel();
            _googleKey = AddPassword(googleContent, "API Key", "粘贴 Google Cloud Translation 密钥");
            _googleKey.PasswordChanged += delegate
            {
                UpdateProviderSelection();
            };
            var content = new StackPanel();
            _modelVendor = new ComboBox
            {
                ItemsSource = ModelVendors(),
                Visibility = Visibility.Collapsed
            };
            _modelVendor.SelectionChanged += ModelVendorChanged;
            content.Children.Add(_modelVendor);
            _modelBaseUrl = AddText(
                content, "API 地址", "例如 https://api.openai.com/v1");
            _modelName = AddText(
                content, "模型名称", "例如 gpt-5.6-sol / deepseek-chat");
            _modelApiKey = AddPassword(
                content, "API Key", "本地 Ollama 可留空");
            _modelBaseUrl.TextChanged += delegate
            {
                UpdateProviderSelection();
            };
            _modelName.TextChanged += delegate
            {
                UpdateProviderSelection();
            };
            _modelApiKey.PasswordChanged += delegate
            {
                UpdateProviderSelection();
            };

            _providerConfigViews["GoogleFree"] =
                ProviderConfigPanel("Google 免费", "无需账号或密钥", null);
            _providerConfigViews["MicrosoftFree"] =
                ProviderConfigPanel("Microsoft 免费", "无需账号或密钥", null);
            _providerConfigViews["Microsoft"] =
                ProviderConfigPanel("Microsoft Translator", "需要订阅密钥", microsoftContent);
            _providerConfigViews["Google"] =
                ProviderConfigPanel("Google Cloud Translation", "需要 API Key", googleContent);
            UIElement modelPanel = ProviderConfigPanel(
                "AI 模型",
                "OpenAI 兼容接口",
                content);
            foreach (ModelVendorChoice vendor in ModelVendors())
                _providerConfigViews[
                    "ModelApi:" + vendor.Code] = modelPanel;
        }

        private UIElement BuildShortcutTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "全局快捷键",
                "点击快捷键框后直接按下新组合。功能键可单独使用；字母和数字需要搭配 Ctrl、Alt 或 Shift。"));

            var shortcutCard = Card();
            shortcutCard.Padding = new Thickness(16);
            shortcutCard.Margin = new Thickness(0, 14, 0, 12);
            var shortcutContent = new StackPanel();
            shortcutContent.Children.Add(new TextBlock
            {
                Text = "全局快捷键",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy,
                Margin = new Thickness(0, 0, 0, 10)
            });
            _translateHotkey = AddHotkeyRecorder(
                shortcutContent,
                "选中文字翻译",
                "选中文字后触发翻译");
            _ocrHotkey = AddHotkeyRecorder(
                shortcutContent,
                "截图 OCR 翻译",
                "进入屏幕框选并识别翻译");
            _settingsHotkey = AddHotkeyRecorder(
                shortcutContent,
                "打开设置",
                "随时打开 Sharkey 设置页");
            var reset = new Button
            {
                Content = "恢复默认 F8 / F9 / F10",
                Height = 34,
                Padding = new Thickness(13, 0, 13, 0),
                Margin = new Thickness(0, 4, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Background = Brushes.White,
                Foreground = Ocean,
                BorderBrush = Brush("#9BCEDB"),
                Cursor = Cursors.Hand
            };
            reset.Click += delegate
            {
                _translateHotkey.Text = "F8";
                _ocrHotkey.Text = "F9";
                _settingsHotkey.Text = "F10";
            };
            shortcutContent.Children.Add(reset);
            shortcutCard.Child = shortcutContent;
            root.Children.Add(shortcutCard);
            root.Children.Add(InfoBox(
                "若组合键被其他软件占用，保存后 Sharkey 会明确提示未注册成功的快捷键。"));
            return Scroll(root);
        }

        private UIElement BuildGeneralTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "常规",
                "管理 Windows 启动行为，并查看当前开发版本。"));

            var startupCard = Card();
            startupCard.Padding = new Thickness(16);
            startupCard.Margin = new Thickness(0, 14, 0, 12);
            var startupContent = new StackPanel();
            startupContent.Children.Add(new TextBlock
            {
                Text = "Windows 启动",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            _startWithWindows = new CheckBox
            {
                Content = "登录 Windows 后自动启动 Sharkey",
                Margin = new Thickness(0, 10, 0, 5),
                Foreground = Navy,
                FontSize = 12.5
            };
            startupContent.Children.Add(_startWithWindows);
            startupContent.Children.Add(new TextBlock
            {
                Text = "仅为当前 Windows 用户启用。托盘右键也可立即切换，两处状态会自动同步。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 10.5
            });
            startupCard.Child = startupContent;
            root.Children.Add(startupCard);

            var versionCard = Card();
            var reading = new StackPanel { Margin = new Thickness(0, 0, 0, 16) };
            reading.Children.Add(new TextBlock { Text = "浮窗字号", FontSize = 13, Foreground = Navy });
            _popupFontSize = new ComboBox
            {
                ItemsSource = new[] { "小", "标准", "大" },
                SelectedIndex = 1,
                Width = 160,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 8, 0, 0),
                ToolTip = "用于选中翻译与截图翻译的正文；保存后下一次翻译生效"
            };
            reading.Children.Add(_popupFontSize);
            root.Children.Add(reading);
            versionCard.Padding = new Thickness(16);
            var version = new StackPanel();
            version.Children.Add(new TextBlock
            {
                Text = "关于鲨译",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            version.Children.Add(new TextBlock
            {
                Text = "Sharkey  " + VersionInfo.SemanticVersion,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Ocean,
                Margin = new Thickness(0, 8, 0, 3)
            });
            version.Children.Add(new TextBlock
            {
                Text = "Windows x64 · 本地优先的全局翻译助手",
                Foreground = Muted,
                FontSize = 11
            });
            versionCard.Child = version;
            root.Children.Add(versionCard);

            var diagnosticsCard = Card();
            diagnosticsCard.Padding = new Thickness(16);
            diagnosticsCard.Margin = new Thickness(0, 12, 0, 0);
            var diagnostics = new StackPanel();
            diagnostics.Children.Add(new TextBlock
            {
                Text = "诊断与支持",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            diagnostics.Children.Add(new TextBlock
            {
                Text = "若其他电脑无法启动或快捷键无响应，复制诊断信息并附上日志文件即可定位。诊断摘要不包含 API Key 或翻译原文。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 10.5,
                Margin = new Thickness(0, 6, 0, 10)
            });
            var diagnosticActions = new StackPanel
            {
                Orientation = Orientation.Horizontal
            };
            _copyDiagnostics = SecondaryButton("复制诊断信息");
            _copyDiagnostics.Margin = new Thickness(0, 0, 8, 0);
            _copyDiagnostics.Click += CopyDiagnosticsClick;
            diagnosticActions.Children.Add(_copyDiagnostics);
            _openDiagnosticFolder = SecondaryButton("打开日志目录");
            _openDiagnosticFolder.Click += OpenDiagnosticFolderClick;
            diagnosticActions.Children.Add(_openDiagnosticFolder);
            diagnostics.Children.Add(diagnosticActions);
            diagnosticsCard.Child = diagnostics;
            root.Children.Add(diagnosticsCard);
            return Scroll(root);
        }

        private void CopyDiagnosticsClick(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(DiagnosticLog.BuildSupportSummary());
                _copyDiagnostics.Content = "已复制";
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    "复制诊断信息失败：" + error.Message,
                    "Sharkey",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void OpenDiagnosticFolderClick(object sender, RoutedEventArgs e)
        {
            try
            {
                DiagnosticLog.Write("Diagnostic folder requested");
                Directory.CreateDirectory(DiagnosticLog.FolderPath);
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + DiagnosticLog.FilePath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                MessageBox.Show(
                    "无法打开日志目录：" + error.Message + "\n\n" +
                    DiagnosticLog.FolderPath,
                    "Sharkey",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private TextBox AddHotkeyRecorder(
            Panel parent, string label, string description)
        {
            var row = new Grid
            {
                Margin = new Thickness(0, 0, 0, 10)
            };
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            row.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(190)
            });
            var copy = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            copy.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Navy,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });
            copy.Children.Add(new TextBlock
            {
                Text = description,
                Foreground = Muted,
                FontSize = 10.5,
                Margin = new Thickness(0, 2, 0, 0)
            });
            row.Children.Add(copy);
            var recorder = new TextBox
            {
                Height = 38,
                IsReadOnly = true,
                Cursor = Cursors.Hand,
                TextAlignment = TextAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.Bold,
                Foreground = Ocean,
                Background = Brush("#F6FBFC"),
                BorderBrush = Brush("#9BCEDB"),
                ToolTip = "点击后按下快捷键"
            };
            recorder.PreviewKeyDown += HotkeyRecorderKeyDown;
            recorder.GotKeyboardFocus += delegate
            {
                recorder.Background = Brush("#E9F8FB");
                recorder.BorderBrush = Ocean;
            };
            recorder.LostKeyboardFocus += delegate
            {
                recorder.Background = Brush("#F6FBFC");
                recorder.BorderBrush = Brush("#9BCEDB");
            };
            Grid.SetColumn(recorder, 1);
            row.Children.Add(recorder);
            parent.Children.Add(row);
            return recorder;
        }

        private void HotkeyRecorderKeyDown(
            object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Tab) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            HotkeyGesture gesture;
            string error;
            if (HotkeyGesture.TryFromKeyEvent(
                key,
                Keyboard.Modifiers,
                out gesture,
                out error))
            {
                ((TextBox)sender).Text = gesture.Display;
                ((TextBox)sender).ToolTip = "已录制 " + gesture.Display;
            }
            else
                ((TextBox)sender).ToolTip = error;
            e.Handled = true;
        }

        private UIElement BuildOcrTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "截图 OCR",
                "默认使用 DeepSeek Vision 读取截图；无需下载 Windows 多语言 OCR 包。"));

            var aiCard = Card();
            aiCard.Padding = new Thickness(16);
            var ai = new StackPanel();
            ai.Children.Add(new TextBlock
            {
                Text = "DeepSeek Vision",
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            ai.Children.Add(new TextBlock
            {
                Text = "识别结果只要求转录，不翻译、不纠错；原文仍可编辑后重新翻译。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 9)
            });
            _ocrAiFallback = new CheckBox
            {
                Content = "使用 DeepSeek Vision 作为默认截图识别引擎",
                Margin = new Thickness(0, 0, 0, 7),
                Foreground = Navy,
                FontSize = 12
            };
            ai.Children.Add(_ocrAiFallback);
            _ocrVisionModel = AddText(
                ai, "视觉模型名称", "deepseek-v4-flash-vision-exp");
            ai.Children.Add(new TextBlock
            {
                Text = "API 地址和 Key 复用“AI 模型”页的 DeepSeek 配置；截图只在内存中编码。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#397080"),
                FontSize = 10.5,
                Margin = new Thickness(24, 0, 0, 0)
            });
            var configureDeepSeek = SecondaryButton("配置 DeepSeek API");
            configureDeepSeek.Width = 142;
            configureDeepSeek.Height = 34;
            configureDeepSeek.Margin = new Thickness(24, 10, 0, 0);
            configureDeepSeek.Click += delegate
            {
                _tabs.SelectedIndex = 0;
                SelectConfigSection("Model");
                BrowseProvider("ModelApi:DeepSeek");
                if (_modelApiKey != null) _modelApiKey.Focus();
            };
            ai.Children.Add(configureDeepSeek);
            aiCard.Child = ai;
            root.Children.Add(aiCard);

            var fallbackCard = Card();
            fallbackCard.Padding = new Thickness(16);
            fallbackCard.Margin = new Thickness(0, 12, 0, 12);
            var fallback = new StackPanel();
            fallback.Children.Add(new TextBlock
            {
                Text = "离线回退（可选）",
                FontSize = 13.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            _ocrLocalFallback = new CheckBox
            {
                Content = "AI 请求失败时尝试 Windows 本地 OCR（使用已安装语言）",
                Margin = new Thickness(0, 9, 0, 4),
                Foreground = Navy,
                FontSize = 12
            };
            fallback.Children.Add(_ocrLocalFallback);
            fallback.Children.Add(new TextBlock
            {
                Text = "本地识别不会自动下载或安装语言包；未安装可用识别器时，将提示配置视觉 API。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 10.5
            });
            fallbackCard.Child = fallback;
            root.Children.Add(fallbackCard);
            return Scroll(root);
        }

 #if LEGACY_OCR_COMPONENT_UI
        private Border BuildEnglishOcrCard()
        {
            var card = Card();
            card.Padding = new Thickness(16);
            card.Margin = new Thickness(0, 14, 0, 0);
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = "English 语言组件",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });

            content.Children.Add(ComponentStatusRow(
                "English Basic",
                "OCR 的基础语言资源，也可能被英文语音、手写等功能使用。",
                out _englishBasicStatus));
            content.Children.Add(ComponentStatusRow(
                "English OCR",
                "鲨译英文识别必需，避免大小写和断词异常。",
                out _englishOcrStatus));

            _ocrTaskProgress = new ProgressBar
            {
                Height = 7,
                Minimum = 0,
                Maximum = 100,
                Margin = new Thickness(0, 13, 0, 6),
                Visibility = Visibility.Collapsed
            };
            content.Children.Add(_ocrTaskProgress);
            _ocrTaskStatus = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = Ocean,
                FontSize = 11,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 10)
            };
            content.Children.Add(_ocrTaskStatus);

            var actions = new WrapPanel
            {
                Orientation = Orientation.Horizontal
            };
            _installEnglishOcr = PrimaryButton("安装缺失组件");
            _installEnglishOcr.Width = 142;
            _installEnglishOcr.Height = 38;
            _installEnglishOcr.Click += InstallEnglishOcrClick;
            actions.Children.Add(_installEnglishOcr);
            _removeEnglishOcr = SecondaryButton("卸载 OCR");
            _removeEnglishOcr.Margin = new Thickness(9, 0, 0, 0);
            _removeEnglishOcr.Click += RemoveEnglishOcrClick;
            actions.Children.Add(_removeEnglishOcr);
            _removeEnglishAll = SecondaryButton("卸载全部");
            _removeEnglishAll.Margin = new Thickness(9, 0, 0, 0);
            _removeEnglishAll.Click += RemoveEnglishAllClick;
            actions.Children.Add(_removeEnglishAll);
            _copyEnglishOcrCommand = new Button
            {
                Content = "复制命令",
                Height = 38,
                Margin = new Thickness(9, 0, 0, 0),
                Padding = new Thickness(14, 0, 14, 0),
                Background = Brushes.White,
                Foreground = Ocean,
                BorderBrush = Brush("#9BCEDB"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                FontWeight = FontWeights.SemiBold
            };
            _copyEnglishOcrCommand.Click += CopyEnglishOcrCommandClick;
            actions.Children.Add(_copyEnglishOcrCommand);
            content.Children.Add(actions);
            content.Children.Add(new TextBlock
            {
                Text = "安装和卸载都会弹出 UAC。任务可以在设置隐藏后继续；为保护 Windows 组件服务，运行中不提供强制取消。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 10.5,
                Margin = new Thickness(0, 9, 0, 0)
            });
            card.Child = content;
            UpdateEnglishOcrStatus();
            return card;
        }

        private static Border ComponentStatusRow(
            string name, string description, out TextBlock status)
        {
            var row = new Border
            {
                Background = Brush("#F6FAFC"),
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 9, 0, 0)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            var copy = new StackPanel();
            copy.Children.Add(new TextBlock
            {
                Text = name,
                Foreground = Navy,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12
            });
            copy.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 10,
                Margin = new Thickness(0, 2, 12, 0)
            });
            grid.Children.Add(copy);
            status = new TextBlock
            {
                VerticalAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                FontSize = 11
            };
            Grid.SetColumn(status, 1);
            grid.Children.Add(status);
            row.Child = grid;
            return row;
        }

        private void UpdateEnglishOcrStatus()
        {
            OcrComponentProgress progress =
                OcrLanguagePackManager.IsRunning
                    ? OcrLanguagePackManager.CurrentProgress
                    : OcrLanguagePackManager.GetInstalledSnapshot();
            ApplyOcrProgress(progress);
        }

        private async void InstallEnglishOcrClick(
            object sender, RoutedEventArgs e)
        {
            MessageBoxResult consent = MessageBox.Show(
                "将通过 Windows 可选功能安装 English (United States) Basic 与 OCR 组件。\n\n" +
                "此操作需要联网、管理员权限，可能持续几分钟，并会弹出 UAC。是否继续？",
                "安装 English OCR",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);
            if (consent != MessageBoxResult.Yes) return;

            OcrPackInstallResult result =
                await OcrLanguagePackManager.StartAsync(
                    OcrComponentAction.InstallRequired);
            if (result.Installed)
                RefreshOcrLanguagesAndSelectEnglish();
            UpdateEnglishOcrStatus();
        }

        private async void RemoveEnglishOcrClick(
            object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "将卸载 English OCR。鲨译仍可自动识别其他已安装语言，但英文准确率会下降。是否继续？",
                "卸载 English OCR",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            await OcrLanguagePackManager.StartAsync(
                OcrComponentAction.RemoveOcr);
            UpdateEnglishOcrStatus();
        }

        private async void RemoveEnglishAllClick(
            object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show(
                "将先卸载 English OCR，再尝试卸载 English Basic。\n\n" +
                "如果 Basic 仍被语音、手写等英文功能使用，鲨译会保留它并说明原因。是否继续？",
                "卸载 English 组件",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            await OcrLanguagePackManager.StartAsync(
                OcrComponentAction.RemoveAll);
            UpdateEnglishOcrStatus();
        }

        private void OcrProgressChanged(
            object sender, OcrComponentProgressEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(
                delegate { ApplyOcrProgress(e.Progress); }));
        }

        private void ApplyOcrProgress(OcrComponentProgress progress)
        {
            if (_englishBasicStatus == null ||
                _englishOcrStatus == null)
                return;
            SetCapabilityStatus(
                _englishBasicStatus, progress.BasicState,
                progress.BasicState == OcrCapabilityState.Unknown
                    ? "待管理员确认"
                    : null);
            SetCapabilityStatus(
                _englishOcrStatus, progress.OcrState, null);
            bool running = !progress.IsCompleted &&
                           progress.Stage !=
                               OcrComponentTaskStage.Idle;
            _ocrTaskProgress.Visibility = running
                ? Visibility.Visible
                : Visibility.Collapsed;
            _ocrTaskStatus.Visibility = running ||
                progress.Stage == OcrComponentTaskStage.Failed ||
                progress.Stage == OcrComponentTaskStage.Cancelled
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            _ocrTaskProgress.IsIndeterminate =
                running && progress.Percent < 0;
            if (progress.Percent >= 0)
                _ocrTaskProgress.Value = progress.Percent;
            _ocrTaskStatus.Text = progress.Message +
                (running
                    ? "  ·  已用时 " +
                      FormatElapsed(progress.Elapsed) +
                      (progress.Percent >= 0
                          ? "  ·  " + progress.Percent + "%"
                          : "")
                    : "");
            _ocrTaskStatus.Foreground =
                progress.Stage == OcrComponentTaskStage.Failed
                    ? Brush("#A33A3A")
                    : Ocean;
            _installEnglishOcr.IsEnabled =
                !running &&
                progress.OcrState != OcrCapabilityState.Installed;
            _removeEnglishOcr.IsEnabled =
                !running &&
                progress.OcrState == OcrCapabilityState.Installed;
            _removeEnglishAll.IsEnabled = !running &&
                (progress.OcrState == OcrCapabilityState.Installed ||
                 progress.BasicState == OcrCapabilityState.Installed);
            _copyEnglishOcrCommand.IsEnabled = !running;
        }

        private static void SetCapabilityStatus(
            TextBlock target,
            OcrCapabilityState state,
            string unknownText)
        {
            if (state == OcrCapabilityState.Installed)
            {
                target.Text = "✓ 已安装";
                target.Foreground = Brush("#137A57");
            }
            else if (state == OcrCapabilityState.NotInstalled)
            {
                target.Text = "未安装";
                target.Foreground = Brush("#A45B00");
            }
            else
            {
                target.Text = unknownText ?? "状态未知";
                target.Foreground = Muted;
            }
        }

        private static string FormatElapsed(TimeSpan elapsed)
        {
            if (elapsed.TotalHours >= 1)
                return string.Format(
                    "{0}:{1:00}:{2:00}",
                    (int)elapsed.TotalHours,
                    elapsed.Minutes, elapsed.Seconds);
            return string.Format(
                "{0}:{1:00}",
                (int)elapsed.TotalMinutes, elapsed.Seconds);
        }

        private void CopyEnglishOcrCommandClick(
            object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(
                    OcrLanguagePackManager.ManualInstallCommand);
                MessageBox.Show(
                    "安装命令已复制。\n\n请打开“管理员 Windows PowerShell”粘贴运行，完成后重新打开 OCR 设置页检查。",
                    "已复制",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "复制失败：" + ex.Message,
                    "鲨译",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private void RefreshOcrLanguagesAndSelectEnglish()
        {
            _ocrLanguage.ItemsSource = AvailableOcrLanguages();
            SelectOcrLanguage("en");
        }

 #endif
        private Border BuildFooter()
        {
            var footer = new Border
            {
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(20, 13, 20, 15)
            };
            var dock = new DockPanel();
            footer.Child = dock;
            var save = PrimaryButton("保存并应用");
            save.Width = 140;
            save.Height = 42;
            save.Click += SaveClick;
            DockPanel.SetDock(save, Dock.Right);
            dock.Children.Add(save);

            _cancelSettings = new Button
            {
                Content = "取消",
                Width = 88,
                Height = 42,
                Margin = new Thickness(0, 0, 10, 0),
                Background = Brushes.White,
                Foreground = Navy,
                BorderBrush = Brush("#B9D0DA"),
                BorderThickness = new Thickness(1),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                ToolTip = "放弃未保存的修改并关闭设置"
            };
            _cancelSettings.Click += CancelClick;
            DockPanel.SetDock(_cancelSettings, Dock.Right);
            dock.Children.Add(_cancelSettings);

            dock.Children.Add(new TextBlock
            {
                Text = "🔒  密钥由 Windows DPAPI 加密，仅当前账户可读取",
                Foreground = Muted,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center
            });
            return footer;
        }

        private void CancelClick(object sender, RoutedEventArgs e)
        {
            DiscardAndHide();
        }

        private void SettingsWindowKeyDown(
            object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            DiscardAndHide();
            e.Handled = true;
        }

        private void DiscardAndHide()
        {
            LoadValues();
            Hide();
        }

        private void LoadValues()
        {
            _loadingValues = true;
            try
            {
                _language.SelectedIndex =
                    string.Equals(
                        _settings.TargetLanguageMode,
                        "Fixed",
                        StringComparison.OrdinalIgnoreCase)
                        ? LanguageIndex(
                            _settings.TargetLanguage) + 1
                        : 0;
                UpdateTargetRule();
                _googleKey.Password = _settings.GoogleApiKey;
                _microsoftKey.Password = _settings.MicrosoftApiKey;
                _region.Text = _settings.MicrosoftRegion;
                LoadModelDrafts();
                SelectModelVendor(
                    _settings.ModelVendor,
                    _settings.ModelBaseUrl);
                var selectedVendor =
                    _modelVendor.SelectedItem as ModelVendorChoice;
                _activeModelVendor = selectedVendor == null
                    ? "Custom"
                    : selectedVendor.Code;
                LoadModelDraft(_activeModelVendor);
                _pendingModelVendor =
                    NormalizeModelVendor(_settings.ModelVendor);
                if (_ocrLocalFallback != null)
                    _ocrLocalFallback.IsChecked = _settings.OcrLocalFallback;
                if (_ocrAiFallback != null)
                    _ocrAiFallback.IsChecked = _settings.OcrAiFallback;
                if (_ocrVisionModel != null)
                    _ocrVisionModel.Text = _settings.OcrVisionModel;
                _translateHotkey.Text =
                    NormalizeHotkey(_settings.TranslateHotkey, "F8");
                _ocrHotkey.Text =
                    NormalizeHotkey(_settings.OcrHotkey, "F9");
                _settingsHotkey.Text =
                    NormalizeHotkey(_settings.SettingsHotkey, "F10");
                _startWithWindows.IsChecked =
                    StartupManager.IsEnabled();
                _popupFontSize.SelectedIndex = _settings.PopupFontSize == "Small" ? 0 : _settings.PopupFontSize == "Large" ? 2 : 1;
                _pendingProvider =
                    IsKnownProvider(_settings.Provider)
                        ? _settings.Provider
                        : "GoogleFree";
                _browsedProvider =
                    _pendingProvider == "ModelApi"
                        ? "ModelApi:" + _pendingModelVendor
                        : _pendingProvider;
                SelectConfigSection(
                    ConfigSectionForProvider(_pendingProvider));
                BrowseProvider(_browsedProvider);
                UpdateProviderSelection();
                _tabs.SelectedIndex = 0;
            }
            finally
            {
                _loadingValues = false;
            }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            HotkeyGesture translateGesture;
            HotkeyGesture ocrGesture;
            HotkeyGesture settingsGesture;
            if (!ValidateHotkeys(
                out translateGesture,
                out ocrGesture,
                out settingsGesture))
                return;

            bool enableAiOcr = _ocrAiFallback != null &&
                _ocrAiFallback.IsChecked == true;
            bool grantOcrConsent = false;
            if (enableAiOcr &&
                string.IsNullOrWhiteSpace(
                    _ocrVisionModel == null
                        ? ""
                        : _ocrVisionModel.Text))
            {
                _tabs.SelectedIndex = 1;
                MessageBox.Show(
                    "请填写视觉模型名称；DeepSeek API 地址和 Key 在“AI 模型 → DeepSeek”中配置。",
                    "鲨译", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (enableAiOcr && !_settings.OcrAiConsentGranted)
            {
                MessageBoxResult consent = MessageBox.Show(
                    "开启后，所选截图会发送到你配置的 DeepSeek Vision 服务。\n\n" +
                    "截图可能包含隐私信息。确定允许上传吗？",
                    "启用 AI 视觉 OCR",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (consent != MessageBoxResult.Yes) return;
                grantOcrConsent = true;
            }

            SaveActiveModelDraft();
            if (!IsProviderReady(_pendingProvider))
            {
                _tabs.SelectedIndex = 0;
                SelectConfigSection(
                    ConfigSectionForProvider(_pendingProvider));
                BrowseProvider(
                    _pendingProvider == "ModelApi"
                        ? "ModelApi:" + _pendingModelVendor
                        : _pendingProvider);
                MessageBox.Show(
                    ProviderDisplayName(_pendingProvider) +
                    " 的必要配置尚未填写完整。请补充配置，或选择其他可用服务。",
                    "当前翻译服务不可用",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _settings.Provider = _pendingProvider;
            _settings.TargetLanguageMode =
                _language.SelectedIndex <= 0
                    ? "Smart"
                    : "Fixed";
            if (_language.SelectedIndex > 0)
                _settings.TargetLanguage =
                    LanguageCode(_language.SelectedIndex - 1);
            _settings.GoogleApiKey = _googleKey.Password.Trim();
            _settings.MicrosoftApiKey = _microsoftKey.Password.Trim();
            _settings.MicrosoftRegion = _region.Text.Trim();
            _settings.ModelVendor =
                NormalizeModelVendor(_pendingModelVendor);
            foreach (KeyValuePair<string, ModelConnectionSettings>
                profile in _modelDrafts)
                _settings.SetModelConnection(
                    profile.Key, profile.Value);
            ModelConnectionSettings activeConnection =
                _modelDrafts[_settings.ModelVendor];
            _settings.ModelBaseUrl = activeConnection.BaseUrl;
            _settings.ModelName = activeConnection.Model;
            _settings.ModelApiKey = activeConnection.ApiKey;
            _settings.OcrAiFallback = enableAiOcr;
            _settings.OcrLocalFallback = _ocrLocalFallback == null ||
                _ocrLocalFallback.IsChecked == true;
            _settings.OcrVisionModel = _ocrVisionModel == null
                ? "deepseek-v4-flash-vision-exp"
                : _ocrVisionModel.Text.Trim();
            _settings.AutoTranslate = false;
            _settings.TranslateHotkey = translateGesture.Display;
            _settings.OcrHotkey = ocrGesture.Display;
            _settings.SettingsHotkey = settingsGesture.Display;
            _settings.StartWithWindows =
                _startWithWindows.IsChecked == true;
            _settings.PopupFontSize = _popupFontSize.SelectedIndex == 0 ? "Small" : _popupFontSize.SelectedIndex == 2 ? "Large" : "Standard";
            try
            {
                StartupManager.SetEnabled(
                    _settings.StartWithWindows);
                if (grantOcrConsent)
                    _settings.OcrAiConsentGranted = true;
                _settings.Save();
                var handler = SettingsSaved;
                if (handler != null) handler(this, EventArgs.Empty);
                Hide();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "保存失败：" + ex.Message, "鲨译",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateHotkeys(
            out HotkeyGesture translate,
            out HotkeyGesture ocr,
            out HotkeyGesture settings)
        {
            translate = null;
            ocr = null;
            settings = null;
            string error;
            if (!HotkeyGesture.TryParse(
                _translateHotkey.Text,
                out translate,
                out error))
                return ShowHotkeyError(
                    "选中文字翻译", error, _translateHotkey);
            if (!HotkeyGesture.TryParse(
                _ocrHotkey.Text,
                out ocr,
                out error))
                return ShowHotkeyError(
                    "截图 OCR 翻译", error, _ocrHotkey);
            if (!HotkeyGesture.TryParse(
                _settingsHotkey.Text,
                out settings,
                out error))
                return ShowHotkeyError(
                    "打开设置", error, _settingsHotkey);
            if (translate.SameAs(ocr) ||
                translate.SameAs(settings) ||
                ocr.SameAs(settings))
                return ShowHotkeyError(
                    "快捷键冲突",
                    "三项功能不能使用相同的快捷键。",
                    null);
            return true;
        }

        private bool ShowHotkeyError(
            string name, string error, Control focus)
        {
            _tabs.SelectedIndex = 2;
            MessageBox.Show(
                name + "：" + error,
                "快捷键设置",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            if (focus != null) focus.Focus();
            return false;
        }

        private static string NormalizeHotkey(
            string value, string fallback)
        {
            HotkeyGesture gesture;
            string error;
            return HotkeyGesture.TryParse(
                value, out gesture, out error)
                ? gesture.Display
                : fallback;
        }

        private void ModelVendorChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (_loadingValues) return;
            var vendor =
                _modelVendor.SelectedItem as ModelVendorChoice;
            if (vendor == null) return;
            SaveActiveModelDraft();
            _activeModelVendor = vendor.Code;
            EnsureModelDraft(vendor);
            LoadModelDraft(vendor.Code);
            UpdateProviderSelection();
        }

        private void LoadModelDrafts()
        {
            _modelDrafts.Clear();
            foreach (ModelVendorChoice vendor in
                _modelVendor.Items)
            {
                ModelConnectionSettings saved =
                    _settings.GetModelConnection(vendor.Code);
                if (string.IsNullOrWhiteSpace(saved.BaseUrl))
                    saved.BaseUrl = vendor.BaseUrl;
                if (string.IsNullOrWhiteSpace(saved.Model))
                    saved.Model = vendor.Model;
                _modelDrafts[vendor.Code] = saved;
            }
        }

        private void EnsureModelDraft(ModelVendorChoice vendor)
        {
            if (_modelDrafts.ContainsKey(vendor.Code)) return;
            _modelDrafts[vendor.Code] =
                new ModelConnectionSettings
                {
                    BaseUrl = vendor.BaseUrl,
                    Model = vendor.Model,
                    ApiKey = ""
                };
        }

        private void SaveActiveModelDraft()
        {
            if (string.IsNullOrWhiteSpace(
                _activeModelVendor) ||
                _modelBaseUrl == null ||
                _modelName == null ||
                _modelApiKey == null)
                return;
            _modelDrafts[_activeModelVendor] =
                new ModelConnectionSettings
                {
                    BaseUrl = _modelBaseUrl.Text.Trim(),
                    Model = _modelName.Text.Trim(),
                    ApiKey = _modelApiKey.Password.Trim()
                };
        }

        private void LoadModelDraft(string vendor)
        {
            ModelConnectionSettings draft;
            if (!_modelDrafts.TryGetValue(
                vendor, out draft))
                return;
            _modelBaseUrl.Text = draft.BaseUrl;
            _modelName.Text = draft.Model;
            _modelApiKey.Password = draft.ApiKey;
        }

        private void SelectModelVendor(
            string code, string baseUrl)
        {
            string requested = code ?? "";
            if (requested.Length == 0 ||
                requested == "Custom")
            {
                foreach (ModelVendorChoice choice in
                    _modelVendor.Items)
                {
                    if (choice.Code != "Custom" &&
                        string.Equals(
                            choice.BaseUrl.TrimEnd('/'),
                            (baseUrl ?? "").TrimEnd('/'),
                            StringComparison.OrdinalIgnoreCase))
                    {
                        requested = choice.Code;
                        break;
                    }
                }
            }
            for (int i = 0; i < _modelVendor.Items.Count; i++)
            {
                var choice =
                    _modelVendor.Items[i] as ModelVendorChoice;
                if (choice != null &&
                    string.Equals(
                        choice.Code,
                        requested,
                        StringComparison.OrdinalIgnoreCase))
                {
                    _modelVendor.SelectedIndex = i;
                    return;
                }
            }
            _modelVendor.SelectedIndex = 0;
        }

        private string SelectedProvider()
        {
            return _pendingProvider;
        }

        private static bool IsKnownProvider(string provider)
        {
            return provider == "GoogleFree" ||
                   provider == "MicrosoftFree" ||
                   provider == "Microsoft" ||
                   provider == "Google" ||
                   provider == "ModelApi";
        }

        private static Border ShortcutPill(
            string key,
            string action,
            out TextBlock keyText)
        {
            var panel = new StackPanel { Orientation = Orientation.Horizontal };
            keyText = new TextBlock
            {
                Text = key,
                Foreground = Navy,
                FontWeight = FontWeights.Bold,
                FontSize = 12,
                Background = Brushes.White,
                Padding = new Thickness(8, 5, 8, 5)
            };
            panel.Children.Add(keyText);
            panel.Children.Add(new TextBlock
            {
                Text = action,
                Foreground = Brushes.White,
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 0, 9, 0)
            });
            return new Border
            {
                Background = Brush("#287C98"),
                BorderBrush = Brush("#59C6D5"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(8, 0, 0, 0),
                Child = panel
            };
        }

        private static TabItem Tab(string title, UIElement content)
        {
            var item = new TabItem
            {
                Header = title,
                Content = content,
                Width = 102,
                Height = 48,
                Margin = new Thickness(7, 5, 7, 0),
                Padding = new Thickness(15, 10, 15, 10),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                Foreground = Navy,
                HorizontalContentAlignment =
                    HorizontalAlignment.Stretch,
                VerticalContentAlignment =
                    VerticalAlignment.Stretch
            };
            var template = new ControlTemplate(
                typeof(TabItem));
            var border = new FrameworkElementFactory(
                typeof(Border));
            border.Name = "NavigationChrome";
            border.SetValue(
                Border.CornerRadiusProperty,
                new CornerRadius(9));
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            var presenter = new FrameworkElementFactory(
                typeof(ContentPresenter));
            presenter.SetValue(
                ContentPresenter.ContentSourceProperty,
                "Header");
            presenter.SetValue(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            presenter.SetValue(
                FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Left);
            presenter.SetBinding(
                FrameworkElement.MarginProperty,
                new Binding("Padding")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger
            {
                Property = UIElement.IsMouseOverProperty,
                Value = true
            };
            hover.Setters.Add(new Setter(
                Control.BackgroundProperty,
                Brush("#F0F7F9")));
            template.Triggers.Add(hover);
            var selected = new Trigger
            {
                Property = TabItem.IsSelectedProperty,
                Value = true
            };
            selected.Setters.Add(new Setter(
                Control.BackgroundProperty,
                Brush("#E4F3F6")));
            selected.Setters.Add(new Setter(
                Control.ForegroundProperty,
                Ocean));
            selected.Setters.Add(new Setter(
                Control.BorderBrushProperty,
                Brush("#A9D6DF")));
            selected.Setters.Add(new Setter(
                Control.BorderThicknessProperty,
                new Thickness(1)));
            template.Triggers.Add(selected);
            item.Template = template;
            return item;
        }

        private static StackPanel TabBody()
        {
            return new StackPanel { Margin = new Thickness(18) };
        }

        private static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Brushes.White
            };
        }

        private static StackPanel Intro(string title, string description)
        {
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            if (!string.IsNullOrWhiteSpace(description))
            {
                panel.Children.Add(new TextBlock
                {
                    Text = description,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Muted,
                    FontSize = 11.5,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            return panel;
        }

        private static Border Card()
        {
            return new Border
            {
                Background = Brushes.White,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12)
            };
        }

        private Button ProviderChoice(
            string name, string category, string code)
        {
            var text = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            text.Children.Add(new TextBlock
            {
                Text = name,
                TextAlignment = TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            text.Children.Add(new TextBlock
            {
                Text = category,
                TextAlignment = TextAlignment.Left,
                TextTrimming = TextTrimming.CharacterEllipsis,
                FontSize = 10.5,
                Foreground = Muted,
                Margin = new Thickness(0, 2, 0, 0)
            });
            var button = new Button
            {
                Content = text,
                Tag = code,
                Height = 48,
                MinWidth = 112,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, 4, 0)
            };
            button.Template = RoundedButtonTemplate(9);
            button.Click += ProviderBrowseClick;
            return button;
        }

        private static UIElement ProviderConfigPanel(
            string name,
            string state,
            UIElement fields)
        {
            if (fields != null) return fields;
            return new TextBlock
            {
                Text = state,
                Foreground = Muted,
                FontSize = 11.5,
                Margin = new Thickness(0, 2, 0, 0)
            };
        }

        private static Border ConfigurationCard(
            string name,
            string badge,
            string description,
            UIElement fields = null)
        {
            var card = Card();
            card.Padding = new Thickness(15);
            var content = new StackPanel();
            content.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            content.Children.Add(new TextBlock
            {
                Text = badge,
                FontSize = 10.5,
                Foreground = Ocean,
                Margin = new Thickness(0, 2, 0, 0)
            });
            content.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(
                    0, 7, 0, fields == null ? 0 : 8)
            });
            if (fields != null)
            {
                content.Children.Add(new Border
                {
                    Height = 1,
                    Background = Line,
                    Margin = new Thickness(0, 5, 0, 11)
                });
                content.Children.Add(fields);
            }
            card.Child = content;
            return card;
        }

        private Button ConfigSectionButton(
            string text, string section)
        {
            var button = new Button
            {
                Content = text,
                Tag = section,
                Height = 34,
                Background = Brushes.Transparent,
                Foreground = Muted,
                BorderThickness = new Thickness(0),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            button.Template = RoundedButtonTemplate(9);
            button.Click += ConfigSectionClick;
            return button;
        }

        private void ConfigSectionClick(
            object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            SelectConfigSection(
                button.Tag as string ?? "Free");
            e.Handled = true;
        }

        private void SelectConfigSection(string section)
        {
            if (section != "Official" &&
                section != "Model")
                section = "Free";
            _activeConfigSection = section;
            Button[] buttons =
            {
                _freeConfigButton,
                _officialConfigButton,
                _modelConfigButton
            };
            foreach (Button button in buttons)
            {
                if (button == null) continue;
                bool selected = string.Equals(
                    button.Tag as string,
                    section,
                    StringComparison.Ordinal);
                button.Background = selected
                    ? Brushes.White
                    : Brushes.Transparent;
                button.Foreground = selected
                    ? Navy
                    : Muted;
                button.BorderBrush = selected
                    ? Brush("#C8DDE5")
                    : Brushes.Transparent;
                button.BorderThickness = selected
                    ? new Thickness(1)
                    : new Thickness(0);
            }
            if (_providerConfigHost == null) return;
            string next = DefaultBrowsedProvider(section);
            RebuildProviderBrowseButtons(section);
            BrowseProvider(next);
        }

        private static string ConfigSectionForProvider(
            string provider)
        {
            if (provider == "Microsoft" ||
                provider == "Google")
                return "Official";
            if (provider == "ModelApi")
                return "Model";
            return "Free";
        }

        private string DefaultBrowsedProvider(string section)
        {
            if (ConfigSectionForBrowse(
                _browsedProvider) == section)
                return _browsedProvider;
            if (ConfigSectionForProvider(
                _pendingProvider) == section)
                return _pendingProvider == "ModelApi"
                    ? "ModelApi:" +
                      NormalizeModelVendor(
                          _pendingModelVendor)
                    : _pendingProvider;
            if (section == "Official")
                return "Microsoft";
            if (section == "Model")
                return "ModelApi:" +
                    NormalizeModelVendor(
                        _pendingModelVendor);
            return "GoogleFree";
        }

        private void RebuildProviderBrowseButtons(
            string section)
        {
            var host = _providerConfigHost == null
                ? null
                : _providerConfigHost.Tag as StackPanel;
            if (host == null) return;
            host.Children.Clear();
            _providerBrowseButtons.Clear();
            var choices = new List<KeyValuePair<string, string>>();
            if (section == "Official")
            {
                choices.Add(new KeyValuePair<string, string>(
                    "Microsoft", "Microsoft Translator"));
                choices.Add(new KeyValuePair<string, string>(
                    "Google", "Google Cloud"));
            }
            else if (section == "Model")
            {
                choices.Add(new KeyValuePair<string, string>(
                    "ModelApi:DeepSeek", "DeepSeek"));
                choices.Add(new KeyValuePair<string, string>(
                    "ModelApi:MiMo", "MiMo"));
                choices.Add(new KeyValuePair<string, string>(
                    "ModelApi:Qwen", "Qwen"));
                choices.Add(new KeyValuePair<string, string>(
                    "ModelApi:Custom", "自定义"));
            }
            else
            {
                choices.Add(new KeyValuePair<string, string>(
                    "GoogleFree", "Google 免费"));
                choices.Add(new KeyValuePair<string, string>(
                    "MicrosoftFree", "Microsoft 免费"));
            }
            foreach (KeyValuePair<string, string>
                choice in choices)
            {
                bool ready = IsBrowseReady(choice.Key);
                Button button = ProviderChoice(
                    choice.Value,
                    IsPendingProvider(choice.Key)
                        ? "✓ 当前引擎"
                        : ready
                            ? "已配置"
                            : "待配置",
                    choice.Key);
                _providerBrowseButtons[choice.Key] = button;
                host.Children.Add(button);
                switch (choice.Key)
                {
                    case "GoogleFree":
                        _googleFree = button;
                        break;
                    case "MicrosoftFree":
                        _microsoftFree = button;
                        break;
                    case "Microsoft":
                        _microsoftOfficial = button;
                        break;
                    case "Google":
                        _googleOfficial = button;
                        break;
                    case "ModelApi:Custom":
                        _modelApi = button;
                        break;
                }
            }
        }

        private void BrowseProvider(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                code = "GoogleFree";
            string section = ConfigSectionForBrowse(code);
            if (section != _activeConfigSection)
            {
                SelectConfigSection(section);
                return;
            }
            _browsedProvider = code;
            if (BaseProvider(code) == "ModelApi")
                SelectModelVendorForBrowse(
                    ModelVendorFromBrowse(code));
            UIElement view;
            if (_providerConfigHost != null &&
                _providerConfigViews.TryGetValue(
                    code, out view))
                _providerConfigHost.Content = view;
            UpdateProviderSelection();
        }

        private void SelectModelVendorForBrowse(
            string vendorCode)
        {
            vendorCode = NormalizeModelVendor(vendorCode);
            if (_activeModelVendor == vendorCode)
                return;
            SaveActiveModelDraft();
            bool wasLoading = _loadingValues;
            _loadingValues = true;
            try
            {
                for (int i = 0;
                    i < _modelVendor.Items.Count;
                    i++)
                {
                    var choice =
                        _modelVendor.Items[i]
                        as ModelVendorChoice;
                    if (choice == null ||
                        !string.Equals(
                            choice.Code,
                            vendorCode,
                            StringComparison.OrdinalIgnoreCase))
                        continue;
                    _modelVendor.SelectedIndex = i;
                    _activeModelVendor = choice.Code;
                    LoadModelDraft(choice.Code);
                    break;
                }
            }
            finally
            {
                _loadingValues = wasLoading;
            }
        }

        private bool IsBrowsedProviderReady()
        {
            return IsBrowseReady(_browsedProvider);
        }

        private bool IsBrowseReady(string browseCode)
        {
            string provider = BaseProvider(browseCode);
            if (provider != "ModelApi")
                return IsProviderReady(provider);
            string vendor =
                ModelVendorFromBrowse(browseCode);
            if (string.Equals(
                vendor,
                _activeModelVendor,
                StringComparison.OrdinalIgnoreCase))
                SaveActiveModelDraft();
            ModelConnectionSettings draft;
            return _modelDrafts.TryGetValue(
                vendor, out draft) &&
                draft.IsUsable(vendor);
        }

        private bool IsPendingProvider(string browseCode)
        {
            string provider = BaseProvider(browseCode);
            if (!string.Equals(
                provider,
                _pendingProvider,
                StringComparison.OrdinalIgnoreCase))
                return false;
            return provider != "ModelApi" ||
                string.Equals(
                    ModelVendorFromBrowse(browseCode),
                    NormalizeModelVendor(
                        _pendingModelVendor),
                    StringComparison.OrdinalIgnoreCase);
        }

        private static string BaseProvider(string browseCode)
        {
            return browseCode != null &&
                browseCode.StartsWith(
                    "ModelApi:",
                    StringComparison.OrdinalIgnoreCase)
                ? "ModelApi"
                : browseCode ?? "GoogleFree";
        }

        private static string ConfigSectionForBrowse(
            string browseCode)
        {
            return ConfigSectionForProvider(
                BaseProvider(browseCode));
        }

        private static string ModelVendorFromBrowse(
            string browseCode)
        {
            if (browseCode == null) return "Custom";
            int separator = browseCode.IndexOf(':');
            return separator < 0
                ? "Custom"
                : NormalizeModelVendor(
                    browseCode.Substring(
                        separator + 1));
        }

        private static string NormalizeModelVendor(
            string vendor)
        {
            if (string.Equals(
                vendor, "DeepSeek",
                StringComparison.OrdinalIgnoreCase))
                return "DeepSeek";
            if (string.Equals(
                vendor, "MiMo",
                StringComparison.OrdinalIgnoreCase))
                return "MiMo";
            if (string.Equals(
                vendor, "Qwen",
                StringComparison.OrdinalIgnoreCase))
                return "Qwen";
            return "Custom";
        }

        private static string ModelVendorDisplayName(
            string vendor)
        {
            switch (NormalizeModelVendor(vendor))
            {
                case "DeepSeek": return "DeepSeek";
                case "MiMo": return "MiMo";
                case "Qwen": return "Qwen";
                default: return "自定义";
            }
        }

        private static string ProviderBrowseDisplayName(
            string browseCode)
        {
            if (BaseProvider(browseCode) == "ModelApi")
                return ModelVendorDisplayName(
                    ModelVendorFromBrowse(
                        browseCode));
            return ProviderDisplayName(
                BaseProvider(browseCode));
        }

        private void ProviderBrowseClick(
            object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            BrowseProvider(
                button.Tag as string ?? "GoogleFree");
            e.Handled = true;
        }

        private void ActivateBrowsedProviderClick(
            object sender, RoutedEventArgs e)
        {
            string provider =
                BaseProvider(_browsedProvider);
            if (!IsBrowsedProviderReady())
            {
                MessageBox.Show(
                    ProviderBrowseDisplayName(
                        _browsedProvider) +
                    " 的必要配置尚未填写完整。",
                    "无法设为当前引擎",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            _pendingProvider = provider;
            if (provider == "ModelApi")
                _pendingModelVendor =
                    ModelVendorFromBrowse(
                        _browsedProvider);
            UpdateProviderSelection();
            e.Handled = true;
        }

        private void UpdateProviderSelection()
        {
            foreach (KeyValuePair<string, Button> item in
                _providerBrowseButtons)
            {
                Button button = item.Value;
                string code = item.Key;
                bool browsed = string.Equals(
                    code, _browsedProvider,
                    StringComparison.OrdinalIgnoreCase);
                bool current = IsPendingProvider(code);
                bool ready = IsBrowseReady(code);
                var panel = button.Content as StackPanel;
                if (panel != null && panel.Children.Count >= 2)
                {
                    var title = panel.Children[0] as TextBlock;
                    var state = panel.Children[1] as TextBlock;
                    if (state != null)
                        state.Text = current
                            ? "✓ 当前引擎"
                            : ready
                                ? "已配置"
                                : "待配置";
                    if (title != null)
                        title.Foreground = browsed
                            ? Ocean
                            : Navy;
                    if (state != null)
                        state.Foreground = current
                            ? Ocean
                            : ready
                                ? Brush("#137A57")
                                : Brush("#A45B00");
                }
                button.Background = browsed
                    ? Brushes.White
                    : Brushes.Transparent;
                button.BorderBrush = browsed
                    ? Brush("#A9D1DC")
                    : Brushes.Transparent;
            }
            bool browsedReady = IsBrowsedProviderReady();
            bool alreadyCurrent =
                IsPendingProvider(_browsedProvider);
            if (_providerConfigTitle != null)
                _providerConfigTitle.Text =
                    ProviderBrowseDisplayName(
                        _browsedProvider);
            if (_providerConfigState != null)
            {
                _providerConfigState.Text =
                    alreadyCurrent
                        ? "当前引擎"
                        : browsedReady
                            ? "配置可用"
                            : "配置尚未完成";
                _providerConfigState.Foreground =
                    browsedReady
                        ? Brush("#137A57")
                        : Brush("#A45B00");
            }
            if (_activateProviderButton != null)
            {
                _activateProviderButton.IsEnabled =
                    browsedReady && !alreadyCurrent;
                _activateProviderButton.Content =
                    alreadyCurrent
                        ? "当前引擎"
                        : "设为当前引擎";
                _activateProviderButton.Opacity =
                    _activateProviderButton.IsEnabled
                        ? 1.0
                        : 0.58;
            }
            if (_headerCurrentEngine != null)
            {
                string name = ProviderDisplayName(
                    _pendingProvider);
                if (_pendingProvider == "ModelApi")
                    name += " · " +
                        ModelVendorDisplayName(
                            _pendingModelVendor);
                bool changed =
                    !string.Equals(
                        _pendingProvider,
                        _settings.Provider,
                        StringComparison.OrdinalIgnoreCase) ||
                    (_pendingProvider == "ModelApi" &&
                     !string.Equals(
                         _pendingModelVendor,
                         NormalizeModelVendor(
                             _settings.ModelVendor),
                         StringComparison.OrdinalIgnoreCase));
                _headerCurrentEngine.Text =
                    name + (changed ? "（待保存）" : "");
            }
        }

        private bool IsProviderReady(string provider)
        {
            if (provider == "GoogleFree" ||
                provider == "MicrosoftFree")
                return true;
            if (provider == "Google")
                return !string.IsNullOrWhiteSpace(
                    _googleKey == null ? "" : _googleKey.Password);
            if (provider == "Microsoft")
                return !string.IsNullOrWhiteSpace(
                    _microsoftKey == null
                        ? ""
                        : _microsoftKey.Password);
            if (provider == "ModelApi")
            {
                SaveActiveModelDraft();
                ModelConnectionSettings pending;
                return _modelDrafts.TryGetValue(
                    NormalizeModelVendor(
                        _pendingModelVendor),
                    out pending) &&
                    pending.IsUsable(
                        NormalizeModelVendor(
                            _pendingModelVendor));
            }
            return false;
        }

        private static string ProviderDisplayName(string provider)
        {
            switch (provider)
            {
                case "MicrosoftFree": return "Microsoft 免费";
                case "Microsoft": return "Microsoft Translator";
                case "Google": return "Google Cloud Translation";
                case "ModelApi": return "AI 大模型";
                default: return "Google 免费";
            }
        }

        private static Border InfoBox(string text)
        {
            return new Border
            {
                Background = Brush("#ECF8FA"),
                BorderBrush = Brush("#C9EAEF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 14, 0, 0),
                Child = new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("#397080"),
                    FontSize = 11
                }
            };
        }

        private static TextBox AddText(
            Panel parent, string label, string placeholder)
        {
            AddFieldLabel(parent, label);
            var box = new TextBox
            {
                Height = 36,
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(24, 5, 0, 9),
                ToolTip = placeholder,
                BorderBrush = Line
            };
            parent.Children.Add(box);
            return box;
        }

        private static PasswordBox AddPassword(
            Panel parent, string label, string placeholder)
        {
            AddFieldLabel(parent, label);
            var box = new PasswordBox
            {
                Height = 36,
                Padding = new Thickness(9, 7, 9, 7),
                Margin = new Thickness(24, 5, 0, 9),
                ToolTip = placeholder,
                BorderBrush = Line
            };
            parent.Children.Add(box);
            return box;
        }

        private static void AddFieldLabel(Panel parent, string label)
        {
            parent.Children.Add(new TextBlock
            {
                Text = label,
                Margin = new Thickness(24, 0, 0, 0),
                Foreground = Navy,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold
            });
        }

        private static Button PrimaryButton(string text)
        {
            var button = new Button
            {
                Content = text,
                Background = Ocean,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            });
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            return button;
        }

        private static ControlTemplate RoundedButtonTemplate(
            double radius)
        {
            var template = new ControlTemplate(
                typeof(Button));
            var border = new FrameworkElementFactory(
                typeof(Border));
            border.SetValue(
                Border.CornerRadiusProperty,
                new CornerRadius(radius));
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            var presenter = new FrameworkElementFactory(
                typeof(ContentPresenter));
            presenter.SetValue(
                FrameworkElement.HorizontalAlignmentProperty,
                HorizontalAlignment.Stretch);
            presenter.SetValue(
                FrameworkElement.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            presenter.SetBinding(
                FrameworkElement.MarginProperty,
                new Binding("Padding")
                {
                    RelativeSource = new RelativeSource(
                        RelativeSourceMode.TemplatedParent)
                });
            border.AppendChild(presenter);
            template.VisualTree = border;
            return template;
        }

        private static Button SecondaryButton(string text)
        {
            return new Button
            {
                Content = text,
                Height = 38,
                Padding = new Thickness(13, 0, 13, 0),
                Background = Brushes.White,
                Foreground = Ocean,
                BorderBrush = Brush("#9BCEDB"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        private static List<ModelVendorChoice> ModelVendors()
        {
            return new List<ModelVendorChoice>
            {
                new ModelVendorChoice(
                    "Custom",
                    "自定义 / 其他 OpenAI 兼容",
                    "",
                    ""),
                new ModelVendorChoice(
                    "DeepSeek",
                    "DeepSeek",
                    "https://api.deepseek.com",
                    "deepseek-v4-flash"),
                new ModelVendorChoice(
                    "MiMo",
                    "小米 MiMo",
                    "https://api.xiaomimimo.com/v1",
                    "mimo-v2.5"),
                new ModelVendorChoice(
                    "Qwen",
                    "阿里云百炼 · Qwen（中国区）",
                    "https://dashscope.aliyuncs.com/compatible-mode/v1",
                    "qwen-plus")
            };
        }

 #if LEGACY_OCR_COMPONENT_UI
        private static List<OcrLanguageChoice> AvailableOcrLanguages()
        {
            var result = new List<OcrLanguageChoice>
            {
                new OcrLanguageChoice(
                    "auto", "自动（按截图内容比较已安装语言）")
            };
            string[] tags = { "zh-Hans", "zh-Hant", "en", "ja", "ko" };
            string[] names = { "简体中文", "繁體中文", "English", "日本語", "한국어" };
            for (int i = 0; i < tags.Length; i++)
            {
                foreach (Language language in OcrEngine.AvailableRecognizerLanguages)
                {
                    if (!OcrTagMatches(language.LanguageTag, tags[i])) continue;
                    result.Add(new OcrLanguageChoice(
                        language.LanguageTag,
                        names[i] + "  (" + language.LanguageTag + ")"));
                    break;
                }
            }
            return result;
        }

        private void SelectOcrLanguage(string tag)
        {
            for (int i = 0; i < _ocrLanguage.Items.Count; i++)
            {
                var choice = _ocrLanguage.Items[i] as OcrLanguageChoice;
                if (choice != null && OcrTagMatches(choice.Tag, tag))
                {
                    _ocrLanguage.SelectedIndex = i;
                    return;
                }
            }
            _ocrLanguage.SelectedIndex = 0;
        }

        private static bool OcrTagMatches(string available, string requested)
        {
            if (string.IsNullOrWhiteSpace(requested) ||
                string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
                return string.Equals(
                    available, "auto", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(available, requested, StringComparison.OrdinalIgnoreCase))
                return true;
            if (requested.StartsWith("zh-", StringComparison.OrdinalIgnoreCase))
                return available.StartsWith(
                    requested, StringComparison.OrdinalIgnoreCase);
            return string.Equals(
                (available ?? "").Split('-')[0],
                requested.Split('-')[0],
                StringComparison.OrdinalIgnoreCase);
        }

 #endif
        private void UpdateTargetRule()
        {
            if (_targetRule == null || _language == null)
                return;
            if (_language.SelectedIndex <= 0)
            {
                _targetRule.Text =
                    "中文原文 → English · 其他语言 → 简体中文";
                return;
            }
            string[] names =
            {
                "简体中文", "繁體中文", "English",
                "日本語", "한국어", "Français",
                "Deutsch", "Español"
            };
            int index = _language.SelectedIndex - 1;
            _targetRule.Text =
                "始终翻译为 " +
                (index >= 0 && index < names.Length
                    ? names[index]
                    : "简体中文");
        }

        private static int LanguageIndex(string code)
        {
            string[] codes = { "zh-Hans", "zh-Hant", "en", "ja", "ko", "fr", "de", "es" };
            int index = Array.IndexOf(codes, code);
            return index < 0 ? 0 : index;
        }

        private static string LanguageCode(int index)
        {
            string[] codes = { "zh-Hans", "zh-Hant", "en", "ja", "ko", "fr", "de", "es" };
            return index >= 0 && index < codes.Length ? codes[index] : "zh-Hans";
        }

        private static SolidColorBrush Brush(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }

        private sealed class OcrLanguageChoice
        {
            public readonly string Tag;
            private readonly string _display;

            public OcrLanguageChoice(string tag, string display)
            {
                Tag = tag;
                _display = display;
            }

            public override string ToString()
            {
                return _display;
            }
        }

        private sealed class ModelVendorChoice
        {
            public readonly string Code;
            public readonly string Display;
            public readonly string BaseUrl;
            public readonly string Model;

            public ModelVendorChoice(
                string code,
                string display,
                string baseUrl,
                string model)
            {
                Code = code;
                Display = display;
                BaseUrl = baseUrl;
                Model = model;
            }

            public override string ToString()
            {
                return Display;
            }
        }
    }
}
