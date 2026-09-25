using System;
using System.Collections.Generic;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using Bitmap = System.Drawing.Bitmap;

namespace GlobalTranslator
{
    internal sealed class WritingWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly TranslationClient _client;
        private readonly Action _openModelSettings;
        private readonly List<byte[]> _images = new List<byte[]>();
        private readonly List<CommunicationTurn> _turns = new List<CommunicationTurn>();
        private readonly TextBox _background = Editor(100);
        private readonly TextBox _intent = Editor(100);
        private readonly TextBox _adjustment = Editor(55);
        private readonly TextBox _result = Editor(120);
        private readonly TextBox _meaning = Editor(70);
        private readonly TextBox _advice = Editor(85);
        private readonly TextBox _calculation = Editor(95);
        private readonly TextBox _sources = Editor(75);
        private readonly TextBlock _staleNotice = new TextBlock
        {
            Text = "材料已更改，回复尚未更新",
            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(143, 91, 29)),
            Visibility = Visibility.Collapsed
        };
        private readonly TextBlock _editNotice = new TextBlock
        {
            Text = "回复已编辑；中文释义为生成时版本",
            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(87, 111, 124)),
            Visibility = Visibility.Collapsed
        };
        private TextBlock _adjustmentLabel;
        private TextBlock _calculationLabel;
        private TextBlock _sourcesLabel;
        private WrapPanel _calculationActions;
        private WrapPanel _sourceActions;
        private readonly StackPanel _conversationHistory = new StackPanel
        {
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, 14, 0, 0)
        };
        private Grid _workGrid;
        private Border _leftPane;
        private Border _rightPane;
        private StackPanel _narrowTabs;
        private bool _showingReply;
        private readonly WrapPanel _imageList = new WrapPanel();
        private readonly TextBlock _status = new TextBlock
            { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        private readonly TextBlock _modelSummary = new TextBlock
        {
            FontSize = 11.5,
            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(101, 124, 135))
        };
        private readonly TextBlock _emptyResult = new TextBlock
        {
            Text = "把客户消息、截图或你的想法放到左侧，\n生成后在这里核对可发送的回复。",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 14,
            Foreground = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(108, 131, 143)),
            Margin = new Thickness(5, 18, 5, 12)
        };
        private readonly ComboBox _language = new ComboBox
            { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _copy;
        private readonly Button _generate;
        private readonly Button _adviceOnly;
        private readonly Button _stop;
        private CancellationTokenSource _request;
        private CommunicationResult _lastResult;
        private int _successful;
        private bool _composing;
        private bool _loading;
        private bool _allowClose;
        internal bool IsCapturing { get; private set; }
        private static readonly string[] Codes =
            { "auto", "en", "ja", "ko", "de", "fr", "es", "ru", "ar", "pt", "zh-Hans" };

        public WritingWindow(AppSettings settings, TranslationClient client,
            Action openModelSettings)
        {
            _settings = settings;
            _client = client;
            _openModelSettings = openModelSettings;
            Title = "鲨译 · 外贸沟通助手";
            Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 250, 253));
            FontFamily = new FontFamily("Microsoft YaHei UI");
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            FitToWorkArea();
            SourceInitialized += delegate { PositionInitialWorkbench(); };

            var root = new DockPanel { Margin = new Thickness(16) };
            Content = root;
            var footer = new StackPanel { Margin = new Thickness(0, 7, 0, 0) };
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            footer.Children.Add(_status);
            var actions = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            _generate = Action("生成回复", async delegate { await Generate(false); });
            StyleAction(_generate, true);
            _adviceOnly = Action("仅给建议", async delegate { await Generate(true); });
            actions.Children.Add(_generate);
            actions.Children.Add(_adviceOnly);
            _stop = Action("停止", CancelRequest);
            _stop.Visibility = Visibility.Collapsed;
            actions.Children.Add(_stop);
            _copy = Action("复制回复", CopyReply);
            StyleAction(_copy, true);
            _copy.IsEnabled = false;
            var header = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);
            var headerActions = new WrapPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(headerActions, Dock.Right);
            header.Children.Add(headerActions);
            headerActions.Children.Add(Action("新建沟通", NewConversation));
            headerActions.Children.Add(Action("AI 设置", delegate
                { if (_openModelSettings != null) _openModelSettings(); }));
            var heading = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            heading.Children.Add(new TextBlock
            {
                Text = "外贸助手", FontSize = 19,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(22, 69, 91)),
                VerticalAlignment = VerticalAlignment.Center
            });
            heading.Children.Add(_modelSummary);
            header.Children.Add(heading);
            RefreshModelSummary();
            _narrowTabs = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 8)
            };
            _narrowTabs.Children.Add(Action("材料与意图", delegate
            {
                _showingReply = false;
                UpdateWorkbenchLayout();
            }));
            _narrowTabs.Children.Add(Action("回复与核对", delegate
            {
                _showingReply = true;
                UpdateWorkbenchLayout();
            }));
            DockPanel.SetDock(_narrowTabs, Dock.Top);
            root.Children.Add(_narrowTabs);

            _workGrid = new Grid();
            _workGrid.ColumnDefinitions.Add(new ColumnDefinition());
            _workGrid.ColumnDefinitions.Add(new ColumnDefinition());
            _workGrid.ColumnDefinitions.Add(new ColumnDefinition());
            root.Children.Add(_workGrid);
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _leftPane = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(216, 230, 235)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 12, 10),
                Child = scroll
            };
            _workGrid.Children.Add(_leftPane);
            var splitter = new GridSplitter
            {
                Width = 7, Background = System.Windows.Media.Brushes.Transparent,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetColumn(splitter, 1);
            _workGrid.Children.Add(splitter);
            var replyScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _rightPane = new Border
            {
                Background = System.Windows.Media.Brushes.White,
                BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(216, 230, 235)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 10, 12, 10),
                Child = replyScroll
            };
            Grid.SetColumn(_rightPane, 2);
            _workGrid.Children.Add(_rightPane);
            var content = new StackPanel();
            scroll.Content = content;
            var reply = new StackPanel();
            replyScroll.Content = reply;
            var languageRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            languageRow.Children.Add(new TextBlock
                { Text = "回复语言", Margin = new Thickness(0, 5, 8, 0) });
            foreach (string name in new[] { "自动（跟随客户，否则英语）", "英语", "日语",
                "韩语", "德语", "法语", "西班牙语", "俄语", "阿拉伯语", "葡萄牙语", "简体中文" })
                _language.Items.Add(name);
            int saved = Array.IndexOf(Codes, settings.CommunicationLanguage);
            _language.SelectedIndex = saved < 0 ? 0 : saved;
            _language.SelectionChanged += delegate { OnMaterialChanged(); };
            languageRow.Children.Add(_language);
            content.Children.Add(languageRow);

            content.Children.Add(Label("客户消息 / 背景（可选）"));
            content.Children.Add(_background);
            _background.ToolTip = "粘贴客户聊天内容、邮件或其他背景文字";
            content.Children.Add(Label("我的想法 / 要求"));
            content.Children.Add(_intent);
            _intent.ToolTip = "写出零散要点即可。Enter 生成，Shift+Enter 换行";
            var imageActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 4) };
            imageActions.Children.Add(Action("框选截图", async delegate
                { await CaptureImage(); }));
            imageActions.Children.Add(Action("粘贴图片", PasteImage));
            imageActions.Children.Add(Action("添加图片", AddFiles));
            content.Children.Add(imageActions);
            content.Children.Add(_imageList);
            content.Children.Add(actions);
            _adjustmentLabel = Label("继续调整");
            _adjustmentLabel.Visibility = Visibility.Collapsed;
            content.Children.Add(_adjustmentLabel);
            _adjustment.Visibility = Visibility.Collapsed;
            content.Children.Add(_adjustment);
            _adjustment.ToolTip = "例如“短一点”“不要提价格”。Enter 提交，Shift+Enter 换行";
            content.Children.Add(_conversationHistory);
            reply.Children.Add(Label("可发送的回复 · 可编辑"));
            reply.Children.Add(_staleNotice);
            reply.Children.Add(_editNotice);
            reply.Children.Add(_emptyResult);
            _result.Height = 235;
            _result.FontSize = 16;
            _result.Visibility = Visibility.Collapsed;
            reply.Children.Add(_result);
            var replyActions = new WrapPanel
            {
                Margin = new Thickness(0, 7, 0, 0)
            };
            replyActions.Children.Add(_copy);
            replyActions.Children.Add(Action("继续调整", delegate
            {
                _showingReply = false;
                UpdateWorkbenchLayout();
                _adjustment.Focus();
            }));
            reply.Children.Add(replyActions);
            reply.Children.Add(Label("中文意思"));
            _meaning.IsReadOnly = true;
            reply.Children.Add(_meaning);
            reply.Children.Add(Label("沟通建议 / 待确认事项"));
            _advice.IsReadOnly = true;
            reply.Children.Add(_advice);
            _calculationLabel = Label("计算明细 · 请核对输入与单位");
            _calculationLabel.Visibility = Visibility.Collapsed;
            reply.Children.Add(_calculationLabel);
            _calculation.IsReadOnly = true;
            _calculation.Visibility = Visibility.Collapsed;
            reply.Children.Add(_calculation);
            _calculationActions = new WrapPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 5, 0, 0)
            };
            _calculationActions.Children.Add(Action("复制计算明细", delegate
            {
                try
                {
                    Clipboard.SetText(_calculation.Text);
                    _status.Text = "已复制计算明细。";
                }
                catch { _status.Text = "剪贴板正忙，请重试。"; }
            }));
            reply.Children.Add(_calculationActions);
            _sourcesLabel = Label("资料来源 · 请核对日期与适用条件");
            _sourcesLabel.Visibility = Visibility.Collapsed;
            reply.Children.Add(_sourcesLabel);
            _sources.IsReadOnly = true;
            _sources.Visibility = Visibility.Collapsed;
            reply.Children.Add(_sources);
            _sourceActions = new WrapPanel
            {
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 5, 0, 0)
            };
            _sourceActions.Children.Add(Action("复制来源", delegate
            {
                try
                {
                    Clipboard.SetText(_sources.Text);
                    _status.Text = "已复制资料来源。";
                }
                catch { _status.Text = "剪贴板正忙，请重试。"; }
            }));
            reply.Children.Add(_sourceActions);

            SizeChanged += delegate { UpdateWorkbenchLayout(); };
            UpdateWorkbenchLayout();

            _background.TextChanged += delegate { OnMaterialChanged(); };
            _intent.TextChanged += delegate { OnMaterialChanged(); };
            _adjustment.TextChanged += delegate
                { if (!_loading && _request != null) CancelRequest(); };
            _result.TextChanged += delegate
            {
                _copy.IsEnabled = !string.IsNullOrWhiteSpace(_result.Text);
                _emptyResult.Visibility = _copy.IsEnabled
                    ? Visibility.Collapsed : Visibility.Visible;
                _result.Visibility = _copy.IsEnabled
                    ? Visibility.Visible : Visibility.Collapsed;
                if (!_loading && _lastResult != null)
                    _editNotice.Visibility = Visibility.Visible;
                if (!_loading && _request != null) CancelRequest();
            };
            PreviewKeyDown += HandleKeyDown;
            TextCompositionManager.AddPreviewTextInputStartHandler(this,
                delegate { _composing = true; });
            TextCompositionManager.AddTextInputHandler(this,
                delegate { _composing = false; });
            Loaded += delegate { _intent.Focus(); };
            Closed += delegate { CancelRequest(); };
            Closing += delegate(object sender,
                System.ComponentModel.CancelEventArgs args)
            {
                if (_allowClose || System.Windows.Application.Current == null ||
                    System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
                    return;
                args.Cancel = true;
                CancelRequest();
                Hide();
            };
            _loading = false;
        }

        internal void CloseForExit()
        {
            _allowClose = true;
            Close();
        }

        private void FitToWorkArea()
        {
            System.Drawing.Point pointer =
                System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle area =
                System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
            uint monitorDpi = NativeMethods.GetMonitorDpi(
                pointer.X, pointer.Y);
            double scale = monitorDpi == 0 ? 1 : monitorDpi / 96.0;
            double maxWidth = Math.Max(360, area.Width / scale - 24);
            double maxHeight = Math.Max(360, area.Height / scale - 24);
            MinWidth = Math.Min(540, maxWidth);
            MinHeight = Math.Min(380, maxHeight);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            Width = Math.Min(960, maxWidth);
            Height = Math.Min(690, maxHeight);
        }

        private void PositionInitialWorkbench()
        {
            System.Drawing.Point pointer =
                System.Windows.Forms.Cursor.Position;
            System.Drawing.Rectangle area =
                System.Windows.Forms.Screen.FromPoint(pointer).WorkingArea;
            uint monitorDpi = NativeMethods.GetMonitorDpi(
                pointer.X, pointer.Y);
            double scale = monitorDpi == 0 ? 1 : monitorDpi / 96.0;
            int x = area.Left + (int)((area.Width - Width * scale) / 2);
            int y = area.Top + (int)((area.Height - Height * scale) / 2);
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(this)
                .Handle;
            NativeMethods.SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0,
                0x0001 | NativeMethods.SWP_NOACTIVATE |
                NativeMethods.SWP_NOZORDER);
        }

        private void UpdateWorkbenchLayout()
        {
            if (_workGrid == null || _leftPane == null ||
                _rightPane == null || _narrowTabs == null) return;
            bool narrow = ActualWidth > 0 ? ActualWidth < 820 : Width < 820;
            _narrowTabs.Visibility = narrow
                ? Visibility.Visible : Visibility.Collapsed;
            ColumnDefinitionCollection columns = _workGrid.ColumnDefinitions;
            if (narrow)
            {
                columns[0].Width = new GridLength(_showingReply ? 0 : 1,
                    GridUnitType.Star);
                columns[1].Width = new GridLength(0);
                columns[2].Width = new GridLength(_showingReply ? 1 : 0,
                    GridUnitType.Star);
                _leftPane.Visibility = _showingReply
                    ? Visibility.Collapsed : Visibility.Visible;
                _rightPane.Visibility = _showingReply
                    ? Visibility.Visible : Visibility.Collapsed;
            }
            else
            {
                columns[0].Width = new GridLength(42, GridUnitType.Star);
                columns[1].Width = new GridLength(7);
                columns[2].Width = new GridLength(58, GridUnitType.Star);
                _leftPane.Visibility = _rightPane.Visibility =
                    Visibility.Visible;
            }
        }

        internal void RefreshModelSummary()
        {
            ModelConnectionSettings current =
                _settings.GetModelConnection(_settings.ModelVendor);
            _modelSummary.Text = "当前 AI · " + _settings.ModelVendor +
                " · " + current.Model;
        }

        private static TextBox Editor(double height)
        {
            var box = new TextBox
            {
                Height = height, MinHeight = 40, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(11), FontSize = 14,
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(30, 58, 70)),
                BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(208, 226, 232)),
                BorderThickness = new Thickness(1)
            };
            var template = new ControlTemplate(typeof(TextBox));
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty,
                new CornerRadius(9));
            border.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            border.AppendChild(host);
            template.VisualTree = border;
            box.Template = template;
            return box;
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
                { Text = text, Margin = new Thickness(0, 8, 0, 4), FontWeight = FontWeights.SemiBold };
        }

        internal static Button Action(string label, Action action)
        {
            var button = new Button
                { Content = label, Padding = new Thickness(12, 7, 12, 7),
                  Margin = new Thickness(0, 2, 6, 2), MinHeight = 34,
                  FontSize = 12.5 };
            StyleAction(button, false);
            button.Click += delegate { action(); };
            return button;
        }

        private static void StyleAction(Button button, bool primary)
        {
            button.Background = new SolidColorBrush(primary
                ? System.Windows.Media.Color.FromRgb(9, 119, 151)
                : System.Windows.Media.Color.FromRgb(245, 250, 251));
            button.Foreground = new SolidColorBrush(primary
                ? System.Windows.Media.Colors.White
                : System.Windows.Media.Color.FromRgb(27, 73, 91));
            button.BorderBrush = new SolidColorBrush(primary
                ? System.Windows.Media.Color.FromRgb(9, 119, 151)
                : System.Windows.Media.Color.FromRgb(208, 226, 232));
            button.BorderThickness = new Thickness(1);
            var template = new ControlTemplate(typeof(Button));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "ActionBorder";
            border.SetValue(Border.CornerRadiusProperty,
                new CornerRadius(8));
            border.SetBinding(Border.BackgroundProperty,
                new System.Windows.Data.Binding("Background")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty,
                new System.Windows.Data.Binding("BorderBrush")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty,
                new System.Windows.Data.Binding("BorderThickness")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            var presenter = new FrameworkElementFactory(
                typeof(ContentPresenter));
            presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty,
                HorizontalAlignment.Center);
            presenter.SetValue(ContentPresenter.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            presenter.SetBinding(ContentPresenter.MarginProperty,
                new System.Windows.Data.Binding("Padding")
                { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger
            {
                Property = Button.IsMouseOverProperty, Value = true
            };
            hover.Setters.Add(new Setter(Button.BackgroundProperty,
                new SolidColorBrush(primary
                    ? System.Windows.Media.Color.FromRgb(7, 101, 130)
                    : System.Windows.Media.Color.FromRgb(232, 245, 248))));
            template.Triggers.Add(hover);
            var disabled = new Trigger
            {
                Property = Button.IsEnabledProperty, Value = false
            };
            disabled.Setters.Add(new Setter(Button.OpacityProperty, 0.45));
            template.Triggers.Add(disabled);
            button.Template = template;
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                if (_composing) { _composing = false; return; }
                if (_request != null) CancelRequest();
                else Hide();
                e.Handled = true;
                return;
            }
            if (e.Key != Key.Enter || e.IsRepeat || _composing) return;
            if (e.Key == Key.ImeProcessed || e.ImeProcessedKey == Key.Enter)
                return;
            ModifierKeys modifiers = Keyboard.Modifiers;
            bool control = modifiers == ModifierKeys.Control;
            bool plainIntent = modifiers == ModifierKeys.None &&
                ReferenceEquals(Keyboard.FocusedElement, _intent);
            bool plainAdjustment = modifiers == ModifierKeys.None &&
                ReferenceEquals(Keyboard.FocusedElement, _adjustment);
            if (!control && !plainIntent && !plainAdjustment) return;
            if (_request != null) { e.Handled = true; return; }
            e.Handled = true;
            if (plainAdjustment && string.IsNullOrWhiteSpace(_adjustment.Text))
                return;
            if (plainAdjustment && _lastResult == null)
            {
                _status.Text = "请先生成一份回复，再填写调整要求。";
                return;
            }
            var ignored = Generate(false);
        }

        private void OnMaterialChanged()
        {
            if (_loading) return;
            if (_request != null) CancelRequest();
            if (_turns.Count > 0)
            {
                _turns.Clear();
                _successful = 0;
                _lastResult = null;
                _status.Text = "背景或需求已改变，请重新生成。已有回复仍可复制。";
                _staleNotice.Visibility = Visibility.Visible;
                _conversationHistory.Children.Clear();
                _conversationHistory.Visibility = Visibility.Collapsed;
            }
        }

        private async Task Generate(bool adviceOnly)
        {
            if (_request != null) return;
            if (string.IsNullOrWhiteSpace(_background.Text) &&
                string.IsNullOrWhiteSpace(_intent.Text) && _images.Count == 0)
            {
                _status.Text = "请填写想法、客户消息或添加截图。";
                return;
            }
            if (!string.IsNullOrWhiteSpace(_adjustment.Text) && _lastResult == null)
            {
                _status.Text = "请先生成一份回复，再填写调整要求。";
                return;
            }
            var turns = _turns.Skip(Math.Max(0, _turns.Count - 10))
                .Select(t => new CommunicationTurn
                { Instruction = t.Instruction, Reply = t.Reply }).ToArray();
            if (turns.Length > 0 && _lastResult != null)
                turns[turns.Length - 1].Reply = _result.Text;
            var input = new CommunicationRequest
            {
                Background = _background.Text,
                Intent = _intent.Text,
                Adjustment = _adjustment.Text,
                Language = Codes[_language.SelectedIndex],
                AdviceOnly = adviceOnly,
                Images = _images.ToArray(),
                Turns = turns,
                ApproveSensitiveSearch = query => MessageBox.Show(this,
                    "搜索词可能包含客户或订单信息：\n\n" + query +
                    "\n\n是否允许发送到搜索服务？",
                    "确认联网搜索", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) == MessageBoxResult.Yes
            };
            var pending = new CancellationTokenSource();
            _request = pending;
            _generate.IsEnabled = false;
            _adviceOnly.IsEnabled = false;
            _stop.Visibility = Visibility.Visible;
            _status.Text = "正在分析沟通内容…";
            try
            {
                _settings.CommunicationLanguage = input.Language;
                _settings.Save();
                CommunicationResult answer = await _client.ComposeCommunicationAsync(
                    input, _settings, pending.Token);
                if (_request != pending || pending.IsCancellationRequested) return;
                _loading = true;
                if (!adviceOnly && !answer.ToolLimitReached)
                {
                    _lastResult = answer;
                    _result.Text = answer.Reply;
                    _meaning.Text = answer.MeaningZh;
                    _staleNotice.Visibility = Visibility.Collapsed;
                    _editNotice.Visibility = Visibility.Collapsed;
                    _adjustmentLabel.Visibility = Visibility.Visible;
                    _adjustment.Visibility = Visibility.Visible;
                    if (Width < 820)
                    {
                        _showingReply = true;
                        UpdateWorkbenchLayout();
                    }
                }
                _advice.Text = answer.AdviceZh;
                if ((!adviceOnly && !answer.ToolLimitReached) ||
                    !string.IsNullOrWhiteSpace(answer.CalculationDetails))
                    _calculation.Text = answer.CalculationDetails;
                if ((!adviceOnly && !answer.ToolLimitReached) ||
                    !string.IsNullOrWhiteSpace(answer.Sources))
                    _sources.Text = answer.Sources;
                _calculationLabel.Visibility = _calculation.Visibility =
                    string.IsNullOrWhiteSpace(_calculation.Text)
                        ? Visibility.Collapsed : Visibility.Visible;
                _calculationActions.Visibility = _calculation.Visibility;
                _sourcesLabel.Visibility = _sources.Visibility =
                    string.IsNullOrWhiteSpace(_sources.Text)
                        ? Visibility.Collapsed : Visibility.Visible;
                _sourceActions.Visibility = _sources.Visibility;
                _loading = false;
                if (!answer.ToolLimitReached)
                {
                    _turns.Add(new CommunicationTurn
                        { Instruction = input.Adjustment,
                          Reply = adviceOnly ? _result.Text : answer.Reply });
                    _successful++;
                }
                AddHistoryTurn(input, answer);
                _loading = true;
                _adjustment.Clear();
                _loading = false;
                _status.Text = answer.ToolLimitReached
                    ? "本次工具已达上限；现有回复和计算明细已保留。"
                    : _turns.Count > 10
                    ? "已完成；继续调整仅参考最近 10 次结果。"
                    : adviceOnly ? "建议已更新；原回复已保留。" :
                    answer.Reply.Length > 0
                    ? "已生成，可核对后复制回复。"
                    : "信息不足，已列出建议和待确认事项。";
            }
            catch (OperationCanceledException)
            { if (_request == pending) _status.Text = "已取消。"; }
            catch (UnsupportedCommunicationImageException error)
            {
                if (_request == pending) _status.Text = error.Message;
            }
            catch (Exception error)
            {
                if (_request == pending)
                    _status.Text = error.Message + " 现有成功回复已保留，可重试。";
            }
            finally
            {
                bool active = _request == pending;
                if (active) _request = null;
                pending.Dispose();
                if (active)
                {
                    _generate.IsEnabled = true;
                    _adviceOnly.IsEnabled = true;
                    _stop.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void CancelRequest()
        {
            if (_request == null) return;
            _request.Cancel();
            _request = null;
            _status.Text = "已取消。";
            _generate.IsEnabled = true;
            _adviceOnly.IsEnabled = true;
            _stop.Visibility = Visibility.Collapsed;
        }

        private void CopyReply()
        {
            if (string.IsNullOrWhiteSpace(_result.Text)) return;
            try
            {
                Clipboard.SetText(_result.Text);
                _status.Text = "已复制回复。";
            }
            catch { _status.Text = "剪贴板正忙，请重试。"; }
        }

        private void NewConversation()
        {
            if ((!string.IsNullOrWhiteSpace(_background.Text) ||
                 !string.IsNullOrWhiteSpace(_intent.Text) ||
                 !string.IsNullOrWhiteSpace(_result.Text) ||
                 _images.Count > 0) &&
                MessageBox.Show(this, "清空本次沟通的材料和结果？",
                    "新建沟通", MessageBoxButton.YesNo,
                    MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;
            CancelRequest();
            _loading = true;
            _background.Clear(); _intent.Clear(); _adjustment.Clear();
            _result.Clear(); _meaning.Clear(); _advice.Clear();
            _calculation.Clear(); _sources.Clear();
            _calculationLabel.Visibility = _calculation.Visibility =
                Visibility.Collapsed;
            _calculationActions.Visibility = Visibility.Collapsed;
            _sourcesLabel.Visibility = _sources.Visibility =
                Visibility.Collapsed;
            _sourceActions.Visibility = Visibility.Collapsed;
            _staleNotice.Visibility = Visibility.Collapsed;
            _editNotice.Visibility = Visibility.Collapsed;
            _adjustmentLabel.Visibility = Visibility.Collapsed;
            _adjustment.Visibility = Visibility.Collapsed;
            _images.Clear(); _turns.Clear();
            _conversationHistory.Children.Clear();
            _conversationHistory.Visibility = Visibility.Collapsed;
            _lastResult = null; _successful = 0;
            RefreshImages();
            _status.Text = "已新建沟通。";
            _loading = false;
            _intent.Focus();
        }

        private void AddHistoryTurn(CommunicationRequest input,
            CommunicationResult answer)
        {
            string question = string.IsNullOrWhiteSpace(input.Adjustment)
                ? input.Intent : input.Adjustment;
            if (string.IsNullOrWhiteSpace(question))
                question = input.Images.Length > 0
                    ? "请分析这次的截图和客户材料" :
                    "请分析客户材料";
            _conversationHistory.Visibility = Visibility.Visible;
            if (_conversationHistory.Children.Count == 0)
                _conversationHistory.Children.Add(Label("本次对话"));
            _conversationHistory.Children.Add(HistoryBubble(
                "你 · " + question, true));
            string preview = input.AdviceOnly ||
                string.IsNullOrWhiteSpace(answer.Reply)
                ? answer.AdviceZh : answer.Reply;
            if (preview.Length > 220)
                preview = preview.Substring(0, 220) + "…";
            _conversationHistory.Children.Add(HistoryBubble(
                "助手 · " + preview, false));
            while (_conversationHistory.Children.Count > 21)
            {
                _conversationHistory.Children.RemoveAt(1);
                _conversationHistory.Children.RemoveAt(1);
            }
        }

        private static Border HistoryBubble(string message, bool user)
        {
            return new Border
            {
                Background = new SolidColorBrush(user
                    ? System.Windows.Media.Color.FromRgb(232, 246, 250)
                    : System.Windows.Media.Color.FromRgb(244, 248, 249)),
                CornerRadius = new CornerRadius(9),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(user ? 24 : 0, 4,
                    user ? 0 : 24, 0),
                Child = new TextBlock
                {
                    Text = message, TextWrapping = TextWrapping.Wrap,
                    FontSize = 12.5,
                    Foreground = new SolidColorBrush(
                        System.Windows.Media.Color.FromRgb(40, 73, 87))
                }
            };
        }

        private async Task CaptureImage()
        {
            if (_images.Count >= 5) { _status.Text = "每次沟通最多添加 5 张图片。"; return; }
            if (IsCapturing) return;
            IsCapturing = true;
            Visibility = Visibility.Hidden;
            try
            {
                await Task.Delay(130);
                using (var selector = new ScreenshotSelector())
                {
                    if (selector.ShowDialog() != System.Windows.Forms.DialogResult.OK)
                        return;
                    using (Bitmap bitmap = selector.SelectedBitmap)
                        AddBitmap(bitmap);
                }
            }
            catch (Exception error) { _status.Text = "截图失败：" + error.Message; }
            finally
            {
                IsCapturing = false;
                Visibility = Visibility.Visible;
                Activate();
            }
        }

        private void PasteImage()
        {
            try
            {
                if (!Clipboard.ContainsImage())
                { _status.Text = "剪贴板中没有图片。"; return; }
                BitmapSource source = Clipboard.GetImage();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(source));
                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream);
                    AddImage(stream.ToArray());
                }
            }
            catch (Exception error) { _status.Text = "粘贴图片失败：" + error.Message; }
        }

        private void AddFiles()
        {
            var dialog = new OpenFileDialog
            {
                Title = "选择聊天截图",
                Filter = "图片|*.png;*.jpg;*.jpeg",
                Multiselect = true
            };
            if (dialog.ShowDialog(this) != true) return;
            foreach (string path in dialog.FileNames)
            {
                if (_images.Count >= 5)
                { _status.Text = "每次沟通最多添加 5 张图片。"; break; }
                try { using (var bitmap = new Bitmap(path)) AddBitmap(bitmap); }
                catch (Exception error) { _status.Text = "添加图片失败：" + error.Message; }
            }
        }

        private void AddBitmap(Bitmap bitmap)
        {
            using (var stream = new MemoryStream())
            {
                bitmap.Save(stream, ImageFormat.Png);
                AddImage(stream.ToArray());
            }
        }

        private void AddImage(byte[] bytes)
        {
            if (_images.Count >= 5)
            { _status.Text = "每次沟通最多添加 5 张图片。"; return; }
            if (bytes == null || bytes.Length == 0 || bytes.Length > 10 * 1024 * 1024)
            { _status.Text = "单张图片需小于 10 MB。"; return; }
            _images.Add(bytes);
            OnMaterialChanged();
            RefreshImages();
            _status.Text = "已添加 " + _images.Count + " 张图片。";
        }

        private static BitmapImage PreviewSource(byte[] bytes,
            int decodeWidth = 180)
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            if (decodeWidth > 0) bitmap.DecodePixelWidth = decodeWidth;
            bitmap.StreamSource = new MemoryStream(bytes);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }

        private void RefreshImages()
        {
            _imageList.Children.Clear();
            for (int i = 0; i < _images.Count; i++)
            {
                int index = i;
                var holder = new StackPanel
                    { Margin = new Thickness(0, 0, 9, 8), Width = 170 };
                holder.Children.Add(new TextBlock
                    { Text = "截图 " + (index + 1), FontWeight = FontWeights.SemiBold });
                var preview = new Image
                    { Source = PreviewSource(_images[index]),
                      Width = 160, Height = 92, Stretch = Stretch.Uniform };
                preview.MouseLeftButtonUp += delegate { ShowImage(index); };
                holder.Children.Add(preview);
                var controls = new WrapPanel();
                controls.Children.Add(Action("↑", delegate { MoveImage(index, -1); }));
                controls.Children.Add(Action("↓", delegate { MoveImage(index, 1); }));
                controls.Children.Add(Action("删除", delegate
                    { _images.RemoveAt(index); OnMaterialChanged(); RefreshImages(); }));
                holder.Children.Add(controls);
                _imageList.Children.Add(holder);
            }
        }

        private void MoveImage(int index, int delta)
        {
            int target = index + delta;
            if (target < 0 || target >= _images.Count) return;
            byte[] value = _images[index];
            _images[index] = _images[target];
            _images[target] = value;
            OnMaterialChanged();
            RefreshImages();
        }

        private void ShowImage(int index)
        {
            var window = new Window
            {
                Title = "截图 " + (index + 1), Owner = this,
                Width = Math.Min(900, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(700, SystemParameters.WorkArea.Height - 40),
                Content = new ScrollViewer { Content = new Image
                    { Source = PreviewSource(_images[index], 0), Stretch = Stretch.Uniform } }
            };
            window.ShowDialog();
        }

    }

    internal sealed class HistoryWindow : Window
    {
        public HistoryWindow(AppSettings settings, TranslationClient client)
        {
            Title = "鲨译 · 最近翻译（本次会话，最多100条）";
            Width = 650; Height = 480; MinWidth = 420; MinHeight = 300;
            var root = new DockPanel { Margin = new Thickness(14) };
            Content = root;
            var search = new TextBox
                { Margin = new Thickness(0, 0, 0, 8), ToolTip = "搜索原文或译文" };
            DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
            var list = new ListBox();
            Action refresh = delegate
                { list.ItemsSource = TranslationHistory.Search(search.Text); };
            var actions = new WrapPanel();
            DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
            Action open = delegate
            {
                var entry = list.SelectedItem as HistoryEntry;
                if (entry != null)
                    new HistoryDetailWindow(settings, client, entry).Show();
            };
            actions.Children.Add(WritingWindow.Action("查看 / 复制 / 再次翻译", open));
            actions.Children.Add(WritingWindow.Action("清空记录", delegate
                { TranslationHistory.Clear(); refresh(); }));
            root.Children.Add(list);
            search.TextChanged += delegate { refresh(); };
            list.MouseDoubleClick += delegate { open(); };
            Activated += delegate { refresh(); };
            refresh();
        }
    }

    internal sealed class HistoryDetailWindow : Window
    {
        public HistoryDetailWindow(AppSettings settings, TranslationClient client,
            HistoryEntry entry)
        {
            Title = "鲨译 · 翻译记录";
            Width = 650; Height = 520;
            var root = new DockPanel { Margin = new Thickness(14) };
            Content = root;
            var footer = new WrapPanel();
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var status = new TextBlock { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(status, Dock.Bottom); root.Children.Add(status);
            var stack = new StackPanel();
            var source = new TextBox
                { Text = entry.Source, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                  VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 140 };
            var result = new TextBox
                { Text = entry.Translation, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                  VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Height = 140 };
            stack.Children.Add(new TextBlock { Text = "原文" });
            stack.Children.Add(source);
            stack.Children.Add(new TextBlock { Text = "译文", Margin = new Thickness(0, 10, 0, 0) });
            stack.Children.Add(result);
            root.Children.Add(stack);
            footer.Children.Add(WritingWindow.Action("复制译文", delegate
            {
                try { Clipboard.SetText(result.Text); }
                catch { status.Text = "剪贴板正忙，请重试。"; }
            }));
            footer.Children.Add(WritingWindow.Action("重新翻译", async delegate
            {
                var snapshot = new AppSettings();
                foreach (var field in typeof(AppSettings).GetFields(
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public))
                    field.SetValue(snapshot, field.GetValue(settings));
                snapshot.TargetLanguageMode = "Fixed";
                snapshot.TargetLanguage = entry.Target;
                status.Text = "正在翻译…";
                try
                {
                    TranslationResult translated = await client.TranslateWithRequirementsAsync(
                        source.Text, snapshot, CancellationToken.None, null,
                        entry.Requirements, entry.Rewrite);
                    result.Text = translated.Text;
                    status.Text = "翻译完成。";
                }
                catch (Exception error) { status.Text = error.Message; }
            }));
        }
    }
}
