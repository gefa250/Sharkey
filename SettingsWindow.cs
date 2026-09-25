using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ShapePath = System.Windows.Shapes.Path;

namespace GlobalTranslator
{
    internal sealed class SettingsWindow : Window
    {
        private static readonly Brush Ink = Brush("#17324A");
        private static readonly Brush Subtle = Brush("#647B8D");
        private static readonly Brush Accent = Brush("#1682A6");
        private static readonly Brush BorderLine = Brush("#D9E7EF");
        private static readonly Brush Canvas = Brush("#F6FAFC");
        private static readonly Brush Surface = Brush("#FFFFFF");
        private static readonly string[] LanguageCodes =
            { "zh-Hans", "zh-Hant", "en", "ja", "ko", "fr", "de", "es" };
        private static readonly string[] LanguageNames =
            { "简体中文", "繁體中文", "English", "日本語", "한국어", "Français", "Deutsch", "Español" };
        private static readonly int[] UpdateIntervals = { 6, 12, 24, 72, 168, 0 };

        private readonly AppSettings _settings;
        private AppSettings _draft;
        private readonly Dictionary<string, ModelConnectionSettings> _modelDrafts =
            new Dictionary<string, ModelConnectionSettings>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _navigationButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Button> _vendorButtons =
            new Dictionary<string, Button>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<PasswordBox, TextBox> _revealedKeys =
            new Dictionary<PasswordBox, TextBox>();
        private readonly Dictionary<string, string> _pageTitles =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private readonly DispatcherTimer _balanceTimer =
            new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };

        private Grid _contentHost;
        private StackPanel _navigation;
        private TextBlock _pageTitle;
        private TextBlock _headerCurrentEngine;
        private TextBlock _dirtyText;
        private Button _saveButton;
        private Button _cancelSettings;
        private string _selectedPage = "Model";
        private string _pendingModelVendor = "DeepSeek";
        private string _activeModelVendor = "DeepSeek";
        private bool _loadingValues;
        private bool _isDirty;
        private bool _reloadOnShow;
        private int _testGeneration;
        private CancellationTokenSource _modelTestCancellation;
        private CancellationTokenSource _balanceRequest;
        private string _balanceIdentity = "";
        private string _balanceValue;
        private DateTime _balanceUpdated;

        private ComboBox _language;
        private TextBlock _targetRule;
        private TextBlock _modelSavedSummary;
        private ComboBox _modelVendor;
        private TextBox _modelBaseUrl;
        private PasswordBox _modelApiKey;
        private TextBox _modelName;
        private ComboBox _modelProtocol;
        private TextBlock _modelEndpointHint;
        private Expander _connectionExpander;
        private TextBlock _modelTestStatus;
        private Button _testTextButton;
        private Button _testImageButton;
        private TextBlock _balanceText;
        private Button _refreshBalance;
        private CheckBox _ocrAiFallback;
        private CheckBox _ocrConsentAllowed;
        private TextBlock _ocrConsentStatus;
        private ComboBox _popupFontSize;
        private CheckBox _commerceSearchEnabled;
        private PasswordBox _commerceSearchKey;
        private TextBlock _searchStatus;
        private TextBox _writingHotkey;
        private TextBox _translateHotkey;
        private TextBox _ocrHotkey;
        private TextBox _settingsHotkey;
        private CheckBox _startWithWindows;
        private ComboBox _updateInterval;
        private Button _checkUpdate;
        private TextBlock _updateStatus;
        private Button _copyDiagnostics;
        private Button _openDiagnosticFolder;

        public event EventHandler SettingsSaved;
        public event EventHandler UpdateCheckRequested;

        public SettingsWindow(AppSettings settings)
        {
            if (settings == null) throw new ArgumentNullException("settings");
            _settings = settings;
            Title = "鲨译 Sharkey · 设置";
            Width = 960;
            Height = 720;
            MinWidth = 760;
            MinHeight = 560;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : Canvas;
            FontFamily = new FontFamily("Microsoft YaHei UI");
            FontSize = 14;
            KeyDown += SettingsWindowKeyDown;
            IsVisibleChanged += SettingsVisibilityChanged;
            _balanceTimer.Tick += async delegate { await RefreshBalanceAsync(); };

            BuildWindow();
            LoadValues();
            ClampToWorkArea();
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
            if (!_loadingValues && _startWithWindows != null)
                _startWithWindows.IsChecked = StartupManager.IsEnabled();
        }

        public void RefreshStartupState()
        {
            if (_startWithWindows != null)
                _startWithWindows.IsChecked = StartupManager.IsEnabled();
        }

        public void ShowOcrSettings()
        {
            Navigate("Assistant");
            if (_ocrAiFallback != null) _ocrAiFallback.Focus();
        }

        public void ShowModelSettings()
        {
            Navigate("Model");
            if (_modelApiKey != null && string.IsNullOrWhiteSpace(_modelApiKey.Password))
                _modelApiKey.Focus();
            else if (_modelName != null)
                _modelName.Focus();
        }

        public void SetUpdateStatus(string status, bool checkEnabled)
        {
            if (_updateStatus != null) _updateStatus.Text = status ?? "";
            if (_checkUpdate != null) _checkUpdate.IsEnabled = checkEnabled;
        }

        private void BuildWindow()
        {
            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = shell;

            Border sidebar = BuildSidebar();
            Grid.SetColumn(sidebar, 0);
            shell.Children.Add(sidebar);

            var main = new Grid();
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(70) });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(main, 1);
            shell.Children.Add(main);

            Border header = BuildMainHeader();
            Grid.SetRow(header, 0);
            main.Children.Add(header);

            _contentHost = new Grid { Margin = new Thickness(26, 10, 26, 14) };
            Grid.SetRow(_contentHost, 1);
            main.Children.Add(_contentHost);

            Border footer = BuildFooter();
            Grid.SetRow(footer, 2);
            main.Children.Add(footer);

            RegisterPage("Model", "AI 模型", BuildModelPage());
            RegisterPage("Assistant", "助手偏好", BuildAssistantPage());
            RegisterPage("Shortcuts", "快捷键", BuildShortcutPage());
            RegisterPage("General", "通用", BuildGeneralPage());
            Navigate(_selectedPage);
        }

        private Border BuildSidebar()
        {
            var sidebar = new Border
            {
                Background = SystemParameters.HighContrast
                    ? (Brush)SystemColors.ControlBrush
                    : new LinearGradientBrush(
                        Color.FromRgb(224, 242, 250),
                        Color.FromRgb(240, 248, 252), 90),
                BorderBrush = BorderLine,
                BorderThickness = new Thickness(0, 0, 1, 0)
            };
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebar.Child = layout;

            var brand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(22, 26, 14, 24),
                VerticalAlignment = VerticalAlignment.Center
            };
            try
            {
                brand.Children.Add(new Image
                {
                    Source = new BitmapImage(new Uri(
                        "pack://application:,,,/Sharkey;component/assets/shark-logo.png")),
                    Width = 44, Height = 44, Stretch = Stretch.Uniform,
                    Margin = new Thickness(0, 0, 11, 0)
                });
            }
            catch { }
            var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            brandText.Children.Add(new TextBlock
            {
                Text = "Sharkey", FontSize = 19, FontWeight = FontWeights.SemiBold,
                Foreground = Ink
            });
            brandText.Children.Add(new TextBlock
            {
                Text = "鲨译", FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(1, 1, 0, 0)
            });
            brand.Children.Add(brandText);
            Grid.SetRow(brand, 0);
            layout.Children.Add(brand);

            _navigation = new StackPanel { Margin = new Thickness(13, 0, 13, 8) };
            AddNavigation("Model", "✦", "AI 模型");
            AddNavigation("Assistant", "◈", "助手偏好");
            AddNavigation("Shortcuts", "⌨", "快捷键");
            AddNavigation("General", "⚙", "通用");
            Grid.SetRow(_navigation, 1);
            layout.Children.Add(_navigation);

            var version = new TextBlock
            {
                Text = VersionInfo.SemanticVersion,
                Foreground = Subtle,
                FontSize = 12,
                Margin = new Thickness(24, 12, 18, 22),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(version, 2);
            layout.Children.Add(version);
            return sidebar;
        }

        private void AddNavigation(string code, string glyph, string title)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.Children.Add(new TextBlock
            {
                Text = glyph, FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 17, Foreground = Accent,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            });
            var label = new TextBlock
            {
                Text = title, FontSize = 14, Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            content.Children.Add(label);
            var button = new Button
            {
                Content = content, Tag = code, Height = 48,
                Margin = new Thickness(0, 3, 0, 5), Padding = new Thickness(10, 5, 10, 5),
                BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent,
                Background = Brushes.Transparent, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand, ToolTip = title
            };
            ApplyButtonTemplate(button, 11);
            button.Click += delegate { Navigate(code); };
            _navigationButtons[code] = button;
            _navigation.Children.Add(button);
        }

        private Border BuildMainHeader()
        {
            var header = new Border
            {
                Background = Surface,
                BorderBrush = BorderLine,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 12, 28, 11)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = grid;
            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _pageTitle = new TextBlock
            {
                Text = "AI 模型", FontSize = 22,
                FontWeight = FontWeights.SemiBold, Foreground = Ink
            };
            titles.Children.Add(_pageTitle);
            _headerCurrentEngine = new TextBlock
            {
                Text = "", FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(1, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            titles.Children.Add(_headerCurrentEngine);
            grid.Children.Add(titles);
            return header;
        }

        private Border BuildFooter()
        {
            var footer = new Border
            {
                Background = Surface,
                BorderBrush = BorderLine,
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 10, 24, 11)
            };
            var dock = new DockPanel();
            footer.Child = dock;
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(actions, Dock.Right);
            dock.Children.Add(actions);
            _cancelSettings = SecondaryButton("取消");
            _cancelSettings.Width = 86;
            _cancelSettings.Height = 40;
            _cancelSettings.Margin = new Thickness(0, 0, 9, 0);
            _cancelSettings.ToolTip = "放弃未保存的修改";
            _cancelSettings.Click += delegate { DiscardAndHide(); };
            actions.Children.Add(_cancelSettings);
            _saveButton = PrimaryButton("保存并应用");
            _saveButton.Width = 140;
            _saveButton.Height = 40;
            _saveButton.Click += SaveClick;
            actions.Children.Add(_saveButton);
            _dirtyText = new TextBlock
            {
                Text = "", FontSize = 12, Foreground = Subtle,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            dock.Children.Add(_dirtyText);
            return footer;
        }

        private void RegisterPage(string key, string title, UIElement content)
        {
            _pageTitles[key] = title;
            _contentHost.Children.Add(content);
            content.Visibility = Visibility.Collapsed;
            FrameworkElement element = content as FrameworkElement;
            if (element != null) element.Tag = key;
        }

        private void Navigate(string key)
        {
            if (_contentHost == null || !_pageTitles.ContainsKey(key)) return;
            _selectedPage = key;
            foreach (UIElement child in _contentHost.Children)
            {
                FrameworkElement element = child as FrameworkElement;
                child.Visibility = string.Equals(element == null ? null : element.Tag as string, key,
                    StringComparison.OrdinalIgnoreCase)
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            if (_pageTitle != null) _pageTitle.Text = _pageTitles[key];
            foreach (KeyValuePair<string, Button> item in _navigationButtons)
            {
                bool selected = string.Equals(item.Key, key,
                    StringComparison.OrdinalIgnoreCase);
                item.Value.Background = selected ? Brush("#DDF1FA") : Brushes.Transparent;
                item.Value.BorderBrush = selected ? Brush("#C7E8F4") : Brushes.Transparent;
                item.Value.Foreground = selected ? Accent : Ink;
            }
            UpdateHeaderSummary();
        }

        private UIElement BuildModelPage()
        {
            var root = PageStack();
            root.Children.Add(SectionHeader("当前模型", "翻译、截图识别、外贸助手和识图转表格共用此模型"));
            var savedRow = new Grid { Margin = new Thickness(0, 2, 0, 15) };
            savedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            savedRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _modelSavedSummary = new TextBlock
            {
                Text = "DeepSeek · 尚未配置", FontSize = 15,
                FontWeight = FontWeights.SemiBold, Foreground = Ink,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            savedRow.Children.Add(_modelSavedSummary);
            var balance = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            Grid.SetColumn(balance, 1);
            _balanceText = new TextBlock
            {
                Text = "余额 · —", FontSize = 12, Foreground = Subtle,
                VerticalAlignment = VerticalAlignment.Center
            };
            balance.Children.Add(_balanceText);
            _refreshBalance = new Button
            {
                Content = "↻", FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 16, Width = 28, Height = 28, Margin = new Thickness(3, 0, 0, 0),
                Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                Foreground = Subtle, ToolTip = "刷新已保存当前账户余额", Cursor = Cursors.Hand
            };
            _refreshBalance.Click += async delegate { await RefreshBalanceAsync(); };
            balance.Children.Add(_refreshBalance);
            savedRow.Children.Add(balance);
            root.Children.Add(savedRow);

            root.Children.Add(FieldLabel("服务商"));
            root.Children.Add(BuildVendorSelector());

            Border form = Card();
            form.Padding = new Thickness(20, 17, 20, 18);
            form.Margin = new Thickness(0, 14, 0, 0);
            var fields = new StackPanel();
            form.Child = fields;
            _modelApiKey = AddPassword(fields, "API 密钥", "粘贴服务商 API Key");
            _modelName = AddText(fields, "模型名称", "填写服务商提供的模型 ID；截图与识图转表格请选择支持图片输入的模型");
            _modelName.Margin = new Thickness(0, 4, 0, 4);
            var modelHint = new TextBlock
            {
                Text = "截图识别和识图转表格需要支持图片输入的模型。模型名称由你填写，鲨译不会把示例名称当作已验证模型。",
                TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(1, 5, 0, 0)
            };
            fields.Children.Add(modelHint);

            _connectionExpander = new Expander
            {
                Header = "连接设置", IsExpanded = false,
                Margin = new Thickness(0, 14, 0, 0),
                Foreground = Ink, FontSize = 13
            };
            var connectionFields = new StackPanel
            {
                Margin = new Thickness(0, 10, 0, 2)
            };
            _modelBaseUrl = AddText(connectionFields, "API 地址", "例如 https://api.deepseek.com");
            _modelBaseUrl.Margin = new Thickness(0, 4, 0, 8);
            _modelProtocol = new ComboBox
            {
                ItemsSource = new[] { "OpenAI 兼容 · Chat Completions", "Anthropic · Messages" },
                MinHeight = 34, Margin = new Thickness(0, 4, 0, 5),
                Padding = new Thickness(8, 5, 8, 5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddField(connectionFields, "接口协议", _modelProtocol);
            _modelEndpointHint = new TextBlock
            {
                Text = "", Foreground = Subtle, FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(1, 2, 0, 0)
            };
            connectionFields.Children.Add(_modelEndpointHint);
            _connectionExpander.Content = connectionFields;
            fields.Children.Add(_connectionExpander);
            root.Children.Add(form);

            var testRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 14, 0, 0)
            };
            _testTextButton = PrimaryButton("测试连接");
            _testTextButton.Padding = new Thickness(18, 8, 18, 8);
            _testTextButton.Click += async delegate { await RunTextTestAsync(); };
            testRow.Children.Add(_testTextButton);
            _testImageButton = SecondaryButton("测试图片识别");
            _testImageButton.Padding = new Thickness(16, 8, 16, 8);
            _testImageButton.Margin = new Thickness(9, 0, 0, 0);
            _testImageButton.Click += async delegate { await RunImageTestAsync(); };
            testRow.Children.Add(_testImageButton);
            _modelTestStatus = new TextBlock
            {
                Text = "未测试", FontSize = 12, Foreground = Subtle,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            testRow.Children.Add(_modelTestStatus);
            root.Children.Add(testRow);
            root.Children.Add(new Border { Height = 12, Background = Brushes.Transparent });

            WireModelDraftEvents();
            return Scroll(root);
        }

        private UIElement BuildVendorSelector()
        {
            var selector = new Grid { MinHeight = 42 };
            string[] vendors = { "DeepSeek", "MiMo", "Qwen", "Custom" };
            for (int i = 0; i < vendors.Length; i++)
                selector.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _modelVendor = new ComboBox
            {
                ItemsSource = ModelVendors(), Visibility = Visibility.Collapsed,
                IsHitTestVisible = false, Focusable = false
            };
            // This non-visual selector is the normalized model-vendor source used
            // by migration and reflection-based diagnostics; the visible buttons
            // are the modern segmented control.
            var bar = new Border
            {
                Background = Brush("#EDF4F7"), BorderBrush = BorderLine,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12),
                Padding = new Thickness(4)
            };
            var segments = new Grid();
            for (int i = 0; i < vendors.Length; i++)
                segments.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            foreach (string vendor in vendors)
            {
                Button button = SecondaryButton(ModelVendorDisplayName(vendor));
                button.Tag = vendor;
                button.Margin = new Thickness(1);
                button.Height = 36;
                button.BorderThickness = new Thickness(1);
                button.Click += VendorButtonClick;
                Grid.SetColumn(button, Array.IndexOf(vendors, vendor));
                segments.Children.Add(button);
                _vendorButtons[vendor] = button;
            }
            bar.Child = segments;
            var host = new StackPanel();
            host.Children.Add(bar);
            host.Children.Add(_modelVendor);
            return host;
        }

        private UIElement BuildAssistantPage()
        {
            var root = PageStack();
            root.Children.Add(SectionHeader("翻译与截图", "目标语言、阅读显示和截图上传偏好"));
            var targetCard = Card();
            targetCard.Padding = new Thickness(18);
            var targetFields = new StackPanel();
            targetCard.Child = targetFields;
            _language = new ComboBox
            {
                ItemsSource = new[]
                {
                    "智能 · 中文 → 英文，其他语言 → 简体中文",
                    "简体中文", "繁體中文", "English", "日本語", "한국어", "Français", "Deutsch", "Español"
                },
                MinHeight = 38, Padding = new Thickness(9, 6, 9, 6),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            AddField(targetFields, "翻译目标", _language);
            _targetRule = new TextBlock
            {
                Text = "", FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(1, -1, 0, 12),
                TextWrapping = TextWrapping.Wrap
            };
            targetFields.Children.Add(_targetRule);
            _popupFontSize = new ComboBox
            {
                ItemsSource = new[] { "小", "标准", "大" },
                MinHeight = 36, Width = 150,
                HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 8, 5)
            };
            AddField(targetFields, "浮窗字号", _popupFontSize);
            root.Children.Add(targetCard);

            root.Children.Add(SectionHeader("截图识别", "截图会发送到当前 AI 服务处理"));
            var ocrCard = Card();
            ocrCard.Padding = new Thickness(18);
            var ocrFields = new StackPanel();
            ocrCard.Child = ocrFields;
            _ocrAiFallback = new CheckBox
            {
                Content = "启用 AI 截图识别与翻译", Foreground = Ink,
                FontSize = 14, Margin = new Thickness(0, 0, 0, 12)
            };
            ocrFields.Children.Add(_ocrAiFallback);
            _ocrConsentAllowed = new CheckBox
            {
                Content = "允许将截图发送到当前 AI 模型", Foreground = Ink,
                FontSize = 13, Margin = new Thickness(0, 0, 0, 5)
            };
            ocrFields.Children.Add(_ocrConsentAllowed);
            _ocrConsentStatus = new TextBlock
            {
                Text = "", Foreground = Subtle, FontSize = 12,
                TextWrapping = TextWrapping.Wrap
            };
            ocrFields.Children.Add(_ocrConsentStatus);
            root.Children.Add(ocrCard);

            root.Children.Add(SectionHeader("联网搜索", "默认关闭；只在外贸助手明确需要时查询资料"));
            var searchCard = Card();
            searchCard.Padding = new Thickness(18);
            var searchFields = new StackPanel();
            searchCard.Child = searchFields;
            _commerceSearchEnabled = new CheckBox
            {
                Content = "允许外贸助手联网搜索", Foreground = Ink,
                FontSize = 14, Margin = new Thickness(0, 0, 0, 10)
            };
            searchFields.Children.Add(_commerceSearchEnabled);
            _commerceSearchKey = AddPassword(searchFields, "Tavily API Key", "仅用于外贸助手按需检索公开资料");
            var testSearch = SecondaryButton("测试搜索连接");
            testSearch.HorizontalAlignment = HorizontalAlignment.Left;
            testSearch.Click += async delegate { await TestSearchAsync(testSearch); };
            searchFields.Children.Add(testSearch);
            _searchStatus = new TextBlock
            {
                Text = "", FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(1, 7, 0, 0), TextWrapping = TextWrapping.Wrap
            };
            searchFields.Children.Add(_searchStatus);
            root.Children.Add(searchCard);

            _language.SelectionChanged += delegate
            {
                UpdateTargetRule();
                MarkDirty();
            };
            _popupFontSize.SelectionChanged += delegate { MarkDirty(); };
            _ocrAiFallback.Checked += delegate { MarkDirty(); };
            _ocrAiFallback.Unchecked += delegate { MarkDirty(); };
            _ocrConsentAllowed.Checked += OcrConsentChanged;
            _ocrConsentAllowed.Unchecked += OcrConsentChanged;
            _commerceSearchEnabled.Checked += delegate { MarkDirty(); };
            _commerceSearchEnabled.Unchecked += delegate { MarkDirty(); };
            _commerceSearchKey.PasswordChanged += delegate { MarkDirty(); };
            return Scroll(root);
        }

        private UIElement BuildShortcutPage()
        {
            var root = PageStack();
            root.Children.Add(SectionHeader("快捷键", "点击输入框后按下新的按键或组合键"));
            var card = Card();
            card.Padding = new Thickness(20);
            var list = new StackPanel();
            card.Child = list;
            _writingHotkey = AddHotkeyRecorder(list, "外贸助手", "打开 AI 沟通与计算工作台");
            _translateHotkey = AddHotkeyRecorder(list, "选中翻译", "读取当前选中文字并显示译文");
            _ocrHotkey = AddHotkeyRecorder(list, "截图翻译", "框选屏幕区域进行识别和翻译");
            _settingsHotkey = AddHotkeyRecorder(list, "打开设置", "唤起此设置窗口");
            root.Children.Add(card);
            var defaults = SecondaryButton("恢复默认快捷键");
            defaults.HorizontalAlignment = HorizontalAlignment.Left;
            defaults.Margin = new Thickness(0, 12, 0, 0);
            defaults.Click += delegate
            {
                _writingHotkey.Text = "F7";
                _translateHotkey.Text = "F8";
                _ocrHotkey.Text = "F9";
                _settingsHotkey.Text = "F10";
                MarkDirty();
            };
            root.Children.Add(defaults);
            return Scroll(root);
        }

        private UIElement BuildGeneralPage()
        {
            var root = PageStack();
            root.Children.Add(SectionHeader("通用", "启动、更新与支持工具"));
            var startupCard = Card();
            startupCard.Padding = new Thickness(18);
            _startWithWindows = new CheckBox
            {
                Content = "登录 Windows 后自动启动 Sharkey",
                Foreground = Ink, FontSize = 14
            };
            startupCard.Child = _startWithWindows;
            root.Children.Add(startupCard);
            _startWithWindows.Checked += delegate { MarkDirty(); };
            _startWithWindows.Unchecked += delegate { MarkDirty(); };

            var updates = Card();
            updates.Padding = new Thickness(18);
            var updateFields = new StackPanel();
            updates.Child = updateFields;
            _updateInterval = new ComboBox
            {
                ItemsSource = new[] { "每 6 小时", "每 12 小时", "每 24 小时（推荐）", "每 3 天", "每周", "关闭自动检查" },
                MinHeight = 36, Width = 220, HorizontalAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(8, 5, 8, 5),
                ToolTip = "发现新版本时弹出更新窗口，不会自动安装"
            };
            AddField(updateFields, "自动检查更新", _updateInterval);
            var updateActions = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 5, 0, 0) };
            _checkUpdate = SecondaryButton("检查更新");
            _checkUpdate.Click += delegate
            {
                SetUpdateStatus("正在检查更新…", false);
                EventHandler handler = UpdateCheckRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            updateActions.Children.Add(_checkUpdate);
            _updateStatus = new TextBlock
            {
                Text = "发现新版本时提醒，不自动安装", FontSize = 12,
                Foreground = Subtle, TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            updateActions.Children.Add(_updateStatus);
            updateFields.Children.Add(updateActions);
            root.Children.Add(updates);
            _updateInterval.SelectionChanged += delegate { MarkDirty(); };

            var about = Card();
            about.Padding = new Thickness(18);
            var aboutText = new StackPanel();
            about.Child = aboutText;
            aboutText.Children.Add(new TextBlock
            {
                Text = "Sharkey  " + VersionInfo.SemanticVersion,
                FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Ink
            });
            aboutText.Children.Add(new TextBlock
            {
                Text = "Windows x64 · AI 驱动的翻译与外贸助手",
                FontSize = 12, Foreground = Subtle, Margin = new Thickness(0, 4, 0, 0)
            });
            root.Children.Add(about);

            var diagnostics = new Expander
            {
                Header = "诊断与支持", IsExpanded = false,
                Foreground = Ink, FontSize = 13, Margin = new Thickness(0, 4, 0, 0)
            };
            var diagBody = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            diagBody.Children.Add(new TextBlock
            {
                Text = "诊断摘要不包含 API Key 或翻译原文。", FontSize = 12,
                Foreground = Subtle, Margin = new Thickness(0, 0, 0, 8)
            });
            var diagButtons = new StackPanel { Orientation = Orientation.Horizontal };
            _copyDiagnostics = SecondaryButton("复制诊断信息");
            _copyDiagnostics.Click += CopyDiagnosticsClick;
            diagButtons.Children.Add(_copyDiagnostics);
            _openDiagnosticFolder = SecondaryButton("打开日志目录");
            _openDiagnosticFolder.Margin = new Thickness(8, 0, 0, 0);
            _openDiagnosticFolder.Click += OpenDiagnosticFolderClick;
            diagButtons.Children.Add(_openDiagnosticFolder);
            diagBody.Children.Add(diagButtons);
            diagnostics.Content = diagBody;
            root.Children.Add(diagnostics);
            return Scroll(root);
        }

        private void WireModelDraftEvents()
        {
            _modelApiKey.PasswordChanged += ModelValueChanged;
            _modelName.TextChanged += ModelValueChanged;
            _modelBaseUrl.TextChanged += ModelValueChanged;
            _modelProtocol.SelectionChanged += ModelProtocolChanged;
        }

        private void VendorButtonClick(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            if (button == null) return;
            SelectVendor(button.Tag as string);
        }

        private void SelectVendor(string vendor)
        {
            vendor = NormalizeVendor(vendor);
            if (string.Equals(vendor, _activeModelVendor, StringComparison.OrdinalIgnoreCase))
                return;
            SaveActiveModelDraft();
            _activeModelVendor = vendor;
            _pendingModelVendor = vendor;
            if (_modelVendor != null)
            {
                for (int i = 0; i < _modelVendor.Items.Count; i++)
                {
                    ModelVendorChoice item = _modelVendor.Items[i] as ModelVendorChoice;
                    if (item != null && item.Code == vendor) _modelVendor.SelectedIndex = i;
                }
            }
            LoadModelDraft(vendor);
            if (_connectionExpander != null)
                _connectionExpander.IsExpanded = vendor == "Custom";
            RefreshVendorButtons();
            RefreshConsentPreview();
            ResetModelTest("未测试");
            UpdateHeaderSummary();
            MarkDirty();
        }

        private void RefreshVendorButtons()
        {
            foreach (KeyValuePair<string, Button> item in _vendorButtons)
            {
                bool selected = string.Equals(item.Key, _activeModelVendor,
                    StringComparison.OrdinalIgnoreCase);
                item.Value.Background = selected ? Brush("#E1F3FA") : Brushes.White;
                item.Value.Foreground = selected ? Accent : Ink;
                item.Value.BorderBrush = selected ? Brush("#9FD6E6") : Brushes.Transparent;
                item.Value.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        private void ModelValueChanged(object sender, RoutedEventArgs e)
        {
            if (_loadingValues) return;
            SaveActiveModelDraft();
            ResetModelTest("配置已更改，需重新测试");
            MarkDirty();
        }

        private void ModelProtocolChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loadingValues) return;
            SaveActiveModelDraft();
            UpdateEndpointHint();
            ResetModelTest("配置已更改，需重新测试");
            MarkDirty();
        }

        private void SaveActiveModelDraft()
        {
            if (string.IsNullOrWhiteSpace(_activeModelVendor) || _modelBaseUrl == null) return;
            _modelDrafts[_activeModelVendor] = new ModelConnectionSettings
            {
                BaseUrl = _modelBaseUrl.Text.Trim(),
                ApiKey = _modelApiKey == null ? "" : _modelApiKey.Password.Trim(),
                Model = _modelName == null ? "" : _modelName.Text.Trim(),
                Protocol = SelectedProtocol()
            };
        }

        private void LoadModelDraft(string vendor)
        {
            ModelConnectionSettings value;
            if (!_modelDrafts.TryGetValue(vendor, out value))
            {
                value = new ModelConnectionSettings
                {
                    BaseUrl = RecommendedBaseUrl(vendor, ModelApiProtocols.OpenAI),
                    ApiKey = "", Model = "", Protocol = ModelApiProtocols.OpenAI
                };
                _modelDrafts[vendor] = value;
            }
            bool previous = _loadingValues;
            _loadingValues = true;
            try
            {
                _modelBaseUrl.Text = value.BaseUrl;
                _modelApiKey.Password = value.ApiKey;
                _modelName.Text = value.Model;
                SelectProtocol(value.Protocol);
            }
            finally { _loadingValues = previous; }
            UpdateEndpointHint();
        }

        private void LoadModelDrafts()
        {
            _modelDrafts.Clear();
            foreach (ModelVendorChoice vendor in _modelVendor.Items)
            {
                ModelConnectionSettings value = _settings.GetModelConnection(vendor.Code);
                if (string.IsNullOrWhiteSpace(value.BaseUrl))
                    value.BaseUrl = RecommendedBaseUrl(vendor.Code, value.Protocol);
                _modelDrafts[vendor.Code] = value;
            }
        }

        private void UpdateEndpointHint()
        {
            if (_modelEndpointHint == null) return;
            string url = ModelApiProtocols.BuildEndpoint(
                _modelBaseUrl == null ? "" : _modelBaseUrl.Text,
                SelectedProtocol());
            _modelEndpointHint.Text = "请求地址 · " +
                (string.IsNullOrWhiteSpace(url) ? "填写 API 地址后显示" : url);
        }

        private string SelectedProtocol()
        {
            return _modelProtocol != null && _modelProtocol.SelectedIndex == 1
                ? ModelApiProtocols.Anthropic : ModelApiProtocols.OpenAI;
        }

        private void SelectProtocol(string protocol)
        {
            if (_modelProtocol != null)
                _modelProtocol.SelectedIndex = ModelApiProtocols.IsAnthropic(protocol) ? 1 : 0;
        }

        private async Task RunTextTestAsync()
        {
            SaveActiveModelDraft();
            ModelConnectionSettings connection = ActiveModelDraft();
            string error = ValidateConnection(_activeModelVendor, connection);
            if (error.Length > 0)
            {
                _modelTestStatus.Text = error;
                _modelTestStatus.Foreground = Brush("#B25A28");
                return;
            }
            int generation = BeginModelTest("正在测试文本连接…");
            var cancellation = _modelTestCancellation;
            try
            {
                using (var client = new TranslationClient())
                    await client.TestModelConnectionAsync(_activeModelVendor,
                        connection.Copy(), cancellation.Token);
                if (generation != _testGeneration || cancellation.IsCancellationRequested) return;
                _modelTestStatus.Text = "文本通过";
                _modelTestStatus.Foreground = Brush("#27805A");
            }
            catch (OperationCanceledException) { }
            catch (Exception errorTest)
            {
                if (generation != _testGeneration) return;
                _modelTestStatus.Text = CompactError(errorTest.Message);
                _modelTestStatus.Foreground = Brush("#B25A28");
            }
            finally
            {
                if (generation == _testGeneration)
                {
                    _testTextButton.IsEnabled = true;
                    _testImageButton.IsEnabled = true;
                    _modelTestCancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        private async Task RunImageTestAsync()
        {
            SaveActiveModelDraft();
            ModelConnectionSettings connection = ActiveModelDraft();
            string error = ValidateConnection(_activeModelVendor, connection);
            if (error.Length > 0)
            {
                _modelTestStatus.Text = error;
                _modelTestStatus.Foreground = Brush("#B25A28");
                return;
            }
            int generation = BeginModelTest("正在测试图片识别…");
            var cancellation = _modelTestCancellation;
            try
            {
                using (var image = CreateTestImage())
                using (var client = new TranslationClient())
                {
                    string response = await client.TestImageRecognitionAsync(
                        image, _activeModelVendor, connection.Copy(), cancellation.Token);
                    if (string.IsNullOrWhiteSpace(response))
                        throw new InvalidOperationException("图片请求成功，但模型没有返回文字。");
                }
                if (generation != _testGeneration || cancellation.IsCancellationRequested) return;
                _modelTestStatus.Text = "图片通过";
                _modelTestStatus.Foreground = Brush("#27805A");
            }
            catch (OperationCanceledException) { }
            catch (Exception errorTest)
            {
                if (generation != _testGeneration) return;
                _modelTestStatus.Text = CompactError(errorTest.Message);
                _modelTestStatus.Foreground = Brush("#B25A28");
            }
            finally
            {
                if (generation == _testGeneration)
                {
                    _testTextButton.IsEnabled = true;
                    _testImageButton.IsEnabled = true;
                    _modelTestCancellation = null;
                    cancellation.Dispose();
                }
            }
        }

        private int BeginModelTest(string status)
        {
            if (_modelTestCancellation != null)
            {
                _modelTestCancellation.Cancel();
                _modelTestCancellation.Dispose();
            }
            _modelTestCancellation = new CancellationTokenSource();
            _testGeneration++;
            _modelTestStatus.Text = status;
            _modelTestStatus.Foreground = Subtle;
            _testTextButton.IsEnabled = false;
            _testImageButton.IsEnabled = false;
            return _testGeneration;
        }

        private void ResetModelTest(string text)
        {
            _testGeneration++;
            if (_modelTestCancellation != null)
            {
                _modelTestCancellation.Cancel();
                _modelTestCancellation.Dispose();
                _modelTestCancellation = null;
            }
            if (_modelTestStatus != null)
            {
                _modelTestStatus.Text = text;
                _modelTestStatus.Foreground = Subtle;
            }
            if (_testTextButton != null) _testTextButton.IsEnabled = true;
            if (_testImageButton != null) _testImageButton.IsEnabled = true;
        }

        private ModelConnectionSettings ActiveModelDraft()
        {
            ModelConnectionSettings result;
            return _modelDrafts.TryGetValue(_activeModelVendor, out result)
                ? result.Copy() : new ModelConnectionSettings();
        }

        private static System.Drawing.Bitmap CreateTestImage()
        {
            var bitmap = new System.Drawing.Bitmap(240, 70);
            using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
            using (var font = new System.Drawing.Font("Arial", 19, System.Drawing.FontStyle.Bold))
            using (var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(30, 48, 65)))
            {
                graphics.Clear(System.Drawing.Color.White);
                graphics.DrawString("SHARKEY TEST", font, brush, 10, 17);
            }
            return bitmap;
        }

        private async Task TestSearchAsync(Button button)
        {
            if (string.IsNullOrWhiteSpace(_commerceSearchKey.Password))
            {
                _searchStatus.Text = "请先填写 Tavily API Key。";
                _searchStatus.Foreground = Brush("#B25A28");
                return;
            }
            button.IsEnabled = false;
            _searchStatus.Text = "正在测试…";
            _searchStatus.Foreground = Subtle;
            try
            {
                using (var search = new CommerceSearch())
                    await search.SearchAsync("international trade",
                        _commerceSearchKey.Password.Trim(), CancellationToken.None);
                _searchStatus.Text = "搜索连接通过";
                _searchStatus.Foreground = Brush("#27805A");
            }
            catch (Exception error)
            {
                _searchStatus.Text = CompactError(error.Message);
                _searchStatus.Foreground = Brush("#B25A28");
            }
            finally { button.IsEnabled = true; }
        }

        private void OcrConsentChanged(object sender, RoutedEventArgs e)
        {
            if (_loadingValues) return;
            if (_ocrConsentStatus != null)
                _ocrConsentStatus.Text = _ocrConsentAllowed.IsChecked == true
                    ? "更换 AI 服务或清除授权后，会在首次截图上传前确认。"
                    : "未授权时，首次截图上传会再次询问。";
            MarkDirty();
        }

        private void RefreshConsentPreview()
        {
            if (_ocrConsentAllowed == null || _ocrConsentStatus == null) return;
            ModelConnectionSettings connection = ActiveModelDraft();
            string target = ConsentTarget(_pendingModelVendor, connection);
            bool matches = _settings.OcrAiConsentGranted &&
                string.Equals(_settings.OcrConsentTarget, target,
                    StringComparison.OrdinalIgnoreCase);
            bool wasLoading = _loadingValues;
            _loadingValues = true;
            _ocrConsentAllowed.IsChecked = matches;
            _loadingValues = wasLoading;
            _ocrConsentStatus.Text = matches
                ? "已授权 · " + ModelVendorDisplayName(_pendingModelVendor) + " · " +
                    (connection.Model ?? "")
                : "未授权所选服务；首次截图上传时会再次询问。";
        }

        private async Task RefreshBalanceAsync()
        {
            if (!IsVisible || _balanceText == null) return;
            string vendor = _settings.ModelVendor;
            ModelConnectionSettings connection = _settings.GetModelConnection(vendor);
            string identity = vendor + "\n" + connection.BaseUrl + "\n" + connection.ApiKey;
            if (identity != _balanceIdentity)
            {
                if (_balanceRequest != null) _balanceRequest.Cancel();
                _balanceRequest = null;
                _balanceIdentity = identity;
                _balanceValue = null;
            }
            if (_balanceRequest != null) return;
            string unavailable = AiBalanceService.UnavailableReason(vendor, connection);
            string prefix = vendor + " 余额 · ";
            if (unavailable.Length != 0)
            {
                _balanceText.Text = prefix + unavailable;
                _refreshBalance.IsEnabled = false;
                return;
            }
            var request = new CancellationTokenSource();
            _balanceRequest = request;
            _refreshBalance.IsEnabled = false;
            if (_balanceValue == null) _balanceText.Text = prefix + "查询中…";
            try
            {
                string value;
                using (var service = new AiBalanceService())
                    value = await service.QueryAsync(vendor, connection, request.Token);
                if (_balanceRequest != request || !IsVisible) return;
                ModelConnectionSettings current = _settings.GetModelConnection(_settings.ModelVendor);
                if (identity != _settings.ModelVendor + "\n" + current.BaseUrl + "\n" + current.ApiKey)
                    return;
                _balanceValue = value;
                _balanceUpdated = DateTime.Now;
                _balanceText.Text = prefix + value;
                _balanceText.ToolTip = "更新于 " + _balanceUpdated.ToString("HH:mm:ss") +
                    "；每 60 秒刷新，金额以服务商账单为准。";
            }
            catch
            {
                if (_balanceRequest != request || !IsVisible) return;
                _balanceText.Text = prefix + (_balanceValue == null ? "暂时无法查询" : _balanceValue + " · 更新失败");
                _balanceText.ToolTip = _balanceValue == null ? "请检查网络和账户后重试。" :
                    "上次成功更新：" + _balanceUpdated.ToString("HH:mm:ss") + "；当前显示旧值。";
            }
            finally
            {
                if (_balanceRequest == request)
                {
                    _balanceRequest = null;
                    _refreshBalance.IsEnabled = true;
                }
                request.Dispose();
            }
        }

        private void SettingsVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (IsVisible)
            {
                if (_reloadOnShow)
                {
                    LoadValues();
                    _reloadOnShow = false;
                }
                _balanceTimer.Start();
                Task ignored = RefreshBalanceAsync();
            }
            else
            {
                _balanceTimer.Stop();
                if (_balanceRequest != null)
                {
                    _balanceRequest.Cancel();
                    _balanceRequest = null;
                }
                ResetModelTest("未测试");
            }
        }

        private void LoadValues()
        {
            _loadingValues = true;
            try
            {
                _draft = _settings.Copy();
                _draft.Provider = "ModelApi";
                _modelDrafts.Clear();
                foreach (ModelVendorChoice item in _modelVendor.Items)
                {
                    ModelConnectionSettings connection = _settings.GetModelConnection(item.Code);
                    if (string.IsNullOrWhiteSpace(connection.BaseUrl))
                        connection.BaseUrl = RecommendedBaseUrl(item.Code, connection.Protocol);
                    _modelDrafts[item.Code] = connection;
                }
                _activeModelVendor = NormalizeVendor(_settings.ModelVendor);
                _pendingModelVendor = _activeModelVendor;
                if (!_modelDrafts.ContainsKey(_activeModelVendor))
                {
                    _activeModelVendor = "DeepSeek";
                    _pendingModelVendor = _activeModelVendor;
                }
                for (int i = 0; i < _modelVendor.Items.Count; i++)
                {
                    ModelVendorChoice item = _modelVendor.Items[i] as ModelVendorChoice;
                    if (item != null && item.Code == _activeModelVendor) _modelVendor.SelectedIndex = i;
                }
                LoadModelDraft(_activeModelVendor);
                _connectionExpander.IsExpanded = _activeModelVendor == "Custom";
                RefreshVendorButtons();

                _language.SelectedIndex = string.Equals(_settings.TargetLanguageMode,
                    "Fixed", StringComparison.OrdinalIgnoreCase)
                    ? LanguageIndex(_settings.TargetLanguage) + 1 : 0;
                _popupFontSize.SelectedIndex = _settings.PopupFontSize == "Small" ? 0 :
                    _settings.PopupFontSize == "Large" ? 2 : 1;
                _ocrAiFallback.IsChecked = _settings.OcrAiFallback;
                RefreshConsentPreview();
                _commerceSearchEnabled.IsChecked = _settings.CommerceSearchEnabled;
                _commerceSearchKey.Password = _settings.CommerceSearchApiKey;
                _writingHotkey.Text = NormalizeHotkey(_settings.WritingHotkey, "F7");
                _translateHotkey.Text = NormalizeHotkey(_settings.TranslateHotkey, "F8");
                _ocrHotkey.Text = NormalizeHotkey(_settings.OcrHotkey, "F9");
                _settingsHotkey.Text = NormalizeHotkey(_settings.SettingsHotkey, "F10");
                _startWithWindows.IsChecked = StartupManager.IsEnabled();
                int update = Array.IndexOf(UpdateIntervals,
                    AppSettings.NormalizeUpdateCheckHours(_settings.UpdateCheckHours));
                _updateInterval.SelectedIndex = update < 0 ? 2 : update;
                _searchStatus.Text = "";
                UpdateTargetRule();
                UpdateHeaderSummary();
                ResetModelTest("未测试");
                _isDirty = false;
                UpdateDirtyStatus();
            }
            finally { _loadingValues = false; }
        }

        private void SaveClick(object sender, RoutedEventArgs e)
        {
            HotkeyGesture translate, ocr, settingsHotkey, writing;
            if (!ValidateHotkeys(out translate, out ocr, out settingsHotkey, out writing))
                return;
            SaveActiveModelDraft();
            ModelConnectionSettings active = ActiveModelDraft();
            string validation = ValidateConnection(_pendingModelVendor, active);
            if (validation.Length > 0)
            {
                Navigate("Model");
                _modelTestStatus.Text = validation;
                _modelTestStatus.Foreground = Brush("#B25A28");
                if (string.IsNullOrWhiteSpace(active.ApiKey) && _pendingModelVendor != "Custom")
                    _modelApiKey.Focus();
                else if (string.IsNullOrWhiteSpace(active.Model)) _modelName.Focus();
                else _modelBaseUrl.Focus();
                return;
            }
            if (_commerceSearchEnabled.IsChecked == true &&
                string.IsNullOrWhiteSpace(_commerceSearchKey.Password))
            {
                Navigate("Assistant");
                MessageBox.Show("启用联网搜索需要先填写 Tavily API Key。", "助手偏好",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                _commerceSearchKey.Focus();
                return;
            }

            AppSettings candidate = _draft.Copy();
            candidate.Provider = "ModelApi";
            candidate.ModelVendor = _pendingModelVendor;
            foreach (KeyValuePair<string, ModelConnectionSettings> profile in _modelDrafts)
                candidate.SetModelConnection(profile.Key, profile.Value.Copy());
            candidate.ModelBaseUrl = active.BaseUrl;
            candidate.ModelApiKey = active.ApiKey;
            candidate.ModelName = active.Model;
            candidate.ModelProtocol = ModelApiProtocols.Normalize(active.Protocol);
            candidate.TargetLanguageMode = _language.SelectedIndex <= 0 ? "Smart" : "Fixed";
            if (_language.SelectedIndex > 0)
                candidate.TargetLanguage = LanguageCodes[_language.SelectedIndex - 1];
            candidate.OcrAiFallback = _ocrAiFallback.IsChecked == true;
            candidate.CommerceSearchEnabled = _commerceSearchEnabled.IsChecked == true;
            candidate.CommerceSearchApiKey = _commerceSearchKey.Password.Trim();
            candidate.TranslateHotkey = translate.Display;
            candidate.OcrHotkey = ocr.Display;
            candidate.SettingsHotkey = settingsHotkey.Display;
            candidate.WritingHotkey = writing.Display;
            candidate.StartWithWindows = _startWithWindows.IsChecked == true;
            candidate.PopupFontSize = _popupFontSize.SelectedIndex == 0 ? "Small" :
                _popupFontSize.SelectedIndex == 2 ? "Large" : "Standard";
            candidate.UpdateCheckHours = _updateInterval.SelectedIndex < 0
                ? 24 : UpdateIntervals[_updateInterval.SelectedIndex];

            string consentTarget = ConsentTarget(candidate.ModelVendor, active);
            bool consentStillValid = _ocrConsentAllowed.IsChecked == true &&
                candidate.OcrAiConsentGranted &&
                string.Equals(candidate.OcrConsentTarget, consentTarget,
                    StringComparison.OrdinalIgnoreCase);
            if (!consentStillValid)
            {
                candidate.OcrAiConsentGranted = false;
                candidate.OcrConsentTarget = "";
            }

            bool previousStartup = StartupManager.IsEnabled();
            try
            {
                StartupManager.SetEnabled(candidate.StartWithWindows);
                candidate.Save();
                _settings.CopyFrom(candidate);
                _draft = candidate.Copy();
                _reloadOnShow = true;
                _isDirty = false;
                UpdateDirtyStatus();
                UpdateHeaderSummary();
                EventHandler handler = SettingsSaved;
                if (handler != null) handler(this, EventArgs.Empty);
                Hide();
            }
            catch (Exception error)
            {
                try { StartupManager.SetEnabled(previousStartup); }
                catch (Exception rollbackError)
                {
                    DiagnosticLog.Write("Startup rollback failed; type=" + rollbackError.GetType().Name);
                }
                _dirtyText.Text = "保存失败，编辑内容已保留";
                MessageBox.Show("保存失败：" + error.Message +
                    "\n\n设置没有部分应用；请修复问题后重试。", "鲨译",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private bool ValidateHotkeys(
            out HotkeyGesture translate,
            out HotkeyGesture ocr,
            out HotkeyGesture settings,
            out HotkeyGesture writing)
        {
            translate = ocr = settings = writing = null;
            if (!ParseHotkey(_translateHotkey, "选中翻译", out translate)) return false;
            if (!ParseHotkey(_ocrHotkey, "截图翻译", out ocr)) return false;
            if (!ParseHotkey(_settingsHotkey, "打开设置", out settings)) return false;
            if (!ParseHotkey(_writingHotkey, "外贸助手", out writing)) return false;
            var all = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            HotkeyGesture[] gestures = { translate, ocr, settings, writing };
            string[] labels = { "选中翻译", "截图翻译", "打开设置", "外贸助手" };
            for (int i = 0; i < gestures.Length; i++)
            {
                string prior;
                if (all.TryGetValue(gestures[i].Display, out prior))
                {
                    MessageBox.Show("“" + labels[i] + "”与“" + prior + "”使用了相同快捷键。",
                        "快捷键冲突", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Navigate("Shortcuts");
                    return false;
                }
                all[gestures[i].Display] = labels[i];
            }
            return true;
        }

        private bool ParseHotkey(TextBox input, string name, out HotkeyGesture gesture)
        {
            string error;
            if (HotkeyGesture.TryParse(input.Text, out gesture, out error)) return true;
            MessageBox.Show("“" + name + "”快捷键无效：" + error,
                "快捷键无效", MessageBoxButton.OK, MessageBoxImage.Warning);
            Navigate("Shortcuts");
            input.Focus();
            return false;
        }

        private void DiscardAndHide()
        {
            LoadValues();
            _reloadOnShow = true;
            Hide();
        }

        private void SettingsWindowKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            DiscardAndHide();
            e.Handled = true;
        }

        private void AddHotkeyEvents(TextBox recorder) { }

        private TextBox AddHotkeyRecorder(Panel parent, string title, string hint)
        {
            var row = new Grid { Margin = new Thickness(0, 0, 0, 15) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(190) });
            var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            label.Children.Add(new TextBlock
            {
                Text = title, FontSize = 14, FontWeight = FontWeights.Medium, Foreground = Ink
            });
            label.Children.Add(new TextBlock
            {
                Text = hint, FontSize = 12, Foreground = Subtle,
                Margin = new Thickness(0, 3, 0, 0), TextWrapping = TextWrapping.Wrap
            });
            row.Children.Add(label);
            var recorder = new TextBox
            {
                Height = 38, IsReadOnly = true, Cursor = Cursors.Hand,
                TextAlignment = TextAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold, Foreground = Accent,
                Background = Brush("#F3FAFC"), BorderBrush = BorderLine,
                ToolTip = "点击后按下快捷键"
            };
            recorder.PreviewKeyDown += HotkeyRecorderKeyDown;
            recorder.TextChanged += delegate { MarkDirty(); };
            Grid.SetColumn(recorder, 1);
            row.Children.Add(recorder);
            parent.Children.Add(row);
            return recorder;
        }

        private void HotkeyRecorderKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape || e.Key == Key.Tab) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            HotkeyGesture gesture;
            string error;
            if (HotkeyGesture.TryFromKeyEvent(key, Keyboard.Modifiers, out gesture, out error))
            {
                ((TextBox)sender).Text = gesture.Display;
                ((TextBox)sender).ToolTip = "已录制 " + gesture.Display;
            }
            else ((TextBox)sender).ToolTip = error;
            e.Handled = true;
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
                MessageBox.Show("复制诊断信息失败：" + error.Message, "Sharkey",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    FileName = "explorer.exe", Arguments = "/select,\"" + DiagnosticLog.FilePath + "\"",
                    UseShellExecute = true
                });
            }
            catch (Exception error)
            {
                MessageBox.Show("无法打开日志目录：" + error.Message + "\n\n" +
                    DiagnosticLog.FolderPath, "Sharkey",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadModelSummary()
        {
            UpdateHeaderSummary();
        }

        private void UpdateHeaderSummary()
        {
            if (_headerCurrentEngine != null)
            {
                ModelConnectionSettings current = _settings.GetModelConnection(_settings.ModelVendor);
                string model = string.IsNullOrWhiteSpace(current.Model) ? "尚未配置模型" : current.Model;
                _headerCurrentEngine.Text = _selectedPage == "Model" ? "" :
                    "当前 AI · " + ModelVendorDisplayName(_settings.ModelVendor) + " · " + model;
                _headerCurrentEngine.Visibility = _selectedPage == "Model"
                    ? Visibility.Collapsed : Visibility.Visible;
                _headerCurrentEngine.ToolTip = _headerCurrentEngine.Text;
            }
            if (_modelSavedSummary != null)
            {
                ModelConnectionSettings current = _settings.GetModelConnection(_settings.ModelVendor);
                _modelSavedSummary.Text = ModelVendorDisplayName(_settings.ModelVendor) + " · " +
                    (string.IsNullOrWhiteSpace(current.Model) ? "尚未配置" : current.Model);
            }
        }

        private void UpdateTargetRule()
        {
            if (_targetRule == null || _language == null) return;
            _targetRule.Text = _language.SelectedIndex <= 0
                ? "中文 → English；其他语言 → 简体中文。"
                : "所有内容固定翻译为 " + LanguageNames[_language.SelectedIndex - 1] + "。";
        }

        private void MarkDirty()
        {
            if (_loadingValues) return;
            _isDirty = true;
            UpdateDirtyStatus();
        }

        private void UpdateDirtyStatus()
        {
            if (_dirtyText != null)
                _dirtyText.Text = _isDirty ? "● 有未保存的更改" : "";
            if (_saveButton != null) _saveButton.IsDefault = _isDirty;
        }

        private void ClampToWorkArea()
        {
            double workHeight = SystemParameters.WorkArea.Height;
            double workWidth = SystemParameters.WorkArea.Width;
            Height = Math.Max(MinHeight, Math.Min(Height, workHeight));
            Width = Math.Max(MinWidth, Math.Min(Width, workWidth));
        }

        private void UpdateHeaderFromDraft() { }

        private static string ValidateConnection(string vendor, ModelConnectionSettings connection)
        {
            if (connection == null) return "模型配置不可用。";
            Uri uri;
            if (!Uri.TryCreate((connection.BaseUrl ?? "").Trim(), UriKind.Absolute, out uri) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return "请填写有效的 HTTP 或 HTTPS API 地址。";
            if (string.IsNullOrWhiteSpace(connection.Model)) return "请填写模型名称。";
            if (!connection.IsUsable(vendor))
                return vendor == "Custom" && ModelConnectionSettings.IsLocalEndpoint(connection.BaseUrl)
                    ? "自定义模型配置不完整。" : "请填写 API 密钥。";
            return "";
        }

        private static string ConsentTarget(string vendor, ModelConnectionSettings connection)
        {
            return (vendor ?? "").ToLowerInvariant() + "|" +
                ((connection == null ? "" : connection.BaseUrl) ?? "")
                    .Trim().TrimEnd('/').ToLowerInvariant();
        }

        private void UpdateModelTestButtonState()
        {
            if (_testTextButton == null || _testImageButton == null) return;
            bool ready = ValidateConnection(_activeModelVendor, ActiveModelDraft()).Length == 0;
            _testTextButton.IsEnabled = ready;
            _testImageButton.IsEnabled = ready;
        }

        private static string CompactError(string message)
        {
            string value = string.IsNullOrWhiteSpace(message) ? "请求失败。" : message.Trim();
            return value.Length > 150 ? value.Substring(0, 150) + "…" : value;
        }

        private static string NormalizeHotkey(string value, string fallback)
        {
            HotkeyGesture gesture;
            string error;
            return HotkeyGesture.TryParse(value, out gesture, out error)
                ? gesture.Display : fallback;
        }

        private static string NormalizeVendor(string vendor)
        {
            if (string.Equals(vendor, "MiMo", StringComparison.OrdinalIgnoreCase)) return "MiMo";
            if (string.Equals(vendor, "Qwen", StringComparison.OrdinalIgnoreCase)) return "Qwen";
            if (string.Equals(vendor, "Custom", StringComparison.OrdinalIgnoreCase)) return "Custom";
            return "DeepSeek";
        }

        private static string ModelVendorDisplayName(string vendor)
        {
            switch (NormalizeVendor(vendor))
            {
                case "MiMo": return "MiMo";
                case "Qwen": return "Qwen";
                case "Custom": return "自定义";
                default: return "DeepSeek";
            }
        }

        private static string RecommendedBaseUrl(string vendor, string protocol)
        {
            if (ModelApiProtocols.IsAnthropic(protocol))
            {
                if (vendor == "DeepSeek") return "https://api.deepseek.com/anthropic";
                if (vendor == "MiMo") return "https://api.xiaomimimo.com/anthropic";
            }
            switch (vendor)
            {
                case "DeepSeek": return "https://api.deepseek.com";
                case "MiMo": return "https://api.xiaomimimo.com/v1";
                case "Qwen": return "https://dashscope.aliyuncs.com/compatible-mode/v1";
                default: return "https://api.openai.com/v1";
            }
        }

        private static List<ModelVendorChoice> ModelVendors()
        {
            return new List<ModelVendorChoice>
            {
                new ModelVendorChoice("DeepSeek", "DeepSeek"),
                new ModelVendorChoice("MiMo", "MiMo"),
                new ModelVendorChoice("Qwen", "Qwen"),
                new ModelVendorChoice("Custom", "自定义")
            };
        }

        private static int LanguageIndex(string code)
        {
            int index = Array.IndexOf(LanguageCodes, code);
            return index < 0 ? 0 : index;
        }

        private static StackPanel PageStack()
        {
            return new StackPanel { Margin = new Thickness(0, 4, 8, 12) };
        }

        private static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent,
                PanningMode = PanningMode.VerticalOnly
            };
        }

        private static StackPanel SectionHeader(string title, string subtitle)
        {
            var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            panel.Children.Add(new TextBlock
            {
                Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
                Foreground = Ink
            });
            if (!string.IsNullOrWhiteSpace(subtitle))
                panel.Children.Add(new TextBlock
                {
                    Text = subtitle, FontSize = 12, Foreground = Subtle,
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0)
                });
            return panel;
        }

        private static Border Card()
        {
            return new Border
            {
                Background = Surface, BorderBrush = BorderLine,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14),
                Margin = new Thickness(0, 0, 0, 12)
            };
        }

        private static TextBlock FieldLabel(string text)
        {
            return new TextBlock
            {
                Text = text, FontSize = 12, FontWeight = FontWeights.SemiBold,
                Foreground = Ink, Margin = new Thickness(0, 0, 0, 6)
            };
        }

        private static void AddField(Panel panel, string label, UIElement control)
        {
            panel.Children.Add(FieldLabel(label));
            FrameworkElement element = control as FrameworkElement;
            if (element != null) element.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(control);
        }

        private static TextBox AddText(Panel panel, string label, string hint)
        {
            panel.Children.Add(FieldLabel(label));
            var box = new TextBox
            {
                MinHeight = 38, Padding = new Thickness(10, 7, 10, 7),
                BorderBrush = BorderLine, Background = Brushes.White,
                ToolTip = hint, FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            box.Margin = new Thickness(0, 0, 0, 12);
            panel.Children.Add(box);
            return box;
        }

        private PasswordBox AddPassword(Panel panel, string label, string hint)
        {
            panel.Children.Add(FieldLabel(label));
            var host = new Grid { Height = 40, Margin = new Thickness(0, 0, 0, 12), ToolTip = hint };
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var password = new PasswordBox
            {
                Padding = new Thickness(10, 7, 10, 7), BorderBrush = BorderLine,
                Background = Brushes.White, FontSize = 13, VerticalContentAlignment = VerticalAlignment.Center
            };
            var revealed = new TextBox
            {
                Padding = new Thickness(10, 7, 10, 7), BorderBrush = BorderLine,
                Background = Brushes.White, FontSize = 13,
                Visibility = Visibility.Collapsed, VerticalContentAlignment = VerticalAlignment.Center
            };
            var button = SecondaryButton("显示");
            button.Width = 62;
            button.Height = 40;
            button.Margin = new Thickness(7, 0, 0, 0);
            Grid.SetColumn(password, 0);
            Grid.SetColumn(revealed, 0);
            Grid.SetColumn(button, 1);
            host.Children.Add(password);
            host.Children.Add(revealed);
            host.Children.Add(button);
            panel.Children.Add(host);
            _revealedKeys[password] = revealed;
            password.PasswordChanged += delegate { if (revealed.Text != password.Password) revealed.Text = password.Password; };
            revealed.TextChanged += delegate
            {
                if (revealed.Visibility == Visibility.Visible && password.Password != revealed.Text)
                    password.Password = revealed.Text;
            };
            button.Click += delegate
            {
                bool show = revealed.Visibility != Visibility.Visible;
                revealed.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                password.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
                button.Content = show ? "隐藏" : "显示";
                if (show) { revealed.Focus(); revealed.CaretIndex = revealed.Text.Length; }
                else password.Focus();
            };
            return password;
        }

        private static Button PrimaryButton(string text)
        {
            var button = new Button
            {
                Content = text, MinHeight = 38, Padding = new Thickness(14, 8, 14, 8),
                Background = Accent, Foreground = Brushes.White,
                BorderBrush = Accent, BorderThickness = new Thickness(1),
                FontSize = 13, FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
            ApplyButtonTemplate(button, 10);
            return button;
        }

        private static Button SecondaryButton(string text)
        {
            var button = new Button
            {
                Content = text, MinHeight = 34, Padding = new Thickness(12, 6, 12, 6),
                Background = Brushes.White, Foreground = Ink,
                BorderBrush = BorderLine, BorderThickness = new Thickness(1),
                FontSize = 13, Cursor = Cursors.Hand
            };
            ApplyButtonTemplate(button, 9);
            return button;
        }

        private static void ApplyButtonTemplate(Button button, double radius)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Chrome";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(radius));
            border.SetBinding(Border.BackgroundProperty, new Binding("Background")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderBrushProperty, new Binding("BorderBrush")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.SetBinding(Border.BorderThicknessProperty, new Binding("BorderThickness")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.ContentProperty, new Binding("Content")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            presenter.SetBinding(ContentPresenter.ContentTemplateProperty, new Binding("ContentTemplate")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            presenter.SetBinding(ContentPresenter.MarginProperty, new Binding("Padding")
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
            border.AppendChild(presenter);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(UIElement.OpacityProperty, 0.9));
            template.Triggers.Add(hover);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, 0.5));
            template.Triggers.Add(disabled);
            button.Template = template;
        }

        private static SolidColorBrush Brush(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }

        private sealed class ModelVendorChoice
        {
            public readonly string Code;
            private readonly string _display;
            public ModelVendorChoice(string code, string display) { Code = code; _display = display; }
            public override string ToString() { return _display; }
        }
    }
}
