using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;

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
        private TextBlock _chatWelcome;
        private ScrollViewer _chatScroll;
        private StackPanel _chatFeed;
        private StackPanel _inquiryPanel;
        private string _chatTaskMode = "";
        private WrapPanel _documentList;
        private readonly List<CommerceDocument> _documents = new List<CommerceDocument>();
        private bool _chatLayout;
        private bool _sidebarOpen = true;
        private bool _wasNarrow;
        private bool _refreshingSessions;
        private RichTextBox _chatAnswer;
        private Button _editAnswer;
        private bool _editingAnswer;
        private string _renderedAnswer;

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
            sidebarBottom.Children.Add(new TextBlock { Text = "本机加密保存", FontSize = 11.5,
                Foreground = ChatMuted, Margin = new Thickness(9, 4, 0, 0) });
            var sidebarTop = new StackPanel();
            DockPanel.SetDock(sidebarTop, Dock.Top); sidebarBody.Children.Add(sidebarTop);
            var brand = new WrapPanel { Margin = new Thickness(8, 0, 0, 16) };
            if (Application.Current != null)
                try { brand.Children.Add(new Image { Source = new System.Windows.Media.Imaging.BitmapImage(
                    new Uri("pack://application:,,,/assets/shark-logo.png")), Width = 30, Height = 30,
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
            titleActions.Children.Add(Action("⋯", delegate { ShowConversations(); }));
            var collapse = Action("☰", delegate { _sidebarOpen = !_sidebarOpen; UpdateChatWidth(); });
            titleActions.Children.Add(collapse);
            var heading = new StackPanel();
            _chatTitle = new TextBlock { FontSize = 17, Foreground = ChatInk,
                FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis };
            heading.Children.Add(_chatTitle);
            Detach(_modelSummary); heading.Children.Add(_modelSummary);
            titleBar.Children.Add(heading);

            var composerArea = new StackPanel { Margin = new Thickness(20, 0, 20, 12) };
            DockPanel.SetDock(composerArea, Dock.Bottom); main.Children.Add(composerArea);
            var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 7) };
            quick.Children.Add(QuickPrompt("整理询盘", "请整理这份询盘，列出已知需求、待确认信息和简短追问；不要猜测。", "inquiry"));
            quick.Children.Add(QuickPrompt("对比报价", "请比较这些报价的单价、币种、起订量、交期和费用包含范围；列出不能直接比较的项目。", "quote"));
            quick.Children.Add(QuickPrompt("核对变更", "请比较前后两版客户要求，列出新增、删除和变更项目，以及需要重新确认的报价或交期。", "change"));
            composerArea.Children.Add(quick);
            var composer = new Border { Background = Brushes.White, BorderBrush = ChatLine,
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(13),
                Padding = new Thickness(11, 9, 11, 8) };
            var composerInner = new StackPanel(); composer.Child = composerInner;
            _documentList = new WrapPanel { Margin = new Thickness(0, 0, 0, 2) };
            composerInner.Children.Add(_documentList);
            Detach(_imageList); composerInner.Children.Add(_imageList);
            Detach(_intent); _intent.Height = 80; _intent.MinHeight = 64;
            _intent.Style = SettingsWindow.CreateTextBoxStyle();
            _intent.BorderThickness = new Thickness(0);
            _intent.Padding = new Thickness(2, 4, 2, 4);
            _intent.ToolTip = "输入你的要求，或粘贴客户消息、截图和文档";
            _intent.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter || e.IsRepeat || _composing ||
                    e.ImeProcessedKey == Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
                e.Handled = true;
                if (_request == null) { var ignored = Generate(false); }
            };
            _intent.PreviewKeyDown += async delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
                try
                {
                    if (!Clipboard.ContainsFileDropList()) return;
                    string[] paths = Clipboard.GetFileDropList().Cast<string>()
                        .Where(IsAssistantMaterial).ToArray();
                    if (paths.Length == 0) return;
                    e.Handled = true; await AddMaterialPaths(paths);
                }
                catch { _status.Text = "剪贴板文件读取失败，请重新复制或拖入文件。"; }
            };
            _intent.PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths != null && paths.Any(IsAssistantMaterial))
                { e.Effects = DragDropEffects.Copy; e.Handled = true; }
            };
            _intent.PreviewDrop += async delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                paths = paths.Where(IsAssistantMaterial).ToArray();
                if (paths.Length == 0) return;
                e.Handled = true; await AddMaterialPaths(paths);
            };
            var inputHost = new Grid();
            inputHost.Children.Add(_intent);
            var hint = new TextBlock { Text = "输入要求；截图可 Ctrl+V，图片或文件可 Ctrl+V 或拖入…",
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
            Detach(_language); _language.Width = 185; _language.MinHeight = 33;
            sendGroup.Children.Add(_language);
            Detach(_generate); _generate.Content = "发送"; sendGroup.Children.Add(_generate);
            Detach(_stop); sendGroup.Children.Add(_stop);
            var attachGroup = new WrapPanel(); controls.Children.Add(attachGroup);
            attachGroup.Children.Add(Action("截图", async delegate { await PrepareCapture(); }));
            var moreMenu = new ContextMenu();
            var addFile = new MenuItem { Header = "浏览文件…" };
            addFile.Click += delegate { AddMaterials(); };
            moreMenu.Items.Add(addFile);
            Detach(_adviceOnly);
            var adviceMenu = new MenuItem { Header = "仅给建议" };
            adviceMenu.Click += async delegate { await Generate(true); };
            moreMenu.Items.Add(adviceMenu);
            var more = Action("更多", delegate { moreMenu.IsOpen = true; });
            moreMenu.PlacementTarget = more; attachGroup.Children.Add(more);
            composerArea.Children.Add(composer);
            composerArea.Children.Add(new TextBlock { Text = "Enter 发送 · Shift+Enter 换行",
                FontSize = 11.5, Foreground = ChatMuted, Margin = new Thickness(4, 5, 0, 0) });
            Detach(_status); _status.FontSize = 12; _status.Foreground = ChatMuted;
            composerArea.Children.Add(_status);
            _chatScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 5, 24, 8) };
            _chatFeed = new StackPanel(); _chatScroll.Content = _chatFeed; main.Children.Add(_chatScroll);
            _chatWelcome = new TextBlock { Text = "直接输入要求，或将客户消息、截图和文件粘贴或拖入输入框。",
                FontSize = 14, Foreground = ChatMuted, Margin = new Thickness(4, 28, 0, 0) };
            _chatFeed.Children.Add(_chatWelcome);
            Detach(_conversationHistory); _conversationHistory.Margin = new Thickness(0);
            _chatFeed.Children.Add(_conversationHistory);
            _chatResultCard = new Border { Background = new SolidColorBrush(Color.FromRgb(248, 251, 253)),
                BorderBrush = ChatLine, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11), Padding = new Thickness(15),
                Margin = new Thickness(0, 15, 0, 14) };
            var result = new StackPanel(); _chatResultCard.Child = result;
            result.Children.Add(new TextBlock { Text = "Sharkey", FontSize = 13, Margin = new Thickness(0, 0, 0, 12),
                FontWeight = FontWeights.SemiBold, Foreground = ChatInk });
            _inquiryPanel = new StackPanel { Margin = new Thickness(0, 8, 0, 9), Visibility = Visibility.Collapsed };
            result.Children.Add(_inquiryPanel);
            Detach(_staleNotice); result.Children.Add(_staleNotice);
            Detach(_editNotice); result.Children.Add(_editNotice);
            Detach(_emptyResult); _emptyResult.Text = "粘贴客户材料或输入要求，发送后在这里查看结果。";
            result.Children.Add(_emptyResult);
            _chatAnswer = ChatReadingView.Create("");
            result.Children.Add(_chatAnswer);
            Detach(_result); _result.Height = double.NaN; _result.MinHeight = 90;
            _result.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _result.Style = SettingsWindow.CreateTextBoxStyle();
            result.Children.Add(_result);
            var resultActions = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Detach(_copy); resultActions.Children.Add(_copy);
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
            RebuildChatHistory(); RefreshComposerAttachments(); RefreshSessionList(); RefreshInquiry(); UpdateChatResult(); UpdateChatWidth();
        }

        private Button QuickPrompt(string label, string prompt, string mode)
        {
            return Action(label, delegate { _chatTaskMode = mode; _intent.Text = prompt;
                _intent.Focus(); _intent.CaretIndex = _intent.Text.Length; });
        }

        private static bool IsAssistantMaterial(string path)
        {
            string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg" ||
                CommerceDocuments.IsSupported(path ?? "");
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
                _editAnswer.Content = _editingAnswer ? "完成编辑" : "编辑";
            }
            _chatResultCard.Visibility = _turns.Count > 0 || !string.IsNullOrWhiteSpace(_result.Text) ||
                !string.IsNullOrWhiteSpace(_advice.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_chatWelcome != null)
                _chatWelcome.Visibility = _chatResultCard.Visibility == Visibility.Visible ||
                    _conversationHistory.Children.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
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
                CommunicationTurn turn = _turns[i];
                _conversationHistory.Children.Add(HistoryBubble("你 · " + (turn.Instruction ?? ""), true));
                if (i < _turns.Count - 1)
                    _conversationHistory.Children.Add(HistoryBubble("Sharkey · " + (turn.Reply ?? "") +
                        (string.IsNullOrWhiteSpace(turn.Advice) ? "" : "\n待确认 / 建议：" + turn.Advice), false));
            }
            _conversationHistory.Visibility = _turns.Count > 0 || !string.IsNullOrWhiteSpace(_background.Text)
                ? Visibility.Visible : Visibility.Collapsed;
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
            var builder = new System.Text.StringBuilder(_background.Text ?? "");
            foreach (CommerceDocument document in _documents)
            {
                if (string.IsNullOrWhiteSpace(document.Text)) continue;
                builder.Append("\n\n[客户附件: ").Append(document.Name)
                    .Append(" · ").Append(document.Kind).Append("]\n")
                    .Append(document.Text);
            }
            return builder.ToString();
        }

        private async void AddMaterials()
        {
            var dialog = new OpenFileDialog { Title = "添加客户材料",
                Filter = "支持的材料|*.png;*.jpg;*.jpeg;*.pdf;*.docx;*.xlsx;*.csv",
                Multiselect = true };
            if (dialog.ShowDialog(this) != true) return;
            await AddMaterialPaths(dialog.FileNames);
        }

        private async Task AddMaterialPaths(string[] paths)
        {
            string sessionId = _session.Id;
            foreach (string path in paths)
            {
                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    try { using (var bitmap = new System.Drawing.Bitmap(path)) AddBitmap(bitmap); }
                    catch { _status.Text = "图片读取失败，已有材料保留。"; }
                    continue;
                }
                if (!CommerceDocuments.IsSupported(path)) continue;
                if (_documents.Count >= CommerceDocuments.MaxFiles)
                { _status.Text = "最多添加 5 个文档。"; break; }
                var waiting = new CommerceDocument { Name = Path.GetFileName(path),
                    Kind = ext.TrimStart('.').ToUpperInvariant(), Status = "读取中" };
                _documents.Add(waiting); RefreshComposerAttachments();
                try
                {
                    CommerceDocument ready = await Task.Run(() => CommerceDocuments.Read(path));
                    if (_session.Id != sessionId) return;
                    int total = _documents.Where(x => x != waiting).Sum(x => x.Text.Length) + ready.Text.Length;
                    if (total > 120000) throw new InvalidDataException("材料合计过长，请只保留相关文档。");
                    int index = _documents.IndexOf(waiting);
                    if (index >= 0) _documents[index] = ready;
                    OnMaterialChanged(); ScheduleSave();
                    _status.Text = "已读取 " + ready.Name + "。";
                }
                catch (Exception error)
                {
                    if (_session.Id != sessionId) return;
                    _documents.Remove(waiting);
                    _status.Text = waiting.Name + "：" + error.Message;
                }
                RefreshComposerAttachments();
            }
        }

        private void RefreshComposerAttachments()
        {
            if (_documentList == null) return;
            _documentList.Children.Clear();
            foreach (CommerceDocument document in _documents.ToArray())
            {
                var chip = new WrapPanel { Margin = new Thickness(0, 0, 6, 6),
                    Background = new SolidColorBrush(Color.FromRgb(244, 249, 251)) };
                chip.Children.Add(new TextBlock { Text = document.Kind + "  " + document.Name +
                    " · " + document.Status, FontSize = 12, Foreground = ChatInk,
                    VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 2, 0),
                    MaxWidth = 230, TextTrimming = TextTrimming.CharacterEllipsis });
                chip.Children.Add(Action("×", delegate
                { _documents.Remove(document); RefreshComposerAttachments(); OnMaterialChanged(); ScheduleSave(); }));
                _documentList.Children.Add(chip);
            }
        }
    }
}
