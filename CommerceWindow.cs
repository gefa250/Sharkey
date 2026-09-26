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
    internal sealed partial class WritingWindow : Window
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
            { Width = 240, MinHeight = 34, IsEditable = true,
              IsTextSearchEnabled = true, Margin = new Thickness(0, 0, 8, 0),
              ToolTip = "选择常用语言，也可输入任意语言或地区变体，例如：泰语、巴西葡萄牙语" };
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
        private SavedConversation _session = new SavedConversation();
        private readonly System.Windows.Threading.DispatcherTimer _saveTimer =
            new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        internal bool IsCapturing { get; private set; }
        private Window _capturePrompt;
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
            headerActions.Children.Add(Action("会话记录", ShowConversations));
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
            _language.Style = SettingsWindow.CreateComboBoxStyle();
            languageRow.Children.Add(new TextBlock
                { Text = "回复语言", Margin = new Thickness(0, 5, 8, 0) });
            foreach (string name in new[] { "自动（跟随客户，否则英语）", "英语", "日语",
                "韩语", "德语", "法语", "西班牙语", "俄语", "阿拉伯语", "葡萄牙语", "简体中文" })
                _language.Items.Add(name);
            int saved = Array.IndexOf(Codes, settings.CommunicationLanguage);
            _language.SelectedIndex = saved < 0 ? 0 : saved;
            if (saved < 0 && !string.IsNullOrWhiteSpace(settings.CommunicationLanguage))
                _language.Text = settings.CommunicationLanguage;
            _language.SelectionChanged += delegate { OnMaterialChanged(); };
            _language.AddHandler(TextBox.TextChangedEvent, new TextChangedEventHandler(
                delegate { OnMaterialChanged(); ScheduleSave(); }));
            languageRow.Children.Add(_language);
            content.Children.Add(languageRow);

            content.Children.Add(Label("客户消息 / 背景（可选）"));
            content.Children.Add(_background);
            _background.ToolTip = "粘贴客户聊天内容、邮件或其他背景文字";
            content.Children.Add(Label("我的想法 / 要求"));
            content.Children.Add(_intent);
            foreach (TextBox editor in new[] { _background, _intent, _adjustment })
            {
                DataObject.AddPastingHandler(editor, delegate(object sender, DataObjectPastingEventArgs args)
                {
                    if (!ClipboardImages.HasImage(args.DataObject)) return;
                    args.CancelCommand();
                    PasteImageData(args.DataObject);
                });
                editor.AllowDrop = true;
                editor.PreviewDragOver += delegate(object sender, DragEventArgs args)
                {
                    if (!ClipboardImages.HasImage(args.Data)) return;
                    args.Effects = DragDropEffects.Copy; args.Handled = true;
                };
                editor.PreviewDrop += delegate(object sender, DragEventArgs args)
                {
                    if (!ClipboardImages.HasImage(args.Data)) return;
                    args.Handled = true; PasteImageData(args.Data);
                };
            }
            _intent.ToolTip = "写出零散要点即可。Enter 生成，Shift+Enter 换行";
            var imageActions = new WrapPanel { Margin = new Thickness(0, 7, 0, 4) };
            var imageMenu = new ContextMenu();
            var captureItem = new MenuItem { Header = "框选截图" };
            captureItem.Click += async delegate { await PrepareCapture(); };
            var pasteItem = new MenuItem { Header = "从剪贴板粘贴" };
            pasteItem.Click += delegate { PasteImage(); };
            var fileItem = new MenuItem { Header = "选择图片文件…" };
            fileItem.Click += delegate { AddFiles(); };
            imageMenu.Items.Add(captureItem); imageMenu.Items.Add(pasteItem); imageMenu.Items.Add(fileItem);
            var attachmentButton = Action("添加图片", delegate { imageMenu.IsOpen = true; });
            imageMenu.PlacementTarget = attachmentButton;
            imageActions.Children.Add(attachmentButton);
            imageActions.Children.Add(new TextBlock { Text = "输入区 Ctrl+V 粘贴 · 拖入图片", FontSize = 12,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Brushes.SlateGray });
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
                if (!SaveConversation()) { args.Cancel = true; return; }
                if (_allowClose || System.Windows.Application.Current == null ||
                    System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
                    return;
                args.Cancel = true;
                CancelRequest();
                Hide();
            };
            _loading = false;
            _saveTimer.Tick += delegate { _saveTimer.Stop(); SaveConversation(); };
            foreach (TextBox editor in SessionEditors())
                editor.TextChanged += delegate { ScheduleSave(); };
            IsVisibleChanged += delegate { if (!IsVisible) SaveConversation(); };
            Closed += delegate { _saveTimer.Stop(); SaveConversation(); };
            foreach (string path in ConversationStore.Files())
            {
                try { RestoreConversation(ConversationStore.Load(path)); break; }
                catch { _status.Text = "部分历史记录无法读取，原文件已保留。"; }
            }
            BuildChatInterface();
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
            { _status.Text = "输入已修改，发送后生成新回复。"; }
        }

        private async Task Generate(bool adviceOnly)
        {
            if (_request != null) return;
            if (string.IsNullOrWhiteSpace(_background.Text) &&
                string.IsNullOrWhiteSpace(_intent.Text) && _images.Count == 0 && _documents.Count == 0)
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
                Background = CombinedCustomerMaterials(),
                Intent = _chatLayout && _turns.Count > 0 ? "" : _intent.Text,
                Adjustment = _chatLayout && _turns.Count > 0 ? _intent.Text : _adjustment.Text,
                Language = SelectedLanguage(),
                TaskMode = _chatTaskMode == "inquiry" || _intent.Text.Contains("整理询盘") ? "inquiry" : "",
                UnifiedInput = _chatLayout,
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
                    if (_chatLayout && _lastResult != null)
                        _conversationHistory.Children.Add(HistoryBubble("Sharkey · " + _result.Text, false));
                    _lastResult = answer;
                    if (_chatLayout) RefreshInquiry();
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
                        { Instruction = string.IsNullOrWhiteSpace(input.Adjustment) ? input.Intent : input.Adjustment,
                          Reply = adviceOnly ? _result.Text : answer.Reply,
                          Meaning = answer.MeaningZh, Advice = answer.AdviceZh,
                          Calculation = answer.CalculationDetails, Sources = answer.Sources });
                    _successful++;
                }
                AddHistoryTurn(input, answer);
                _loading = true;
                _adjustment.Clear();
                if (_chatLayout) _intent.Clear();
                if (_chatLayout) _chatTaskMode = "";
                _loading = false;
                ScheduleSave();
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

        private string SelectedLanguage()
        {
            int index = _language.SelectedIndex;
            if (index >= 0 && index < Codes.Length &&
                _language.Text == Convert.ToString(_language.Items[index])) return Codes[index];
            return string.IsNullOrWhiteSpace(_language.Text) ? "auto" : _language.Text.Trim();
        }

        private TextBox[] SessionEditors()
        { return new[] { _background, _intent, _adjustment, _result, _meaning, _advice, _calculation, _sources }; }

        private void ScheduleSave()
        {
            if (_loading) return;
            _saveTimer.Stop(); _saveTimer.Start();
        }

        private bool SaveConversation()
        {
            if (_loading) return true;
            _saveTimer.Stop();
            string[] texts = SessionEditors().Select(editor => editor.Text).ToArray();
            if (texts.All(string.IsNullOrWhiteSpace) && _images.Count == 0 && _turns.Count == 0 && _documents.Count == 0 &&
                _session.Texts.Length == 0) return true;
            _session.Texts = texts; _session.Images = _images.ToArray();
            _session.Turns = _turns.ToArray(); _session.Language = SelectedLanguage();
            _session.Documents = _documents.ToArray();
            _session.InquiryFields = _lastResult == null ? new InquiryField[0] : _lastResult.InquiryFields;
            _session.MissingFields = _lastResult == null ? new string[0] : _lastResult.MissingFields;
            _session.Stale = _staleNotice.Visibility == Visibility.Visible;
            _session.Edited = _editNotice.Visibility == Visibility.Visible;
            bool newTitle = _session.Title == "新沟通";
            if (newTitle)
            {
                string title = string.IsNullOrWhiteSpace(_intent.Text) ? _background.Text : _intent.Text;
                if (string.IsNullOrWhiteSpace(title) && _turns.Count > 0)
                    title = _turns[0].Instruction;
                if (string.IsNullOrWhiteSpace(title) && _documents.Count > 0)
                    title = _documents[0].Name;
                title = title.Replace('\r', ' ').Replace('\n', ' ').Trim();
                _session.Title = title.Length == 0 ? "图片沟通" : title.Substring(0, Math.Min(36, title.Length));
            }
            try { ConversationStore.Save(_session); if (_chatLayout && newTitle) RefreshSessionList(); return true; }
            catch { _status.Text = "会话保存失败，请检查磁盘空间和目录权限；当前内容仍保留，请勿退出。"; return false; }
        }

        private void RestoreConversation(SavedConversation value)
        {
            foreach (byte[] bytes in value.Images) PreviewSource(bytes);
            CancelRequest(); _saveTimer.Stop(); _loading = true;
            try
            {
                _chatTaskMode = "";
                _session = value;
                TextBox[] editors = SessionEditors();
                for (int i = 0; i < editors.Length; i++)
                    editors[i].Text = i < value.Texts.Length ? value.Texts[i] : "";
                int index = Array.IndexOf(Codes, value.Language);
                _language.SelectedIndex = index;
                if (index < 0) _language.Text = value.Language;
                _images.Clear(); _images.AddRange(value.Images);
                _documents.Clear(); _documents.AddRange(value.Documents ?? new CommerceDocument[0]);
                foreach (CommerceDocument document in _documents)
                    if (string.IsNullOrWhiteSpace(document.Text))
                        document.Status = "读取未完成，请重新添加";
                _turns.Clear(); _turns.AddRange(value.Turns);
                _successful = _turns.Count;
                _lastResult = string.IsNullOrWhiteSpace(_result.Text) ? null : new CommunicationResult
                { Reply = _result.Text, MeaningZh = _meaning.Text, AdviceZh = _advice.Text,
                  InquiryFields = value.InquiryFields ?? new InquiryField[0],
                  MissingFields = value.MissingFields ?? new string[0] };
                _adjustmentLabel.Visibility = _adjustment.Visibility = _lastResult == null ? Visibility.Collapsed : Visibility.Visible;
                _calculationLabel.Visibility = _calculation.Visibility = _calculationActions.Visibility =
                    string.IsNullOrWhiteSpace(_calculation.Text) ? Visibility.Collapsed : Visibility.Visible;
                _sourcesLabel.Visibility = _sources.Visibility = _sourceActions.Visibility =
                    string.IsNullOrWhiteSpace(_sources.Text) ? Visibility.Collapsed : Visibility.Visible;
                _staleNotice.Visibility = value.Stale ? Visibility.Visible : Visibility.Collapsed;
                _editNotice.Visibility = value.Edited ? Visibility.Visible : Visibility.Collapsed;
                _conversationHistory.Children.Clear();
                foreach (CommunicationTurn turn in _turns)
                {
                    _conversationHistory.Children.Add(HistoryBubble("你 · " + turn.Instruction, true));
                    _conversationHistory.Children.Add(HistoryBubble("助手 · " + turn.Reply, false));
                }
                _conversationHistory.Visibility = _turns.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
                if (_chatLayout) { RebuildChatHistory(); RefreshInquiry(); }
                RefreshImages(); RefreshComposerAttachments(); _status.Text = "已恢复：" + value.Title;
            }
            finally { _loading = false; }
        }

        private void ShowConversations()
        {
            if (!SaveConversation()) return;
            var dialog = new Window { Title = "会话记录 · 本机加密保存", Owner = this,
                Width = 440, Height = 480, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var panel = new DockPanel { Margin = new Thickness(16) };
            var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom);
            panel.Children.Add(actions);
            var list = new ListBox { Margin = new Thickness(0, 0, 0, 12) };
            int failed = 0;
            foreach (string path in ConversationStore.Files())
                try { list.Items.Add(ConversationStore.Load(path)); } catch { failed++; }
            if (failed > 0) _status.Text = failed + " 条记录无法读取，文件已保留。";
            System.Action open = delegate
            {
                var value = list.SelectedItem as SavedConversation;
                if (value == null) return;
                if (!SaveConversation()) return;
                RestoreConversation(value); dialog.Close();
            };
            actions.Children.Add(Action("打开", open));
            actions.Children.Add(Action("重命名", delegate
            {
                var value = list.SelectedItem as SavedConversation; if (value == null) return;
                var name = new TextBox { Text = value.Title, Margin = new Thickness(12), MaxLength = 80 };
                var rename = new Window { Owner = dialog, Title = "重命名 · Enter 保存", Width = 340,
                    Height = 110, Content = name, WindowStartupLocation = WindowStartupLocation.CenterOwner };
                name.KeyDown += delegate(object sender, KeyEventArgs args)
                {
                    if (args.Key != Key.Enter || string.IsNullOrWhiteSpace(name.Text)) return;
                    string original = value.Title; value.Title = name.Text.Trim();
                    try { ConversationStore.Save(value); if (_session.Id == value.Id) _session.Title = value.Title;
                        list.Items.Refresh(); rename.Close(); }
                    catch { value.Title = original; MessageBox.Show(rename, "重命名保存失败。"); }
                };
                rename.ShowDialog();
            }));
            actions.Children.Add(Action("删除", delegate
            {
                var value = list.SelectedItem as SavedConversation; if (value == null) return;
                if (MessageBox.Show(dialog, "删除此会话及图片？此操作无法撤销。", "删除会话",
                    MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                try
                {
                    ConversationStore.Delete(value.Id); list.Items.Remove(value);
                    if (_session.Id == value.Id)
                        RestoreConversation(new SavedConversation());
                }
                catch { MessageBox.Show(dialog, "删除失败，记录仍保留。"); }
            }));
            list.MouseDoubleClick += delegate { open(); };
            panel.Children.Add(list); dialog.Content = panel; dialog.ShowDialog();
        }

        private void NewConversation()
        {
            if (!SaveConversation()) return;
            _session = new SavedConversation();
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
            _chatTaskMode = "";
            _documents.Clear(); RefreshComposerAttachments();
            _conversationHistory.Children.Clear();
            _conversationHistory.Visibility = Visibility.Collapsed;
            _lastResult = null; _successful = 0;
            RefreshImages();
            _status.Text = "已新建沟通。";
            _loading = false;
            if (_chatLayout) { RefreshSessionList(); UpdateChatResult(); }
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
            if (_chatLayout) return;
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

        internal async void CaptureFromDesktop()
        {
            if (_capturePrompt != null) { _capturePrompt.DialogResult = true; return; }
            await CaptureImage();
        }

        private async Task PrepareCapture()
        {
            if (IsCapturing || _capturePrompt != null) return;
            if (_images.Count >= 5) { _status.Text = "每次沟通最多添加 5 张图片，请先删除不需要的附件。"; return; }
            IsCapturing = true;
            Hide();
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(new TextBlock { Text = "切换到要截图的应用，再开始框选。", Margin = new Thickness(0, 0, 0, 12) });
            var prompt = new Window { Title = "准备截图", Width = 340, SizeToContent = SizeToContent.Height,
                Topmost = true, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false, Content = panel };
            _capturePrompt = prompt;
            IntPtr captureHandle = IntPtr.Zero;
            prompt.SourceInitialized += delegate
            {
                captureHandle = new System.Windows.Interop.WindowInteropHelper(prompt).Handle;
                System.Windows.Interop.HwndSource.FromHwnd(captureHandle).AddHook(
                    delegate(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
                    {
                        if (message == NativeMethods.WM_HOTKEY && wParam.ToInt32() == 7)
                        { prompt.DialogResult = false; handled = true; }
                        return IntPtr.Zero;
                    });
                NativeHotKey.Register(captureHandle, 7, NativeMethods.MOD_NOREPEAT, NativeMethods.VK_ESCAPE);
            };
            prompt.Closed += delegate { if (captureHandle != IntPtr.Zero) NativeHotKey.Unregister(captureHandle, 7); };
            panel.Children.Add(Action("开始框选 · " + _settings.AssistantCaptureHotkey,
                delegate { prompt.DialogResult = true; }));
            panel.Children.Add(Action("取消", delegate { prompt.DialogResult = false; }));
            prompt.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
                { if (args.Key == Key.Escape) prompt.DialogResult = false; };
            bool capture = prompt.ShowDialog() == true;
            _capturePrompt = null;
            IsCapturing = false;
            if (capture) await CaptureImage();
            else { Show(); Activate(); }
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

        private async void PasteImage()
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try { PasteImageData(Clipboard.GetDataObject()); return; }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    if (attempt == 2) { _status.Text = "剪贴板正忙，请稍后重新粘贴。"; return; }
                }
                await Task.Delay(80);
            }
        }

        private void PasteImageData(IDataObject data)
        {
            try
            {
                var images = ClipboardImages.Read(data);
                if (images.Count == 0) { _status.Text = "没有可读取的图片，请重新复制截图。"; return; }
                foreach (byte[] bytes in images) AddImage(bytes);
            }
            catch (Exception) { _status.Text = "无法读取图片，请重新截图或通过添加图片选择文件。已有附件已保留。"; }
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
            PreviewSource(bytes); // Reject malformed images before changing the attachment list.
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
            ScheduleSave();
            _imageList.Children.Clear();
            if (_chatLayout)
            {
                for (int i = 0; i < _images.Count; i++)
                {
                    int index = i;
                    var tile = new WrapPanel { Margin = new Thickness(0, 0, 5, 5),
                        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(244, 249, 251)) };
                    var preview = new Image { Source = PreviewSource(_images[index]), Width = 54,
                        Height = 40, Stretch = Stretch.Uniform, Margin = new Thickness(3) };
                    preview.MouseLeftButtonUp += delegate { ShowImage(index); };
                    tile.Children.Add(preview);
                    tile.Children.Add(new TextBlock { Text = "截图 " + (index + 1), FontSize = 12,
                        VerticalAlignment = VerticalAlignment.Center });
                    tile.Children.Add(Action("×", delegate
                    { _images.RemoveAt(index); OnMaterialChanged(); RefreshImages(); }));
                    var menu = new ContextMenu();
                    var up = new MenuItem { Header = "向前移动" };
                    up.Click += delegate { MoveImage(index, -1); };
                    var down = new MenuItem { Header = "向后移动" };
                    down.Click += delegate { MoveImage(index, 1); };
                    menu.Items.Add(up); menu.Items.Add(down); tile.ContextMenu = menu;
                    _imageList.Children.Add(tile);
                }
                return;
            }
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
