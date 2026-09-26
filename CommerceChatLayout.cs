using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlobalTranslator
{
    internal sealed partial class WritingWindow
    {
        private ListBox _chatSessions;
        private TextBox _chatSearch;
        private TextBlock _chatTitle;
        private Border _chatSidebar;
        private Border _chatResultCard;
        private Expander _detailsExpander;
        private ScrollViewer _chatScroll;
        private StackPanel _chatFeed;
        private StackPanel _inquiryPanel;
        private string _chatTaskMode = "";
        private bool _chatLayout;
        private bool _sidebarOpen = true;
        private bool _wasNarrow;
        private bool _refreshingSessions;
        private RichTextBox _chatAnswer;
        private Button _editAnswer;
        private bool _editingAnswer;
        private string _renderedAnswer;
        private TextBlock _contextNotice;
        private int TopicStart { get { return (_session.TopicBreaks ?? new int[0]).LastOrDefault(); } }

        private static readonly Brush ChatInk = new SolidColorBrush(Color.FromRgb(25, 53, 70));
        private static readonly Brush ChatLine = new SolidColorBrush(Color.FromRgb(215, 229, 235));
        private static readonly Brush ChatMuted = new SolidColorBrush(Color.FromRgb(101, 123, 138));

        private static void Detach(FrameworkElement element)
        {
            var parent = element.Parent as Panel;
            if (parent != null) parent.Children.Remove(element);
        }

        private void BuildChatInterface()
        {
            _chatLayout = true;
            Background = Brushes.White;
            FitToWorkArea();
            Width = Math.Min(1120, Math.Max(Width, 1000));
            Height = Math.Min(760, Math.Max(Height, 700));
            MinWidth = 480; MinHeight = 500;

            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = shell;

            var sidebarBody = new DockPanel { Margin = new Thickness(12, 16, 12, 12) };
            _chatSidebar = new Border { Background = new SolidColorBrush(Color.FromRgb(235, 247, 252)),
                BorderBrush = ChatLine, BorderThickness = new Thickness(0, 0, 1, 0), Child = sidebarBody };
            shell.Children.Add(_chatSidebar);
            var sidebarBottom = new StackPanel();
            DockPanel.SetDock(sidebarBottom, Dock.Bottom); sidebarBody.Children.Add(sidebarBottom);
            sidebarBottom.Children.Add(Action("设置", delegate { if (_openModelSettings != null) _openModelSettings(); }));
            var sidebarTop = new StackPanel();
            DockPanel.SetDock(sidebarTop, Dock.Top); sidebarBody.Children.Add(sidebarTop);
            var brand = new WrapPanel { Margin = new Thickness(8, 0, 0, 16) };
            if (Application.Current != null)
                try { brand.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/Sharkey;component/assets/shark-logo.png")), Width = 30, Height = 30,
                    Margin = new Thickness(0, 0, 8, 0) }); }
                catch { /* Brand text remains usable if the resource fails to load. */ }
            brand.Children.Add(new TextBlock { Text = "Sharkey", FontSize = 19,
                FontWeight = FontWeights.SemiBold, Foreground = ChatInk,
                VerticalAlignment = VerticalAlignment.Center });
            sidebarTop.Children.Add(brand);
            var create = Action("＋  新建会话", NewConversation);
            StyleAction(create, true); create.HorizontalAlignment = HorizontalAlignment.Stretch;
            sidebarTop.Children.Add(create);
            _chatSearch = new TextBox { Height = 35, Margin = new Thickness(0, 11, 0, 12),
                ToolTip = "按会话标题搜索", FontSize = 13, Padding = new Thickness(9, 6, 9, 6) };
            _chatSearch.Style = SettingsWindow.CreateTextBoxStyle();
            _chatSearch.TextChanged += delegate { RefreshSessionList(); };
            var searchHost = new Grid();
            searchHost.Children.Add(_chatSearch);
            var searchHint = new TextBlock { Text = "搜索会话", Foreground = ChatMuted,
                FontSize = 13, IsHitTestVisible = false, Margin = new Thickness(11, 17, 0, 0) };
            searchHost.Children.Add(searchHint);
            _chatSearch.TextChanged += delegate
            { searchHint.Visibility = _chatSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            sidebarTop.Children.Add(searchHost);
            _chatSessions = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0),
                FontSize = 13, Foreground = ChatInk };
            _chatSessions.MouseDoubleClick += delegate { OpenSelectedConversation(); };
            _chatSessions.PreviewMouseLeftButtonUp += delegate { if (!_refreshingSessions) OpenSelectedConversation(); };
            _chatSessions.PreviewMouseRightButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                var item = ItemsControl.ContainerFromElement(_chatSessions, e.OriginalSource as DependencyObject) as ListBoxItem;
                if (item != null) item.IsSelected = true;
            };
            _chatSessions.KeyDown += delegate(object sender, KeyEventArgs e)
            { if (e.Key == Key.Enter) { OpenSelectedConversation(); e.Handled = true; } };
            var context = new ContextMenu();
            var rename = new MenuItem { Header = "重命名" };
            rename.Click += delegate { RenameSelectedConversation(); };
            var delete = new MenuItem { Header = "删除" };
            delete.Click += delegate { DeleteSelectedConversation(); };
            context.Items.Add(rename); context.Items.Add(delete);
            _chatSessions.ContextMenu = context;
            sidebarBody.Children.Add(_chatSessions);

            var main = new DockPanel { Background = Brushes.White };
            Grid.SetColumn(main, 1); shell.Children.Add(main);
            var titleBar = new DockPanel { Margin = new Thickness(22, 14, 20, 12) };
            DockPanel.SetDock(titleBar, Dock.Top); main.Children.Add(titleBar);
            var titleActions = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(titleActions, Dock.Right); titleBar.Children.Add(titleActions);
            Detach(_modelSummary); _modelSummary.MaxWidth = 180;
            _modelSummary.TextTrimming = TextTrimming.CharacterEllipsis;
            var modelButton = Action("", delegate { if (_openModelSettings != null) _openModelSettings(); });
            modelButton.Content = _modelSummary; modelButton.ToolTip = "AI 模型设置";
            titleActions.Children.Add(modelButton);
            var conversationMenu = new ContextMenu();
            var topic = new MenuItem { Header = "从此处开始新话题" };
            topic.Click += delegate { StartNewTopic(); }; conversationMenu.Items.Add(topic);
            var records = new MenuItem { Header = "管理会话" };
            records.Click += delegate { ShowConversations(); }; conversationMenu.Items.Add(records);
            var menuButton = Action("⋯", delegate { conversationMenu.IsOpen = true; });
            menuButton.ToolTip = "会话操作"; conversationMenu.PlacementTarget = menuButton;
            titleActions.Children.Add(menuButton);
            var collapse = Action("☰", delegate { _sidebarOpen = !_sidebarOpen; UpdateChatWidth(); });
            titleActions.Children.Add(collapse);
            var heading = new StackPanel();
            _chatTitle = new TextBlock { FontSize = 17, Foreground = ChatInk,
                FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            heading.Children.Add(_chatTitle);
            heading.VerticalAlignment = VerticalAlignment.Center;
            titleBar.Children.Add(heading);

            var composerArea = new StackPanel { Margin = new Thickness(20, 0, 20, 12) };
            DockPanel.SetDock(composerArea, Dock.Bottom); main.Children.Add(composerArea);
            _contextNotice = new TextBlock { Foreground = ChatMuted, FontSize = 12,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(4, 0, 4, 7), Visibility = Visibility.Collapsed };
            composerArea.Children.Add(_contextNotice);
            var composer = new Border { Background = Brushes.White, BorderBrush = ChatLine,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13),
                Padding = new Thickness(11, 9, 11, 8) };
            var composerInner = new StackPanel(); composer.Child = composerInner;
            Detach(_imageList); composerInner.Children.Add(_imageList);
            Detach(_intent); _intent.Height = 64; _intent.MinHeight = 48;
            _intent.Style = SettingsWindow.CreateTextBoxStyle();
            _intent.BorderThickness = new Thickness(0);
            _intent.Padding = new Thickness(2, 4, 2, 4);
            _intent.ToolTip = "输入你的要求；可直接 Ctrl+V 粘贴截图，或拖入图片";
            _intent.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
                try
                {
                    if (!Clipboard.ContainsFileDropList()) return;
                    string[] copiedPaths = Clipboard.GetFileDropList().Cast<string>().ToArray();
                    string[] paths = copiedPaths.Where(IsImagePath).ToArray();
                    if (paths.Length == 0)
                    {
                        e.Handled = true;
                        _status.Text = "仅支持图片；请复制截图或 PNG/JPEG 图片。";
                        return;
                    }
                    if (paths.Length != copiedPaths.Length)
                        _status.Text = "文档不会添加；已忽略非图片文件。";
                    e.Handled = true; AddImagePaths(paths);
                }
                catch { _status.Text = "剪贴板读取失败，请重新复制图片。"; }
            };
            _intent.PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                e.Handled = true;
                e.Effects = paths.Any(IsImagePath) ? DragDropEffects.Copy : DragDropEffects.None;
            };
            _intent.PreviewDrop += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                if (paths.Any(path => !IsImagePath(path)))
                    _status.Text = "仅支持图片；文档不会添加或发送。";
                paths = paths.Where(IsImagePath).ToArray();
                if (paths.Length == 0) { e.Handled = true; return; }
                e.Handled = true; AddImagePaths(paths);
            };
            var inputHost = new Grid();
            inputHost.Children.Add(_intent);
            var hint = new TextBlock { Text = "输入消息…",
                Foreground = ChatMuted, FontSize = 13, IsHitTestVisible = false,
                Margin = new Thickness(6, 9, 0, 0) };
            inputHost.Children.Add(hint);
            _intent.TextChanged += delegate
            { hint.Visibility = _intent.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            hint.Visibility = _intent.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            composerInner.Children.Add(inputHost);
            var controls = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
            composerInner.Children.Add(controls);
            var sendGroup = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right };
            DockPanel.SetDock(sendGroup, Dock.Right); controls.Children.Add(sendGroup);
            sendGroup.Children.Add(new TextBlock { Text = "回复语言", Foreground = ChatMuted,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0), FontSize = 12 });
            Detach(_language); _language.Width = 120; _language.MinHeight = 33;
            sendGroup.Children.Add(_language);
            Detach(_generate); _generate.Content = new System.Windows.Shapes.Path {
                Data = Geometry.Parse("M 2,10 L 18,10 M 11,3 L 18,10 L 11,17"),
                Stroke = Brushes.White, StrokeThickness = 2, Width = 20, Height = 20 };
            _generate.Width = 42; _generate.Height = 40; _generate.MinWidth = 0;
            _generate.Padding = new Thickness(8); _generate.ToolTip = "发送（Enter）· Shift+Enter 换行";
            System.Windows.Automation.AutomationProperties.SetName(_generate, "发送");
            sendGroup.Children.Add(_generate);
            Detach(_stop); sendGroup.Children.Add(_stop);
            Detach(_adviceOnly);
            composerArea.Children.Add(composer);
            Detach(_status); _status.FontSize = 12; _status.Foreground = ChatMuted;
            _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
            var statusDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(TextBlock.TextProperty, typeof(TextBlock));
            EventHandler statusChanged = delegate { _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible; };
            statusDescriptor.AddValueChanged(_status, statusChanged);
            Closed += delegate { statusDescriptor.RemoveValueChanged(_status, statusChanged); };
            composerArea.Children.Add(_status);
            _chatScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 5, 24, 8) };
            _chatFeed = new StackPanel(); _chatScroll.Content = _chatFeed; main.Children.Add(_chatScroll);
            Detach(_conversationHistory); _conversationHistory.Margin = new Thickness(0);
            _chatFeed.Children.Add(_conversationHistory);
            _chatResultCard = new Border { Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), Padding = new Thickness(0, 4, 0, 4),
                Margin = new Thickness(0, 15, 0, 14) };
            var result = new StackPanel(); _chatResultCard.Child = result;
            _inquiryPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 9), Visibility = Visibility.Collapsed };
            result.Children.Add(_inquiryPanel);
            Detach(_staleNotice); result.Children.Add(_staleNotice);
            Detach(_editNotice); result.Children.Add(_editNotice);
            Detach(_emptyResult);
            _chatAnswer = ChatReadingView.Create("");
            result.Children.Add(_chatAnswer);
            Detach(_result); _result.Height = double.NaN; _result.MinHeight = 90;
            _result.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _result.Style = SettingsWindow.CreateTextBoxStyle();
            result.Children.Add(_result);
            var resultActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Detach(_copy); resultActions.Children.Add(_copy);
            QuietIcon(_copy, "M6,5 L6,2 L17,2 L17,14 L14,14 M2,6 L13,6 L13,18 L2,18 Z", "复制回复");
            _editAnswer = Action("编辑", delegate
            {
                _editingAnswer = !_editingAnswer; UpdateChatResult();
                if (_editingAnswer) _result.Focus();
            });
            resultActions.Children.Add(_editAnswer);
            var details = new Expander { Header = "中文对照与核对信息", Margin = new Thickness(0, 12, 0, 0) };
            _detailsExpander = details;
            var detailBody = new StackPanel(); details.Content = detailBody;
            detailBody.Children.Add(new TextBlock { Text = "中文对照", Foreground = ChatMuted });
            Detach(_meaning); _meaning.Height = 70; _meaning.Style = SettingsWindow.CreateTextBoxStyle(); detailBody.Children.Add(_meaning);
            detailBody.Children.Add(new TextBlock { Text = "沟通建议 / 待确认", Foreground = ChatMuted, Margin = new Thickness(0, 8, 0, 0) });
            Detach(_advice); _advice.Style = SettingsWindow.CreateTextBoxStyle(); detailBody.Children.Add(_advice);
            Detach(_calculationLabel); detailBody.Children.Add(_calculationLabel);
            Detach(_calculation); detailBody.Children.Add(_calculation);
            Detach(_calculationActions); detailBody.Children.Add(_calculationActions);
            Detach(_sourcesLabel); detailBody.Children.Add(_sourcesLabel);
            Detach(_sources); detailBody.Children.Add(_sources);
            Detach(_sourceActions); detailBody.Children.Add(_sourceActions);
            result.Children.Add(resultActions); result.Children.Add(details);
            _chatFeed.Children.Add(_chatResultCard);
            _result.TextChanged += delegate { UpdateChatResult(); };
            _advice.TextChanged += delegate { UpdateChatResult(); };
            SizeChanged += delegate { UpdateChatWidth(); };
            Loaded += delegate { _intent.Focus(); };
            RebuildChatHistory(); RefreshSessionList(); RefreshInquiry(); UpdateChatResult(); UpdateChatWidth(); UpdateContextNotice();
        }

        private Button QuickPrompt(string label, string prompt, string mode)
        {
            return Action(label, delegate { _chatTaskMode = mode; _intent.Text = prompt;
                _intent.Focus(); _intent.CaretIndex = _intent.Text.Length; });
        }

        private static bool IsImagePath(string path)
        {
            string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }

        private void RefreshInquiry()
        {
            if (_inquiryPanel == null) return;
            _inquiryPanel.Children.Clear();
            InquiryField[] fields = _lastResult == null ? null : _lastResult.InquiryFields;
            string[] missing = _lastResult == null ? null : _lastResult.MissingFields;
            if ((fields == null || fields.Length == 0) && (missing == null || missing.Length == 0))
            { _inquiryPanel.Visibility = Visibility.Collapsed; return; }
            _inquiryPanel.Visibility = Visibility.Visible;
            _inquiryPanel.Children.Add(new TextBlock { Text = "询盘整理 · 请核对原材料", FontSize = 13,
                FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 5) });
            foreach (InquiryField field in fields ?? new InquiryField[0])
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 1), Background = Brushes.White };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock { Text = field.Field, Foreground = ChatMuted,
                    Padding = new Thickness(10, 6, 4, 6) });
                var value = new TextBlock { Text = field.Value, TextWrapping = TextWrapping.Wrap,
                    Foreground = ChatInk, Padding = new Thickness(10, 6, 4, 6) };
                Grid.SetColumn(value, 1); row.Children.Add(value);
                _inquiryPanel.Children.Add(row);
            }
            if (missing != null && missing.Length > 0)
                _inquiryPanel.Children.Add(new TextBlock { Text = "待确认：" + string.Join("、", missing),
                    Foreground = new SolidColorBrush(Color.FromRgb(151, 95, 31)),
                    TextWrapping = TextWrapping.Wrap, Margin = new Thickness(1, 8, 0, 0) });
        }

        private void UpdateChatResult()
        {
            if (_chatResultCard == null) return;
            if (_chatAnswer != null)
            {
                if (_renderedAnswer != _result.Text)
                {
                    _renderedAnswer = _result.Text;
                    ChatReadingView.SetText(_chatAnswer, _renderedAnswer);
                }
                bool hasAnswer = !string.IsNullOrWhiteSpace(_result.Text);
                _chatAnswer.Visibility = hasAnswer && !_editingAnswer ? Visibility.Visible : Visibility.Collapsed;
                _result.Visibility = hasAnswer && _editingAnswer ? Visibility.Visible : Visibility.Collapsed;
                _editAnswer.Visibility = hasAnswer ? Visibility.Visible : Visibility.Collapsed;
                QuietIcon(_editAnswer, _editingAnswer ? "M2,10 L7,15 L18,3" : "M3,13 L13,3 L17,7 L7,17 L2,18 Z M11,5 L15,9", _editingAnswer ? "完成编辑" : "编辑回复");
            }
            _chatResultCard.Visibility = !string.IsNullOrWhiteSpace(_result.Text) ||
                !string.IsNullOrWhiteSpace(_advice.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_detailsExpander != null && !string.IsNullOrWhiteSpace(_calculation.Text))
                _detailsExpander.IsExpanded = true;
        }

        private void RebuildChatHistory()
        {
            if (!_chatLayout) return;
            _conversationHistory.Children.Clear();
            if (!string.IsNullOrWhiteSpace(_background.Text))
                _conversationHistory.Children.Add(HistoryBubble("旧版客户材料 · " + _background.Text, true));
            for (int i = 0; i < _turns.Count; i++)
            {
                if ((_session.TopicBreaks ?? new int[0]).Contains(i)) AddTopicDivider();
                CommunicationTurn turn = _turns[i];
                var userBubble = HistoryBubble("你 · " + (turn.Instruction ?? ""), true);
                var userBody = (StackPanel)userBubble.Child;
                var pictures = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (byte[] bytes in turn.Images ?? new byte[0][])
                {
                    byte[] captured = bytes;
                    var thumbnail = new Image { Source = PreviewSource(bytes), Width = 110, Height = 76,
                        Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand };
                    thumbnail.MouseLeftButtonUp += delegate { ShowStoredImage(captured); };
                    pictures.Children.Add(thumbnail);
                }
                if (pictures.Children.Count > 0) userBody.Children.Add(pictures);
                _conversationHistory.Children.Add(userBubble);
                if (i < _turns.Count - 1 || _lastResult == null)
                    _conversationHistory.Children.Add(HistoryBubble("Sharkey · " + (turn.Reply ?? "") +
                        (string.IsNullOrWhiteSpace(turn.Advice) ? "" : "\n待确认 / 建议：" + turn.Advice), false));
            }
            if ((_session.TopicBreaks ?? new int[0]).Contains(_turns.Count)) AddTopicDivider();
            _conversationHistory.Visibility = _turns.Count > 0 || !string.IsNullOrWhiteSpace(_background.Text)
                ? Visibility.Visible : Visibility.Collapsed;
        }

        private void AddTopicDivider()
        {
            _conversationHistory.Children.Add(new TextBlock { Text = "新话题", FontSize = 12,
                Foreground = ChatMuted, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 20, 0, 20) });
        }

        private void StartNewTopic()
        {
            if (_turns.Count == TopicStart) { _intent.Focus(); return; }
            CancelRequest();
            if (_lastResult != null && _turns.Count > 0) _turns[_turns.Count - 1].Reply = _result.Text;
            _session.TopicBreaks = (_session.TopicBreaks ?? new int[0]).Concat(new[] { _turns.Count }).Distinct().ToArray();
            _loading = true;
            _lastResult = null; _editingAnswer = false;
            _result.Clear(); _meaning.Clear(); _advice.Clear(); _calculation.Clear(); _sources.Clear(); _adjustment.Clear();
            _editNotice.Visibility = _staleNotice.Visibility = Visibility.Collapsed;
            _calculationLabel.Visibility = _calculation.Visibility = _calculationActions.Visibility = Visibility.Collapsed;
            _sourcesLabel.Visibility = _sources.Visibility = _sourceActions.Visibility = Visibility.Collapsed;
            if (_detailsExpander != null) _detailsExpander.IsExpanded = false;
            _chatTaskMode = "";
            _loading = false;
            RebuildChatHistory(); RefreshInquiry(); UpdateChatResult(); UpdateContextNotice();
            _status.Text = ""; ScheduleSave(); _intent.Focus();
            _chatScroll.ScrollToEnd();
        }

        private void UpdateContextNotice()
        {
            if (_contextNotice == null) return;
            var context = ConversationContext.Create(_turns, TopicStart, _images.ToArray());
            _contextNotice.Text = (context.LimitedTurns ? "后续仅参考本话题最近 10 轮；更早记录仍保留。" : "") +
                (context.LimitedImages ? "后续仅附带最近 5 张图片。" : "");
            _contextNotice.Visibility = _contextNotice.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowStoredImage(byte[] bytes)
        {
            new Window { Owner = this, Title = "图片", Width = Math.Min(900, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(700, SystemParameters.WorkArea.Height - 40),
                Content = new ScrollViewer { Content = new Image { Source = PreviewSource(bytes, 0), Stretch = Stretch.Uniform } }
            }.ShowDialog();
        }

        private static void QuietIcon(Button button, string geometry, string label)
        {
            StyleAction(button, false);
            button.Background = Brushes.Transparent; button.BorderThickness = new Thickness(0);
            button.Width = 32; button.Height = 32; button.MinHeight = 32; button.MinWidth = 0;
            button.Padding = new Thickness(6); button.ToolTip = label;
            System.Windows.Automation.AutomationProperties.SetName(button, label);
            button.Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(geometry),
                Width = 18, Height = 18, Stroke = ChatMuted, StrokeThickness = 1.5, Stretch = Stretch.Uniform };
        }

        private void UpdateChatWidth()
        {
            if (_chatSidebar == null) return;
            bool narrow = ActualWidth > 0 && ActualWidth < 800;
            if (narrow != _wasNarrow) { _sidebarOpen = !narrow; _wasNarrow = narrow; }
            _chatSidebar.Visibility = _sidebarOpen ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_chatSidebar, narrow ? 1 : 0);
            _chatSidebar.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            _chatSidebar.Width = narrow ? 220 : double.NaN;
            Panel.SetZIndex(_chatSidebar, narrow ? 2 : 0);
            ((Grid)Content).ColumnDefinitions[0].Width = new GridLength(narrow || !_sidebarOpen ? 0 : 220);
        }

        private void RefreshSessionList()
        {
            if (_chatSessions == null) return;
            _refreshingSessions = true;
            string search = _chatSearch == null ? "" : _chatSearch.Text.Trim();
            _chatSessions.Items.Clear();
            foreach (string path in ConversationStore.Files())
                try
                {
                    SavedConversation item = ConversationStore.Load(path);
                    if (search.Length == 0 || item.Title.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) >= 0)
                        _chatSessions.Items.Add(item);
                }
                catch { /* The unreadable file remains available for recovery. */ }
            foreach (SavedConversation item in _chatSessions.Items)
                if (item.Id == _session.Id) { _chatSessions.SelectedItem = item; break; }
            if (_chatTitle != null) _chatTitle.Text = _session.Title;
            _refreshingSessions = false;
        }

        private void OpenSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SavedConversation;
            if (selected == null || selected.Id == _session.Id || !SaveConversation()) return;
            RestoreConversation(selected); RefreshSessionList();
        }

        private void RenameSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SavedConversation;
            if (selected == null) return;
            var text = new TextBox { Text = selected.Title, Margin = new Thickness(14), MaxLength = 80 };
            var prompt = new Window { Owner = this, Width = 340, Height = 110, Title = "重命名会话",
                WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = text };
            text.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(text.Text)) return;
                string old = selected.Title; selected.Title = text.Text.Trim();
                try { ConversationStore.Save(selected); if (selected.Id == _session.Id) _session.Title = selected.Title;
                    prompt.Close(); RefreshSessionList(); }
                catch { selected.Title = old; _status.Text = "重命名保存失败。"; }
            };
            prompt.ShowDialog();
        }

        private void DeleteSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SavedConversation;
            if (selected == null) return;
            if (MessageBox.Show(this, "删除此会话及其材料？", "删除会话", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                ConversationStore.Delete(selected.Id);
                if (selected.Id == _session.Id) RestoreConversation(new SavedConversation());
                RefreshSessionList();
            }
            catch { _status.Text = "删除失败，会话仍保留。"; }
        }

        private string CombinedCustomerMaterials()
        {
            return _background.Text ?? "";
        }

        private void AddImagePaths(string[] paths)
        {
            if (paths == null) return;
            foreach (string path in paths)
            {
                if (!IsImagePath(path)) continue;
                if (_images.Count >= 5)
                { _status.Text = "每次沟通最多添加 5 张图片。"; break; }
                try { using (var bitmap = new System.Drawing.Bitmap(path)) AddBitmap(bitmap); }
                catch { _status.Text = "图片读取失败，已有图片保留。"; }
            }
        }
    }
}
