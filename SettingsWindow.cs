using System;
using System.Collections.Generic;
using System.ComponentModel;
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
        private ComboBox _ocrLanguage;
        private CheckBox _ocrAutoEnhance;
        private CheckBox _ocrAiFallback;
        private TextBox _ocrVisionModel;
        private TextBlock _englishOcrStatus;
        private TextBlock _englishBasicStatus;
        private TextBlock _ocrTaskStatus;
        private ProgressBar _ocrTaskProgress;
        private Button _installEnglishOcr;
        private Button _removeEnglishOcr;
        private Button _removeEnglishAll;
        private Button _copyEnglishOcrCommand;
        private Button _cancelSettings;
        private TextBox _translateHotkey;
        private TextBox _ocrHotkey;
        private TextBox _settingsHotkey;
        private CheckBox _startWithWindows;
        private TextBlock _translateHeaderShortcut;
        private TextBlock _ocrHeaderShortcut;
        private TextBlock _settingsHeaderShortcut;
        private bool _loadingValues;
        private string _pendingProvider = "GoogleFree";
        private TextBlock _currentProviderSummary;
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

            OcrLanguagePackManager.ProgressChanged += OcrProgressChanged;
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
            if (_englishOcrStatus != null)
                UpdateEnglishOcrStatus();
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

        public void HandleEnglishOcrUnavailable()
        {
            if (_ocrLanguage != null)
                SelectOcrLanguage("auto");
            UpdateEnglishOcrStatus();
        }

        public void ShowOcrSettings()
        {
            _tabs.SelectedIndex = 1;
            UpdateEnglishOcrStatus();
        }

        public void ShowModelSettings()
        {
            _tabs.SelectedIndex = 0;
            if (_modelVendor != null) _modelVendor.Focus();
        }

        private Border BuildHeader()
        {
            var header = new Border
            {
                Margin = new Thickness(20, 18, 20, 12),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(24, 16, 22, 16),
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
                Width = 64,
                Height = 64,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(2, 0, 17, 0)
            };
            grid.Children.Add(logo);

            var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            Grid.SetColumn(title, 1);
            title.Children.Add(new TextBlock
            {
                Text = "鲨译 · Sharkey",
                FontSize = 27,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });
            title.Children.Add(new TextBlock
            {
                Text = "选中即译，截图即懂",
                FontSize = 13,
                Foreground = Brush("#BEEBF2"),
                Margin = new Thickness(1, 3, 0, 0)
            });
            grid.Children.Add(title);

            var shortcuts = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(shortcuts, 2);
            shortcuts.Children.Add(ShortcutPill(
                "F8", "翻译", out _translateHeaderShortcut));
            shortcuts.Children.Add(ShortcutPill(
                "F9", "OCR", out _ocrHeaderShortcut));
            shortcuts.Children.Add(ShortcutPill(
                "F10", "设置", out _settingsHeaderShortcut));
            grid.Children.Add(shortcuts);
            return header;
        }

        private Border BuildCommonSettings()
        {
            var card = Card();
            card.Margin = new Thickness(20, 0, 20, 12);
            card.Padding = new Thickness(18, 12, 18, 12);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
            card.Child = grid;

            var copy = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            copy.Children.Add(new TextBlock
            {
                Text = "翻译目标",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            copy.Children.Add(new TextBlock
            {
                Text = "所有翻译方式共用，不必在每个服务里重复设置。",
                FontSize = 11.5,
                Foreground = Muted,
                Margin = new Thickness(0, 3, 0, 0)
            });
            grid.Children.Add(copy);

            _language = new ComboBox
            {
                Height = 38,
                Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center,
                ItemsSource = new[]
                {
                    "简体中文  (zh-Hans)", "繁體中文  (zh-Hant)", "English  (en)",
                    "日本語  (ja)", "한국어  (ko)", "Français  (fr)",
                    "Deutsch  (de)", "Español  (es)"
                }
            };
            Grid.SetColumn(_language, 1);
            grid.Children.Add(_language);
            return card;
        }

        private TabControl BuildProviderTabs()
        {
            var tabs = new TabControl
            {
                Margin = new Thickness(20, 0, 20, 12),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 13
            };
            tabs.Items.Add(Tab("翻译", BuildTranslationTab()));
            tabs.Items.Add(Tab("OCR", BuildOcrTab()));
            tabs.Items.Add(Tab("快捷键", BuildShortcutTab()));
            tabs.Items.Add(Tab("常规", BuildGeneralTab()));
            return tabs;
        }

        private UIElement BuildTranslationTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "翻译服务",
                "配置可以独立编辑；只有点击“设为当前”才会改变保存后使用的翻译线路。"));

            var current = Card();
            current.Padding = new Thickness(16, 12, 16, 12);
            current.Margin = new Thickness(0, 14, 0, 12);
            var currentPanel = new StackPanel();
            currentPanel.Children.Add(new TextBlock
            {
                Text = "当前翻译服务",
                Foreground = Muted,
                FontSize = 10.5
            });
            _currentProviderSummary = new TextBlock
            {
                Foreground = Navy,
                FontWeight = FontWeights.Bold,
                FontSize = 15,
                Margin = new Thickness(0, 3, 0, 0)
            };
            currentPanel.Children.Add(_currentProviderSummary);
            current.Child = currentPanel;
            root.Children.Add(current);

            Border common = BuildCommonSettings();
            common.Margin = new Thickness(0, 0, 0, 14);
            root.Children.Add(common);
            root.Children.Add(BuildFreeTab());
            root.Children.Add(BuildOfficialTab());
            root.Children.Add(BuildModelTab());
            return Scroll(root);
        }

        private UIElement BuildFreeTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "免账号调用",
                "开箱即用，适合日常轻量翻译。它们依赖网页接口，可能因服务方调整而暂时失效。"));

            var grid = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _googleFree = ProviderChoice(
                "Google 免费", "免账号 · 响应快", "GoogleFree");
            _microsoftFree = ProviderChoice(
                "Microsoft 免费", "免账号 · Bing 网页翻译", "MicrosoftFree");
            Border google = ProviderCard(_googleFree, "适合常见语言的快速互译。");
            Border microsoft = ProviderCard(_microsoftFree, "作为另一条免费线路，便于随时切换。");
            grid.Children.Add(google);
            Grid.SetColumn(microsoft, 2);
            grid.Children.Add(microsoft);
            root.Children.Add(grid);

            root.Children.Add(InfoBox(
                "提示：免费方式无需填写密钥。若接口不稳定，可切换到官方 API 或 AI 大模型。"));
            return root;
        }

        private UIElement BuildOfficialTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "官方翻译 API",
                "适合需要稳定性、配额管理和正式服务保障的场景。"));

            _microsoftOfficial = ProviderChoice(
                "Microsoft Translator", "官方接口 · 需要订阅密钥", "Microsoft");
            var microsoftContent = new StackPanel();
            _microsoftKey = AddPassword(microsoftContent, "API Key", "粘贴 Microsoft Translator 密钥");
            _microsoftKey.PasswordChanged += delegate
            {
                UpdateProviderSelection();
            };
            _region = AddText(microsoftContent, "Region", "例如 eastasia");
            Border microsoft = ProviderCard(
                _microsoftOfficial, "Azure AI Translator 官方服务。", microsoftContent);
            microsoft.Margin = new Thickness(0, 14, 0, 12);
            root.Children.Add(microsoft);

            _googleOfficial = ProviderChoice(
                "Google Cloud Translation", "官方接口 · 需要 API Key", "Google");
            var googleContent = new StackPanel();
            _googleKey = AddPassword(googleContent, "API Key", "粘贴 Google Cloud Translation 密钥");
            _googleKey.PasswordChanged += delegate
            {
                UpdateProviderSelection();
            };
            root.Children.Add(ProviderCard(
                _googleOfficial, "Google Cloud Translation 官方服务。", googleContent));
            return root;
        }

        private UIElement BuildModelTab()
        {
            var root = TabBody();
            root.Children.Add(Intro(
                "AI 大模型翻译",
                "连接 OpenAI 兼容的 Chat Completions 接口，可使用云端模型或本地 Ollama。"));
            _modelApi = ProviderChoice(
                "OpenAI 兼容接口", "供应商预设 · 仍可自定义", "ModelApi");
            var content = new StackPanel();
            AddFieldLabel(content, "供应商");
            _modelVendor = new ComboBox
            {
                Height = 36,
                Margin = new Thickness(24, 5, 0, 9),
                Padding = new Thickness(9, 6, 9, 6),
                ItemsSource = ModelVendors()
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
            Border model = ProviderCard(
                _modelApi,
                "适合上下文理解、语气保持和长文本翻译。",
                content);
            model.Margin = new Thickness(0, 14, 0, 0);
            root.Children.Add(model);
            root.Children.Add(InfoBox(
                "兼容规则：API 地址会自动补全 /chat/completions。使用本地服务时请确认它允许本机访问。"));
            return root;
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
                UpdateHeaderShortcuts();
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
            return Scroll(root);
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
                UpdateHeaderShortcuts();
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
                "默认在本机增强和识别截图；只有明确开启后，低质量结果才会使用 AI 视觉兜底。"));
            root.Children.Add(BuildEnglishOcrCard());

            var localCard = Card();
            localCard.Padding = new Thickness(16);
            localCard.Margin = new Thickness(0, 12, 0, 12);
            var local = new StackPanel();
            local.Children.Add(new TextBlock
            {
                Text = "本地识别",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            AddFieldLabel(local, "识别语言");
            _ocrLanguage = new ComboBox
            {
                Height = 36,
                Margin = new Thickness(24, 5, 0, 10),
                Padding = new Thickness(9, 6, 9, 6),
                ItemsSource = AvailableOcrLanguages()
            };
            local.Children.Add(_ocrLanguage);
            _ocrAutoEnhance = new CheckBox
            {
                Content = "自动放大、对比度增强和深色模式反转",
                Margin = new Thickness(24, 3, 0, 4),
                Foreground = Navy,
                FontSize = 12
            };
            local.Children.Add(_ocrAutoEnhance);
            localCard.Child = local;
            root.Children.Add(localCard);

            var aiCard = Card();
            aiCard.Padding = new Thickness(16);
            var ai = new StackPanel();
            ai.Children.Add(new TextBlock
            {
                Text = "AI 视觉兜底",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            ai.Children.Add(new TextBlock
            {
                Text = "仅当本地多路识别结果为空、乱码较多或差异明显时调用。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(0, 4, 0, 9)
            });
            _ocrAiFallback = new CheckBox
            {
                Content = "允许低质量时把截图发送给视觉模型",
                Margin = new Thickness(0, 0, 0, 7),
                Foreground = Navy,
                FontSize = 12
            };
            ai.Children.Add(_ocrAiFallback);
            _ocrVisionModel = AddText(
                ai, "视觉模型名称", "例如 gpt-4.1-mini / qwen-vl-max");
            ai.Children.Add(new TextBlock
            {
                Text = "API 地址和 Key 复用“AI 大模型”页；截图只在内存中编码，不写入历史记录。",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#397080"),
                FontSize = 10.5,
                Margin = new Thickness(24, 0, 0, 0)
            });
            aiCard.Child = ai;
            root.Children.Add(aiCard);
            root.Children.Add(InfoBox(
                "若手动选择的语言包未安装，鲨译会回退到多语言自动识别并给出提示。"));
            return Scroll(root);
        }

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
                    LanguageIndex(_settings.TargetLanguage);
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
                SelectOcrLanguage(_settings.OcrLanguage);
                _ocrAutoEnhance.IsChecked = _settings.OcrAutoEnhance;
                _ocrAiFallback.IsChecked = _settings.OcrAiFallback;
                _ocrVisionModel.Text = _settings.OcrVisionModel;
                _translateHotkey.Text =
                    NormalizeHotkey(_settings.TranslateHotkey, "F8");
                _ocrHotkey.Text =
                    NormalizeHotkey(_settings.OcrHotkey, "F9");
                _settingsHotkey.Text =
                    NormalizeHotkey(_settings.SettingsHotkey, "F10");
                _startWithWindows.IsChecked =
                    StartupManager.IsEnabled();
                UpdateHeaderShortcuts();
                _pendingProvider =
                    IsKnownProvider(_settings.Provider)
                        ? _settings.Provider
                        : "GoogleFree";
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

            bool enableAiOcr = _ocrAiFallback.IsChecked == true;
            if (enableAiOcr &&
                (string.IsNullOrWhiteSpace(_modelBaseUrl.Text) ||
                 string.IsNullOrWhiteSpace(_ocrVisionModel.Text)))
            {
                _tabs.SelectedIndex = 1;
                MessageBox.Show(
                    "开启 AI 视觉兜底前，请填写模型 API 地址和视觉模型名称。",
                    "鲨译", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (enableAiOcr && !_settings.OcrAiFallback)
            {
                MessageBoxResult consent = MessageBox.Show(
                    "开启后，当本地 OCR 质量较差时，所选截图会发送到你配置的视觉模型服务。\n\n" +
                    "截图可能包含隐私信息。确定允许上传吗？",
                    "启用 AI 视觉 OCR",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);
                if (consent != MessageBoxResult.Yes) return;
            }

            if (!IsProviderReady(_pendingProvider))
            {
                _tabs.SelectedIndex = 0;
                MessageBox.Show(
                    ProviderDisplayName(_pendingProvider) +
                    " 的必要配置尚未填写完整。请补充配置，或选择其他可用服务。",
                    "当前翻译服务不可用",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }
            _settings.Provider = _pendingProvider;
            _settings.TargetLanguage = LanguageCode(_language.SelectedIndex);
            _settings.GoogleApiKey = _googleKey.Password.Trim();
            _settings.MicrosoftApiKey = _microsoftKey.Password.Trim();
            _settings.MicrosoftRegion = _region.Text.Trim();
            var modelVendor =
                _modelVendor.SelectedItem as ModelVendorChoice;
            _settings.ModelVendor =
                modelVendor == null ? "Custom" : modelVendor.Code;
            SaveActiveModelDraft();
            foreach (KeyValuePair<string, ModelConnectionSettings>
                profile in _modelDrafts)
                _settings.SetModelConnection(
                    profile.Key, profile.Value);
            ModelConnectionSettings activeConnection =
                _modelDrafts[_settings.ModelVendor];
            _settings.ModelBaseUrl = activeConnection.BaseUrl;
            _settings.ModelName = activeConnection.Model;
            _settings.ModelApiKey = activeConnection.ApiKey;
            var ocrLanguage = _ocrLanguage.SelectedItem as OcrLanguageChoice;
            _settings.OcrLanguage =
                ocrLanguage == null ? "auto" : ocrLanguage.Tag;
            _settings.OcrAutoEnhance = _ocrAutoEnhance.IsChecked == true;
            _settings.OcrAiFallback = enableAiOcr;
            _settings.OcrVisionModel = _ocrVisionModel.Text.Trim();
            _settings.AutoTranslate = false;
            _settings.TranslateHotkey = translateGesture.Display;
            _settings.OcrHotkey = ocrGesture.Display;
            _settings.SettingsHotkey = settingsGesture.Display;
            _settings.StartWithWindows =
                _startWithWindows.IsChecked == true;
            try
            {
                StartupManager.SetEnabled(
                    _settings.StartWithWindows);
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

        private void UpdateHeaderShortcuts()
        {
            if (_translateHeaderShortcut != null)
                _translateHeaderShortcut.Text =
                    string.IsNullOrWhiteSpace(_translateHotkey.Text)
                        ? "F8"
                        : _translateHotkey.Text;
            if (_ocrHeaderShortcut != null)
                _ocrHeaderShortcut.Text =
                    string.IsNullOrWhiteSpace(_ocrHotkey.Text)
                        ? "F9"
                        : _ocrHotkey.Text;
            if (_settingsHeaderShortcut != null)
                _settingsHeaderShortcut.Text =
                    string.IsNullOrWhiteSpace(_settingsHotkey.Text)
                        ? "F10"
                        : _settingsHotkey.Text;
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
            return new TabItem
            {
                Header = title,
                Content = content,
                Padding = new Thickness(22, 10, 22, 10),
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            };
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
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            panel.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 11.5,
                Margin = new Thickness(0, 4, 0, 0)
            });
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
            string name, string badge, string code)
        {
            var text = new StackPanel();
            text.Children.Add(new TextBlock
            {
                Text = name,
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = Navy
            });
            text.Children.Add(new TextBlock
            {
                Text = badge,
                FontSize = 10.5,
                Foreground = Ocean,
                Margin = new Thickness(0, 2, 0, 0)
            });
            text.Children.Add(new TextBlock
            {
                Text = "设为当前",
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#137A57"),
                Margin = new Thickness(0, 6, 0, 0)
            });
            var button = new Button
            {
                Content = text,
                Tag = code,
                Cursor = Cursors.Hand,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Padding = new Thickness(0)
            };
            button.Click += ProviderActivationClick;
            return button;
        }

        private static Border ProviderCard(
            Button choice, string description, UIElement fields = null)
        {
            var card = Card();
            card.Padding = new Thickness(15);
            card.Cursor = Cursors.Hand;
            var content = new StackPanel();
            content.Children.Add(choice);
            content.Children.Add(new TextBlock
            {
                Text = description,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(24, 7, 0, fields == null ? 0 : 8)
            });
            if (fields != null)
            {
                content.Children.Add(new Border
                {
                    Height = 1,
                    Background = Line,
                    Margin = new Thickness(24, 5, 0, 11)
                });
                content.Children.Add(fields);
            }
            card.Child = content;
            return card;
        }

        private void ProviderActivationClick(
            object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            _pendingProvider = button.Tag as string ?? "GoogleFree";
            UpdateProviderSelection();
            e.Handled = true;
        }

        private void UpdateProviderSelection()
        {
            Button[] buttons =
            {
                _googleFree, _microsoftFree, _microsoftOfficial,
                _googleOfficial, _modelApi
            };
            foreach (Button button in buttons)
            {
                if (button == null) continue;
                bool current = string.Equals(
                    button.Tag as string,
                    _pendingProvider,
                    StringComparison.OrdinalIgnoreCase);
                var panel = button.Content as StackPanel;
                if (panel != null && panel.Children.Count >= 3)
                {
                    var state = panel.Children[2] as TextBlock;
                    if (state != null)
                    {
                        state.Text = current
                            ? "✓ 当前使用"
                            : "设为当前";
                        state.Foreground = current
                            ? Brush("#137A57")
                            : Ocean;
                    }
                }
                button.ToolTip = current
                    ? "保存后继续使用此服务"
                    : "点击设为当前翻译服务";
            }
            if (_currentProviderSummary != null)
                _currentProviderSummary.Text =
                    ProviderDisplayName(_pendingProvider) +
                    (IsProviderReady(_pendingProvider)
                        ? " · 已就绪"
                        : " · 配置未完成");
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
                var vendor =
                    _modelVendor == null
                        ? null
                        : _modelVendor.SelectedItem
                          as ModelVendorChoice;
                return new ModelConnectionSettings
                {
                    BaseUrl = _modelBaseUrl == null
                        ? ""
                        : _modelBaseUrl.Text.Trim(),
                    Model = _modelName == null
                        ? ""
                        : _modelName.Text.Trim(),
                    ApiKey = _modelApiKey == null
                        ? ""
                        : _modelApiKey.Password.Trim()
                }.IsUsable(
                    vendor == null ? "Custom" : vendor.Code);
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
