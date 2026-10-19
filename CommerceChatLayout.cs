using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
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
        private TextBlock _chatSubtitle;
        private Border _chatSidebar;
        private Border _chatResultCard;
        private Expander _detailsExpander;
        private ScrollViewer _chatScroll;
        private StackPanel _chatFeed;
        private StackPanel _inquiryPanel;
        private FrameworkElement _emptyState;
        private Border _composerCard;
        private TextBlock _inputHint;
        private string _chatTaskMode = "";
        private bool _chatLayout;
        private bool _sidebarOpen = true;
        private bool _wasNarrow;
        private bool _refreshingSessions;
        private bool _dragHighlight;
        private RichTextBox _chatAnswer;
        private Button _editAnswer;
        private bool _editingAnswer;
        private string _renderedAnswer;
        private TextBlock _contextNotice;
        private int TopicStart { get { return (_session.TopicBreaks ?? new int[0]).LastOrDefault(); } }

        private static readonly Brush ChatInk = Ui.Palette.Ink;
        private static readonly Brush ChatLine = Ui.Palette.Line;
        private static readonly Brush ChatMuted = Ui.Palette.InkMuted;

        private sealed class SessionRow
        {
            public SavedConversation Item;
            public string Title { get; set; }
            public string Meta { get; set; }
            public override string ToString() { return Title ?? ""; }
        }

        private static void Detach(FrameworkElement element)
        {
            var parent = element.Parent as Panel;
            if (parent != null) parent.Children.Remove(element);
        }

        private void BuildChatInterface()
        {
            _chatLayout = true;
            Background = Ui.Palette.Surface;
            FitToWorkArea();
            Width = Math.Min(1180, Math.Max(Width, 1040));
            Height = Math.Min(820, Math.Max(Height, 720));
            MinWidth = 480; MinHeight = 500;
            Ui.InstallTextInputStyles(this);

            var shell = new Grid();
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(264) });
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Content = shell;

            _chatSidebar = new Border
            {
                Background = Ui.Palette.Canvas,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(0, 0, 1, 0),
                Child = BuildSidebar()
            };
            shell.Children.Add(_chatSidebar);

            var main = new Grid { Background = Ui.Palette.Surface };
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            main.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            main.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(main, 1);
            shell.Children.Add(main);

            FrameworkElement header = BuildChatHeader();
            Grid.SetRow(header, 0);
            main.Children.Add(header);
            FrameworkElement body = BuildChatBody();
            Grid.SetRow(body, 1);
            main.Children.Add(body);
            FrameworkElement composer = BuildComposer();
            Grid.SetRow(composer, 2);
            main.Children.Add(composer);

            SizeChanged += delegate { UpdateChatWidth(); };
            Loaded += delegate { _intent.Focus(); };
            RebuildChatHistory(); RefreshSessionList(); RefreshInquiry();
            UpdateChatResult(); UpdateChatWidth(); UpdateContextNotice();
        }

        // ------------------------------------------------------------- sidebar

        private FrameworkElement BuildSidebar()
        {
            var layout = new Grid { Margin = new Thickness(14, 18, 14, 14) };
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var brand = new Grid { Margin = new Thickness(6, 0, 4, 18) };
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            brand.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            try
            {
                brand.Children.Add(new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Sharkey;component/assets/shark-logo.png")),
                    Width = 30, Height = 30, Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                });
            }
            catch { /* The text brand stays usable without the logo. */ }
            var brandText = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            brandText.Children.Add(Ui.Text("Sharkey", Ui.Type.Subtitle, Ui.Palette.Ink, FontWeights.SemiBold));
            TextBlock role = Ui.Text("外贸沟通助手", Ui.Type.Caption, Ui.Palette.InkMuted);
            role.Margin = new Thickness(0, 1, 0, 0);
            brandText.Children.Add(role);
            Grid.SetColumn(brandText, 1);
            brand.Children.Add(brandText);
            TextBlock version = Ui.Text("v" + VersionInfo.SemanticVersion, Ui.Type.Caption, Ui.Palette.InkFaint);
            version.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(version, 2);
            brand.Children.Add(version);
            Grid.SetRow(brand, 0);
            layout.Children.Add(brand);

            Button create = Ui.PrimaryButton("新建会话");
            create.HorizontalAlignment = HorizontalAlignment.Stretch;
            create.Height = 38;
            create.MinHeight = 38;
            create.Padding = new Thickness(10, 0, 10, 0);
            create.Margin = new Thickness(0, 0, 0, 12);
            create.Content = IconLabel(Icons.Plus, "新建会话", Ui.Palette.OnBrand);
            create.Click += delegate { NewConversation(); };
            Grid.SetRow(create, 1);
            layout.Children.Add(create);

            var searchHost = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            _chatSearch = new TextBox
            {
                Height = 38,
                FontSize = Ui.Type.BodySmall,
                Padding = new Thickness(32, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "按会话标题搜索"
            };
            _chatSearch.TextChanged += delegate { RefreshSessionList(); };
            searchHost.Children.Add(_chatSearch);
            var searchIcon = Ui.Glyph(Icons.Search, Ui.Palette.InkFaint, 15, 1.6);
            searchIcon.HorizontalAlignment = HorizontalAlignment.Left;
            searchIcon.Margin = new Thickness(11, 0, 0, 0);
            searchIcon.IsHitTestVisible = false;
            searchHost.Children.Add(searchIcon);
            TextBlock searchHint = Ui.Text("搜索会话", Ui.Type.BodySmall, Ui.Palette.InkFaint);
            searchHint.IsHitTestVisible = false;
            searchHint.Margin = new Thickness(33, 0, 0, 0);
            searchHint.VerticalAlignment = VerticalAlignment.Center;
            searchHost.Children.Add(searchHint);
            _chatSearch.TextChanged += delegate
            { searchHint.Visibility = _chatSearch.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            Grid.SetRow(searchHost, 2);
            layout.Children.Add(searchHost);

            _chatSessions = new ListBox
            {
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = Ui.Type.BodySmall,
                Foreground = Ui.Palette.Ink,
                ItemTemplate = SessionTemplate(),
                ItemContainerStyle = SessionItemStyle(),
                Margin = new Thickness(-6, 0, -6, 0),
                Padding = new Thickness(0)
            };
            ScrollViewer.SetHorizontalScrollBarVisibility(_chatSessions, ScrollBarVisibility.Disabled);
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
            context.Items.Add(rename);
            context.Items.Add(delete);
            _chatSessions.ContextMenu = context;
            Grid.SetRow(_chatSessions, 3);
            layout.Children.Add(_chatSessions);

            var footer = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            Button settings = Ui.GhostButton("设置");
            settings.Content = IconLabel(Icons.Settings, "设置", Ui.Palette.InkMuted);
            settings.HorizontalAlignment = HorizontalAlignment.Stretch;
            settings.HorizontalContentAlignment = HorizontalAlignment.Left;
            settings.Padding = new Thickness(10, 7, 10, 7);
            settings.ToolTip = "AI 模型与助手偏好";
            settings.Click += delegate { if (_openModelSettings != null) _openModelSettings(); };
            footer.Children.Add(settings);
            Grid.SetRow(footer, 4);
            layout.Children.Add(footer);

            return layout;
        }

        private static DataTemplate SessionTemplate()
        {
            var stack = new FrameworkElementFactory(typeof(StackPanel));
            var title = new FrameworkElementFactory(typeof(TextBlock));
            title.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Title"));
            title.SetValue(TextBlock.FontSizeProperty, Ui.Type.BodySmall);
            title.SetValue(TextBlock.FontWeightProperty, FontWeights.Medium);
            title.SetValue(TextBlock.ForegroundProperty, Ui.Palette.Ink);
            title.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            stack.AppendChild(title);
            var meta = new FrameworkElementFactory(typeof(TextBlock));
            meta.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Meta"));
            meta.SetValue(TextBlock.FontSizeProperty, Ui.Type.Caption);
            meta.SetValue(TextBlock.ForegroundProperty, Ui.Palette.InkMuted);
            meta.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            meta.SetValue(FrameworkElement.MarginProperty, new Thickness(0, 3, 0, 0));
            stack.AppendChild(meta);
            return new DataTemplate { VisualTree = stack };
        }

        private static Style SessionItemStyle()
        {
            var style = new Style(typeof(ListBoxItem));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(0, 0, 0, 2)));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            var template = new ControlTemplate(typeof(ListBoxItem));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "Row";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Ui.Radius.Control));
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetBinding(Border.PaddingProperty, new System.Windows.Data.Binding("Padding")
            {
                RelativeSource = new System.Windows.Data.RelativeSource(
                    System.Windows.Data.RelativeSourceMode.TemplatedParent)
            });
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(presenter);
            template.VisualTree = border;
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Palette.SurfaceHover, "Row"));
            template.Triggers.Add(hover);
            var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Palette.BrandTint, "Row"));
            template.Triggers.Add(selected);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        // -------------------------------------------------------------- header

        private FrameworkElement BuildChatHeader()
        {
            var header = new Border
            {
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(28, 13, 22, 12)
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            header.Child = grid;

            var titles = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _chatTitle = Ui.Text("新沟通", Ui.Type.Subtitle, Ui.Palette.Ink, FontWeights.SemiBold);
            _chatSubtitle = Ui.Text("", Ui.Type.Meta, Ui.Palette.InkMuted);
            _chatSubtitle.Margin = new Thickness(1, 3, 0, 0);
            titles.Children.Add(_chatTitle);
            titles.Children.Add(_chatSubtitle);
            grid.Children.Add(titles);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(actions, 1);
            grid.Children.Add(actions);

            Detach(_modelSummary);
            _modelSummary.FontSize = Ui.Type.Meta;
            _modelSummary.Foreground = Ui.Palette.InkMuted;
            _modelSummary.MaxWidth = 150;
            _modelSummary.TextTrimming = TextTrimming.CharacterEllipsis;
            _modelSummary.VerticalAlignment = VerticalAlignment.Center;
            var model = new Button
            {
                Content = _modelSummary,
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                MinHeight = 30,
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand,
                ToolTip = "AI 模型设置"
            };
            Ui.Style(model, Ui.ButtonKind.Secondary);
            model.Click += delegate { if (_openModelSettings != null) _openModelSettings(); };
            actions.Children.Add(model);

            var conversationMenu = new ContextMenu();
            var topic = new MenuItem { Header = "从此处开始新话题" };
            topic.Click += delegate { StartNewTopic(); };
            conversationMenu.Items.Add(topic);
            var records = new MenuItem { Header = "管理会话" };
            records.Click += delegate { ShowConversations(); };
            conversationMenu.Items.Add(records);
            Button menuButton = Ui.IconButton(Icons.More, "会话操作");
            menuButton.Margin = new Thickness(2, 0, 2, 0);
            menuButton.Click += delegate
            {
                conversationMenu.PlacementTarget = menuButton;
                conversationMenu.IsOpen = true;
            };
            actions.Children.Add(menuButton);

            Button collapse = Ui.IconButton(Icons.Sidebar, "收起或展开侧栏");
            collapse.Click += delegate { _sidebarOpen = !_sidebarOpen; UpdateChatWidth(); };
            actions.Children.Add(collapse);
            return header;
        }

        // ---------------------------------------------------------------- body

        private FrameworkElement BuildChatBody()
        {
            var host = new Grid();
            _chatScroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            _chatFeed = new StackPanel
            {
                Margin = new Thickness(28, 14, 28, 18),
                MaxWidth = 820,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            _chatScroll.Content = _chatFeed;
            host.Children.Add(_chatScroll);

            _emptyState = BuildEmptyState();
            _emptyState.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyState.VerticalAlignment = VerticalAlignment.Center;
            _emptyState.Margin = new Thickness(32, 0, 32, 56);
            host.Children.Add(_emptyState);

            Detach(_conversationHistory);
            _conversationHistory.Margin = new Thickness(0);
            _chatFeed.Children.Add(_conversationHistory);

            _chatResultCard = new Border { Background = Brushes.Transparent, Margin = new Thickness(0, 4, 0, 18) };
            var resultGrid = new Grid();
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            resultGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Border avatar = Avatar();
            avatar.VerticalAlignment = VerticalAlignment.Top;
            resultGrid.Children.Add(avatar);
            var result = new StackPanel();
            Grid.SetColumn(result, 1);
            resultGrid.Children.Add(result);
            _chatResultCard.Child = resultGrid;

            _inquiryPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed };
            result.Children.Add(_inquiryPanel);
            Detach(_staleNotice);
            result.Children.Add(_staleNotice);
            Detach(_editNotice);
            result.Children.Add(_editNotice);
            Detach(_emptyResult);

            _chatAnswer = ChatReadingView.Create("");
            result.Children.Add(_chatAnswer);

            Detach(_result);
            _result.Height = double.NaN;
            _result.MinHeight = 90;
            _result.VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _result.Style = SettingsWindow.CreateTextBoxStyle();
            result.Children.Add(_result);

            var resultActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(-6, 6, 0, 0)
            };
            Detach(_copy);
            resultActions.Children.Add(_copy);
            QuietIcon(_copy, Icons.Copy, "复制回复");
            _copy.Margin = new Thickness(0, 0, 2, 0);
            _editAnswer = Action("编辑", delegate
            {
                _editingAnswer = !_editingAnswer;
                UpdateChatResult();
                if (_editingAnswer) _result.Focus();
            });
            resultActions.Children.Add(_editAnswer);
            result.Children.Add(resultActions);

            var separator = Ui.Divider();
            separator.Margin = new Thickness(0, 10, 0, 0);
            result.Children.Add(separator);
            _detailsExpander = new Expander
            {
                Header = DetailHeader("中文对照与核对信息"),
                Margin = new Thickness(0),
                Template = Ui.ExpanderTemplate()
            };
            var detailBody = new StackPanel { Margin = new Thickness(0, 8, 0, 0) };
            _detailsExpander.Content = detailBody;
            detailBody.Children.Add(Ui.SectionHeader("中文对照"));
            Detach(_meaning);
            _meaning.Height = 70;
            _meaning.Style = SettingsWindow.CreateTextBoxStyle();
            _meaning.Margin = new Thickness(0, 5, 0, 10);
            detailBody.Children.Add(_meaning);
            detailBody.Children.Add(Ui.SectionHeader("沟通建议 / 待确认"));
            Detach(_advice);
            _advice.Style = SettingsWindow.CreateTextBoxStyle();
            _advice.Margin = new Thickness(0, 5, 0, 8);
            detailBody.Children.Add(_advice);
            Detach(_calculationLabel);
            _calculationLabel.FontSize = Ui.Type.Meta;
            _calculationLabel.Foreground = Ui.Palette.InkMuted;
            _calculationLabel.Margin = new Thickness(0, 12, 0, 4);
            detailBody.Children.Add(_calculationLabel);
            Detach(_calculation);
            _calculation.Background = Ui.Palette.SurfaceSubtle;
            _calculation.BorderBrush = Ui.Palette.Line;
            _calculation.Margin = new Thickness(0, 0, 0, 8);
            detailBody.Children.Add(_calculation);
            Detach(_calculationActions);
            detailBody.Children.Add(_calculationActions);
            Detach(_sourcesLabel);
            _sourcesLabel.FontSize = Ui.Type.Meta;
            _sourcesLabel.Foreground = Ui.Palette.InkMuted;
            _sourcesLabel.Margin = new Thickness(0, 8, 0, 4);
            detailBody.Children.Add(_sourcesLabel);
            Detach(_sources);
            _sources.Background = Ui.Palette.SurfaceSubtle;
            _sources.BorderBrush = Ui.Palette.Line;
            detailBody.Children.Add(_sources);
            Detach(_sourceActions);
            detailBody.Children.Add(_sourceActions);
            result.Children.Add(_detailsExpander);

            _chatFeed.Children.Add(_chatResultCard);
            _result.TextChanged += delegate { UpdateChatResult(); };
            _advice.TextChanged += delegate { UpdateChatResult(); };
            return host;
        }

        private static FrameworkElement DetailHeader(string text)
        {
            return Ui.Text(text, Ui.Type.Meta, Ui.Palette.InkMuted);
        }

        private static Border Avatar()
        {
            return new Border
            {
                Width = 26,
                Height = 26,
                CornerRadius = new CornerRadius(8),
                Background = Ui.Palette.BrandTint,
                Margin = new Thickness(0, 2, 10, 0),
                Child = Ui.Glyph(Icons.Sparkle, Ui.Palette.BrandText, 15, 1.4)
            };
        }

        // -------------------------------------------------------- empty state

        private FrameworkElement BuildEmptyState()
        {
            var stack = new StackPanel { MaxWidth = 780 };
            try
            {
                stack.Children.Add(new Image
                {
                    Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri("pack://application:,,,/Sharkey;component/assets/shark-logo.png")),
                    Width = 56, Height = 56, Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
            }
            catch { }
            TextBlock title = Ui.Text("把客户消息和截图，变成可以直接发的回复",
                Ui.Type.Display, Ui.Palette.Ink, FontWeights.SemiBold);
            title.TextWrapping = TextWrapping.Wrap;
            title.TextTrimming = TextTrimming.None;
            title.TextAlignment = TextAlignment.Center;
            title.Margin = new Thickness(0, 16, 0, 8);
            stack.Children.Add(title);
            TextBlock subtitle = Ui.Text("粘贴客户消息或截图，也可以从下面的常用任务开始。",
                Ui.Type.BodySmall, Ui.Palette.InkMuted);
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.TextTrimming = TextTrimming.None;
            subtitle.TextAlignment = TextAlignment.Center;
            stack.Children.Add(subtitle);

            var cards = new WrapPanel
            {
                Margin = new Thickness(0, 22, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            cards.Children.Add(SuggestionCard(Icons.List, "整理询盘", "列出已知需求与待确认信息",
                "请整理这份询盘，列出已知需求、待确认信息和简短追问；不要猜测。", "inquiry"));
            cards.Children.Add(SuggestionCard(Icons.Table, "对比报价", "逐项比较单价、交期与费用",
                "请比较这些报价的单价、币种、起订量、交期和费用包含范围；列出不能直接比较的项目。", "quote"));
            cards.Children.Add(SuggestionCard(Icons.Refresh, "核对变更", "找出新增、删除与改动项",
                "请比较前后两版客户要求，列出新增、删除和变更项目，以及需要重新确认的报价或交期。", "change"));
            stack.Children.Add(cards);

            var hints = new WrapPanel { Margin = new Thickness(0, 14, 0, 0), HorizontalAlignment = HorizontalAlignment.Center };
            hints.Children.Add(HintPair("Enter", "发送"));
            hints.Children.Add(HintPair("Shift+Enter", "换行"));
            hints.Children.Add(HintPair("Ctrl+V", "粘贴截图"));
            hints.Children.Add(HintPair("拖入", "添加图片"));
            stack.Children.Add(hints);
            return stack;
        }

        private static FrameworkElement IconLabel(Geometry glyph, string text, Brush color)
        {
            var row = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };
            var icon = Ui.Glyph(glyph, color, 15, 1.7);
            icon.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(icon);
            row.Children.Add(new TextBlock
            {
                Text = text,
                Foreground = color,
                FontSize = Ui.Type.BodySmall,
                VerticalAlignment = VerticalAlignment.Center
            });
            return row;
        }

        private FrameworkElement HintPair(string key, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 8) };
            Border cap = Ui.KeyCap(key);
            cap.Margin = new Thickness(0, 0, 6, 0);
            row.Children.Add(cap);
            TextBlock text = Ui.Text(label, Ui.Type.Meta, Ui.Palette.InkMuted);
            text.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(text);
            return row;
        }

        private Button SuggestionCard(Geometry glyph, string title, string detail, string prompt, string mode)
        {
            var body = new StackPanel();
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var icon = Ui.Glyph(glyph, Ui.Palette.BrandText, 16, 1.6);
            icon.Margin = new Thickness(0, 0, 8, 0);
            head.Children.Add(icon);
            head.Children.Add(Ui.Text(title, Ui.Type.BodySmall, Ui.Palette.Ink, FontWeights.SemiBold));
            body.Children.Add(head);
            TextBlock note = Ui.Text(detail, Ui.Type.Caption, Ui.Palette.InkMuted);
            note.TextWrapping = TextWrapping.Wrap;
            note.TextTrimming = TextTrimming.None;
            note.Margin = new Thickness(0, 6, 0, 0);
            body.Children.Add(note);
            var card = new Button
            {
                Content = body,
                Width = 226,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(14),
                Margin = new Thickness(0, 0, 10, 10),
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1)
            };
            Ui.Style(card, Ui.ButtonKind.Secondary);
            card.Click += delegate
            {
                _chatTaskMode = mode;
                _intent.Text = prompt;
                _intent.Focus();
                _intent.CaretIndex = _intent.Text.Length;
            };
            return card;
        }

        // ------------------------------------------------------------ composer

        private FrameworkElement BuildComposer()
        {
            var area = new Grid
            {
                MaxWidth = 820,
                Margin = new Thickness(28, 0, 28, 14),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            var stack = new StackPanel();
            area.Children.Add(stack);

            _contextNotice = Ui.Text("", Ui.Type.Meta, Ui.Palette.InkMuted);
            _contextNotice.TextWrapping = TextWrapping.Wrap;
            _contextNotice.TextTrimming = TextTrimming.None;
            _contextNotice.Margin = new Thickness(2, 0, 2, 7);
            _contextNotice.Visibility = Visibility.Collapsed;
            stack.Children.Add(_contextNotice);

            _composerCard = new Border
            {
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.LineStrong,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(12, 10, 12, 10)
            };
            stack.Children.Add(_composerCard);
            var inner = new StackPanel();
            _composerCard.Child = inner;

            Detach(_imageList);
            _imageList.Margin = new Thickness(0, 0, 0, 4);
            inner.Children.Add(_imageList);

            var inputHost = new Grid { MinHeight = 52 };
            inner.Children.Add(inputHost);
            Detach(_intent);
            _intent.Height = 52;
            _intent.MinHeight = 48;
            _intent.Template = Ui.PlainTextBoxTemplate();
            _intent.Background = Brushes.Transparent;
            _intent.BorderThickness = new Thickness(0);
            _intent.Foreground = Ui.Palette.Ink;
            _intent.FontSize = Ui.Type.Body;
            _intent.VerticalContentAlignment = VerticalAlignment.Top;
            _intent.Padding = new Thickness(2, 6, 2, 6);
            _intent.ToolTip = "输入你的要求；可直接 Ctrl+V 粘贴截图，或拖入图片";
            inputHost.Children.Add(_intent);
            _inputHint = Ui.Text("输入消息…", Ui.Type.Body, Ui.Palette.InkFaint);
            _inputHint.Margin = new Thickness(6, 8, 0, 0);
            _inputHint.VerticalAlignment = VerticalAlignment.Top;
            _inputHint.IsHitTestVisible = false;
            inputHost.Children.Add(_inputHint);
            _intent.TextChanged += delegate
            { _inputHint.Visibility = _intent.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; };
            _inputHint.Visibility = _intent.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var toolbar = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            inner.Children.Add(toolbar);
            var sendGroup = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(sendGroup, Dock.Right);
            toolbar.Children.Add(sendGroup);

            Detach(_stop);
            _stop.Content = "停止";
            _stop.Height = 34;
            _stop.MinHeight = 34;
            _stop.MinWidth = 0;
            _stop.Padding = new Thickness(12, 0, 12, 0);
            _stop.Margin = new Thickness(0, 0, 8, 0);
            _stop.Background = Ui.Palette.Surface;
            _stop.Foreground = Ui.Palette.InkMuted;
            _stop.BorderBrush = Ui.Palette.Line;
            _stop.BorderThickness = new Thickness(1);
            Ui.Style(_stop, Ui.ButtonKind.Secondary);
            sendGroup.Children.Add(_stop);

            Detach(_generate);
            _generate.Content = Ui.Glyph(Icons.Send, Ui.Palette.OnBrand, 17, 1.8);
            _generate.Width = 38;
            _generate.Height = 38;
            _generate.MinWidth = 0;
            _generate.MinHeight = 38;
            _generate.Padding = new Thickness(0);
            _generate.Background = Ui.Palette.Brand;
            _generate.Foreground = Ui.Palette.OnBrand;
            _generate.BorderBrush = Ui.Palette.Brand;
            _generate.BorderThickness = new Thickness(0);
            _generate.ToolTip = "发送（Enter）· Shift+Enter 换行";
            Ui.Style(_generate, Ui.ButtonKind.Primary);
            AutomationProperties.SetName(_generate, "发送");
            sendGroup.Children.Add(_generate);

            var languageRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            toolbar.Children.Add(languageRow);
            TextBlock languageLabel = Ui.Text("回复语言", Ui.Type.Meta, Ui.Palette.InkMuted);
            languageLabel.VerticalAlignment = VerticalAlignment.Center;
            languageLabel.Margin = new Thickness(0, 0, 8, 0);
            languageRow.Children.Add(languageLabel);
            Detach(_language);
            _language.Width = 132;
            _language.Height = 34;
            _language.MinHeight = 34;
            _language.FontSize = Ui.Type.BodySmall;
            _language.Margin = new Thickness(0, 0, 12, 0);
            languageRow.Children.Add(_language);
            TextBlock pasteTip = Ui.Text("Ctrl+V 粘贴截图", Ui.Type.Caption, Ui.Palette.InkFaint);
            pasteTip.VerticalAlignment = VerticalAlignment.Center;
            languageRow.Children.Add(pasteTip);
            Detach(_adviceOnly);

            Detach(_status);
            _status.FontSize = Ui.Type.Meta;
            _status.Foreground = Ui.Palette.InkMuted;
            _status.Margin = new Thickness(4, 8, 4, 0);
            _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible;
            var statusDescriptor = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(
                TextBlock.TextProperty, typeof(TextBlock));
            EventHandler statusChanged = delegate
            { _status.Visibility = string.IsNullOrEmpty(_status.Text) ? Visibility.Collapsed : Visibility.Visible; };
            statusDescriptor.AddValueChanged(_status, statusChanged);
            Closed += delegate { statusDescriptor.RemoveValueChanged(_status, statusChanged); };
            stack.Children.Add(_status);

            _intent.GotKeyboardFocus += delegate
            { if (!_dragHighlight) _composerCard.BorderBrush = Ui.Palette.Brand; };
            _intent.LostKeyboardFocus += delegate
            { if (!_dragHighlight) _composerCard.BorderBrush = Ui.Palette.LineStrong; };
            AttachComposerInput();
            return area;
        }

        private void AttachComposerInput()
        {
            _intent.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
                // WPF leaves the built-in Paste command disabled while the clipboard holds
                // no text, so a screenshot never reaches the box through the normal path.
                // Read the image first and only swallow Ctrl+V when there really is one.
                e.Handled = PasteClipboardImage();
            };

            var pasteMenu = new ContextMenu();
            var pasteText = new MenuItem { Header = "粘贴" };
            pasteText.Click += delegate { try { _intent.Paste(); } catch { } };
            var pasteImage = new MenuItem { Header = "粘贴图片" };
            pasteImage.Click += delegate { PasteClipboardImage(); };
            var clearInput = new MenuItem { Header = "清空输入" };
            clearInput.Click += delegate { _intent.Clear(); };
            pasteMenu.Items.Add(pasteText);
            pasteMenu.Items.Add(pasteImage);
            pasteMenu.Items.Add(new Separator());
            pasteMenu.Items.Add(clearInput);
            pasteMenu.Opened += delegate
            {
                pasteText.IsEnabled = Clipboard.ContainsText();
                pasteImage.IsEnabled = HasClipboardImage();
                clearInput.IsEnabled = _intent.Text.Length > 0;
            };
            _intent.ContextMenu = pasteMenu;

            _intent.PreviewDragOver += delegate(object sender, DragEventArgs e)
            {
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null)
                {
                    e.Handled = HasDroppedImage(e.Data);
                    if (e.Handled) e.Effects = DragDropEffects.Copy;
                    HighlightComposer(e.Handled);
                    return;
                }
                e.Handled = true;
                bool images = paths.Any(IsImagePath);
                e.Effects = images ? DragDropEffects.Copy : DragDropEffects.None;
                HighlightComposer(images);
            };
            _intent.PreviewDragLeave += delegate { HighlightComposer(false); };
            _intent.PreviewDrop += delegate(object sender, DragEventArgs e)
            {
                HighlightComposer(false);
                if (HasDroppedImage(e.Data))
                { e.Handled = true; PasteImageData(e.Data); return; }
                string[] paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                if (paths.Any(path => !IsImagePath(path)))
                    _status.Text = "仅支持图片；文档不会添加或发送。";
                paths = paths.Where(IsImagePath).ToArray();
                if (paths.Length == 0) { e.Handled = true; return; }
                e.Handled = true;
                AddImagePaths(paths);
            };
        }

        private void HighlightComposer(bool on)
        {
            if (_composerCard == null || _dragHighlight == on) return;
            _dragHighlight = on;
            _composerCard.BorderBrush = on ? Ui.Palette.Brand : Ui.Palette.LineStrong;
            _composerCard.Background = on ? Ui.Palette.BrandTint : Ui.Palette.Surface;
        }

        private static bool HasClipboardImage()
        {
            try
            {
                IDataObject data = Clipboard.GetDataObject();
                return data != null && ClipboardImages.HasImage(data);
            }
            catch { return false; }
        }

        private Button QuickPrompt(string label, string prompt, string mode)
        {
            return Action(label, delegate
            {
                _chatTaskMode = mode;
                _intent.Text = prompt;
                _intent.Focus();
                _intent.CaretIndex = _intent.Text.Length;
            });
        }

        private static bool IsImagePath(string path)
        {
            string extension = Path.GetExtension(path ?? "").ToLowerInvariant();
            return extension == ".png" || extension == ".jpg" || extension == ".jpeg";
        }

        // ------------------------------------------------------------ messages

        private Border ChatBubble(string text, bool user, byte[][] images)
        {
            var body = new StackPanel();
            TextBlock label = Ui.Text(text ?? "", Ui.Type.Body, Ui.Palette.Ink);
            label.TextWrapping = TextWrapping.Wrap;
            label.TextTrimming = TextTrimming.None;
            body.Children.Add(label);
            if (images != null && images.Length > 0)
            {
                var strip = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                foreach (byte[] bytes in images)
                {
                    byte[] captured = bytes;
                    var thumbnail = new Image
                    {
                        Source = PreviewSource(bytes),
                        Width = 84,
                        Height = 84,
                        Stretch = Stretch.UniformToFill,
                        Cursor = Cursors.Hand
                    };
                    thumbnail.Clip = new RectangleGeometry(new Rect(0, 0, 84, 84), 10, 10);
                    var frame = new Border
                    {
                        Background = Ui.Palette.SurfaceSubtle,
                        CornerRadius = new CornerRadius(10),
                        Margin = new Thickness(0, 0, 6, 6),
                        Child = thumbnail,
                        ToolTip = "查看图片"
                    };
                    thumbnail.MouseLeftButtonUp += delegate { ShowStoredImage(captured); };
                    strip.Children.Add(frame);
                }
                body.Children.Add(strip);
            }
            return new Border
            {
                Background = user ? Ui.Palette.BrandTint : Brushes.Transparent,
                CornerRadius = new CornerRadius(14, 14, 4, 14),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(96, 0, 0, 14),
                HorizontalAlignment = HorizontalAlignment.Right,
                MaxWidth = 540,
                Child = body
            };
        }

        private FrameworkElement AssistantBubble(string reply, string advice)
        {
            var grid = new Grid { Margin = new Thickness(0, 0, 0, 18) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.Children.Add(Avatar());
            var body = new StackPanel();
            body.Children.Add(ChatReadingView.Create(reply));
            if (!string.IsNullOrWhiteSpace(advice))
            {
                TextBlock note = Ui.Text("待确认 / 建议：" + advice, Ui.Type.Meta, Ui.Palette.InkMuted);
                note.TextWrapping = TextWrapping.Wrap;
                note.TextTrimming = TextTrimming.None;
                note.Margin = new Thickness(0, 6, 0, 0);
                body.Children.Add(note);
            }
            Grid.SetColumn(body, 1);
            grid.Children.Add(body);
            return grid;
        }

        private void UpdateChatSubtitle()
        {
            if (_chatSubtitle == null) return;
            int turns = _turns == null ? 0 : _turns.Count;
            string model = _modelSummary == null ? "" : (_modelSummary.Text ?? "").Trim();
            _chatSubtitle.Text = string.IsNullOrEmpty(model)
                ? (turns == 0 ? "新会话" : turns + " 轮对话")
                : model + " · " + (turns == 0 ? "新会话" : turns + " 轮对话");
        }

        private void UpdateEmptyState()
        {
            if (_emptyState == null) return;
            bool empty = _turns.Count == 0 &&
                string.IsNullOrWhiteSpace(_background.Text) &&
                string.IsNullOrWhiteSpace(_result.Text) &&
                string.IsNullOrWhiteSpace(_advice.Text);
            _emptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        }

        // -------------------------------------------------------------- tables

        private void RefreshInquiry()
        {
            if (_inquiryPanel == null) return;
            _inquiryPanel.Children.Clear();
            InquiryField[] fields = _lastResult == null ? null : _lastResult.InquiryFields;
            string[] missing = _lastResult == null ? null : _lastResult.MissingFields;
            if ((fields == null || fields.Length == 0) && (missing == null || missing.Length == 0))
            { _inquiryPanel.Visibility = Visibility.Collapsed; return; }
            _inquiryPanel.Visibility = Visibility.Visible;
            var card = Ui.Card(new StackPanel());
            var body = (StackPanel)card.Child;
            var caption = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
            caption.Children.Add(Ui.Text("询盘整理", Ui.Type.BodySmall, Ui.Palette.Ink, FontWeights.SemiBold));
            TextBlock tip = Ui.Text("请核对原材料", Ui.Type.Caption, Ui.Palette.InkMuted);
            tip.VerticalAlignment = VerticalAlignment.Center;
            tip.Margin = new Thickness(8, 1, 0, 0);
            caption.Children.Add(tip);
            body.Children.Add(caption);
            foreach (InquiryField field in fields ?? new InquiryField[0])
            {
                var row = new Grid { Margin = new Thickness(0) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(132) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.Children.Add(new TextBlock
                {
                    Text = field.Field,
                    Foreground = Ui.Palette.InkMuted,
                    FontSize = Ui.Type.BodySmall,
                    Padding = new Thickness(0, 6, 8, 6)
                });
                var value = new TextBlock
                {
                    Text = field.Value,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Ui.Palette.Ink,
                    FontSize = Ui.Type.BodySmall,
                    Padding = new Thickness(0, 6, 0, 6)
                };
                Grid.SetColumn(value, 1);
                row.Children.Add(value);
                body.Children.Add(Ui.Divider());
                body.Children.Add(row);
            }
            if (missing != null && missing.Length > 0)
            {
                TextBlock warn = Ui.Text("待确认：" + string.Join("、", missing),
                    Ui.Type.BodySmall, Ui.Palette.Warning);
                warn.TextWrapping = TextWrapping.Wrap;
                warn.TextTrimming = TextTrimming.None;
                warn.Margin = new Thickness(0, 8, 0, 0);
                body.Children.Add(warn);
            }
            _inquiryPanel.Children.Add(card);
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
                QuietIcon(_editAnswer,
                    _editingAnswer ? Icons.Check : Icons.Edit,
                    _editingAnswer ? "完成编辑" : "编辑回复");
            }
            _chatResultCard.Visibility = !string.IsNullOrWhiteSpace(_result.Text) ||
                !string.IsNullOrWhiteSpace(_advice.Text) ? Visibility.Visible : Visibility.Collapsed;
            if (_detailsExpander != null && !string.IsNullOrWhiteSpace(_calculation.Text))
                _detailsExpander.IsExpanded = true;
            UpdateEmptyState();
        }

        private void RebuildChatHistory()
        {
            if (!_chatLayout) return;
            _conversationHistory.Children.Clear();
            if (!string.IsNullOrWhiteSpace(_background.Text))
                _conversationHistory.Children.Add(ChatBubble(_background.Text, true, null));
            for (int i = 0; i < _turns.Count; i++)
            {
                if ((_session.TopicBreaks ?? new int[0]).Contains(i)) AddTopicDivider();
                CommunicationTurn turn = _turns[i];
                _conversationHistory.Children.Add(ChatBubble(turn.Instruction ?? "", true, turn.Images));
                if (i < _turns.Count - 1 || _lastResult == null)
                    _conversationHistory.Children.Add(AssistantBubble(turn.Reply ?? "", turn.Advice));
            }
            if ((_session.TopicBreaks ?? new int[0]).Contains(_turns.Count)) AddTopicDivider();
            _conversationHistory.Visibility = _turns.Count > 0 || !string.IsNullOrWhiteSpace(_background.Text)
                ? Visibility.Visible : Visibility.Collapsed;
            UpdateChatSubtitle();
            UpdateEmptyState();
        }

        private void AddTopicDivider()
        {
            var row = new DockPanel { Margin = new Thickness(0, 18, 0, 18) };
            var left = Ui.Divider();
            left.VerticalAlignment = VerticalAlignment.Center;
            left.Margin = new Thickness(0, 0, 10, 0);
            var label = Ui.Text("新话题", Ui.Type.Caption, Ui.Palette.InkMuted);
            label.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(label, Dock.Right);
            row.Children.Add(label);
            row.Children.Add(left);
            _conversationHistory.Children.Add(row);
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
            new Window
            {
                Owner = this,
                Title = "图片",
                Width = Math.Min(900, SystemParameters.WorkArea.Width - 40),
                Height = Math.Min(700, SystemParameters.WorkArea.Height - 40),
                Content = new ScrollViewer
                { Content = new Image { Source = PreviewSource(bytes, 0), Stretch = Stretch.Uniform } }
            }.ShowDialog();
        }

        private static void QuietIcon(Button button, Geometry glyph, string label)
        {
            button.Content = Ui.Glyph(glyph, Ui.Palette.InkMuted, 16, 1.5);
            button.Width = 32;
            button.Height = 32;
            button.MinWidth = 0;
            button.MinHeight = 32;
            button.Padding = new Thickness(7);
            button.Background = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.BorderBrush = Brushes.Transparent;
            button.Foreground = Ui.Palette.InkMuted;
            button.ToolTip = label;
            Ui.Style(button, Ui.ButtonKind.Icon);
            AutomationProperties.SetName(button, label);
        }

        private static void QuietIcon(Button button, string geometry, string label)
        {
            QuietIcon(button, Geometry.Parse(geometry), label);
        }

        private void UpdateChatWidth()
        {
            if (_chatSidebar == null) return;
            bool narrow = ActualWidth > 0 && ActualWidth < 900;
            if (narrow != _wasNarrow) { _sidebarOpen = !narrow; _wasNarrow = narrow; }
            _chatSidebar.Visibility = _sidebarOpen ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(_chatSidebar, narrow ? 1 : 0);
            _chatSidebar.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Stretch;
            _chatSidebar.Width = narrow ? 240 : double.NaN;
            Panel.SetZIndex(_chatSidebar, narrow ? 2 : 0);
            ((Grid)Content).ColumnDefinitions[0].Width = new GridLength(narrow || !_sidebarOpen ? 0 : 264);
        }

        // ------------------------------------------------------------ sessions

        private static string RelativeTime(DateTime value)
        {
            TimeSpan span = DateTime.Now - value;
            if (span.TotalMinutes < 1) return "刚刚";
            if (span.TotalHours < 1) return Math.Max(1, (int)span.TotalMinutes) + " 分钟前";
            if (value.Date == DateTime.Today) return value.ToString("HH:mm");
            if (value.Date == DateTime.Today.AddDays(-1)) return "昨天 " + value.ToString("HH:mm");
            return value.ToString("MM-dd");
        }

        private void RefreshSessionList()
        {
            if (_chatSessions == null) return;
            _refreshingSessions = true;
            string search = _chatSearch == null ? "" : _chatSearch.Text.Trim();
            _chatSessions.Items.Clear();
            var rows = new List<SessionRow>();
            foreach (string path in ConversationStore.Files())
                try
                {
                    SavedConversation item = ConversationStore.Load(path);
                    if (search.Length > 0 &&
                        item.Title.IndexOf(search, StringComparison.CurrentCultureIgnoreCase) < 0) continue;
                    int turns = item.Turns == null ? 0 : item.Turns.Length;
                    rows.Add(new SessionRow
                    {
                        Item = item,
                        Title = item.Title,
                        Meta = RelativeTime(File.GetLastWriteTime(path)) +
                               (turns > 0 ? " · " + turns + " 轮" : "")
                    });
                }
                catch { /* The unreadable file remains available for recovery. */ }
            rows = rows.OrderByDescending(row => File.GetLastWriteTime(
                Path.Combine(ConversationStore.DirectoryPath, row.Item.Id + ".bin"))).ToList();
            foreach (SessionRow row in rows) _chatSessions.Items.Add(row);
            foreach (SessionRow row in _chatSessions.Items)
                if (row.Item.Id == _session.Id) { _chatSessions.SelectedItem = row; break; }
            if (_chatTitle != null) _chatTitle.Text = _session.Title;
            UpdateChatSubtitle();
            UpdateEmptyState();
            _refreshingSessions = false;
        }

        private void OpenSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SessionRow;
            if (selected == null || selected.Item.Id == _session.Id || !SaveConversation()) return;
            RestoreConversation(selected.Item);
            RefreshSessionList();
        }

        private void RenameSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SessionRow;
            if (selected == null) return;
            SavedConversation item = selected.Item;
            var text = new TextBox { Text = item.Title, Margin = new Thickness(14), MaxLength = 80 };
            var prompt = new Window
            {
                Owner = this,
                Width = 340,
                Height = 120,
                Title = "重命名会话",
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = text
            };
            Ui.ApplyWindow(prompt);
            text.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Enter || string.IsNullOrWhiteSpace(text.Text)) return;
                string old = item.Title;
                item.Title = text.Text.Trim();
                try
                {
                    ConversationStore.Save(item);
                    if (item.Id == _session.Id) _session.Title = item.Title;
                    prompt.Close();
                    RefreshSessionList();
                }
                catch { item.Title = old; _status.Text = "重命名保存失败。"; }
            };
            prompt.ShowDialog();
        }

        private void DeleteSelectedConversation()
        {
            var selected = _chatSessions.SelectedItem as SessionRow;
            if (selected == null) return;
            if (MessageBox.Show(this, "删除此会话及其材料？", "删除会话", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            try
            {
                ConversationStore.Delete(selected.Item.Id);
                if (selected.Item.Id == _session.Id) RestoreConversation(new SavedConversation());
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

        // Drag data reaches us from arbitrary apps; never let a probing failure
        // escape into the drag event.
        private static bool HasDroppedImage(IDataObject data)
        {
            try { return ClipboardImages.HasImage(data); }
            catch { return false; }
        }

        // Returns true when Ctrl+V was consumed as an image paste; false lets the
        // TextBox paste text exactly as before.
        private bool PasteClipboardImage()
        {
            IDataObject data = null;
            for (int attempt = 0; attempt < 3 && data == null; attempt++)
            {
                try { data = Clipboard.GetDataObject(); }
                catch (System.Runtime.InteropServices.ExternalException)
                { if (attempt < 2) System.Threading.Thread.Sleep(40); }
            }
            if (data == null)
            { _status.Text = "剪贴板正忙，请稍后重新粘贴。"; return true; }
            try
            {
                if (ClipboardImages.HasImage(data)) { PasteImageData(data); return true; }
                if (!data.GetDataPresent(DataFormats.FileDrop)) return false;
                string[] copied = data.GetData(DataFormats.FileDrop) as string[];
                if (copied == null || copied.Length == 0) return false;
                string[] paths = copied.Where(IsImagePath).ToArray();
                if (paths.Length == 0)
                { _status.Text = "仅支持图片；请复制截图或 PNG/JPEG 图片。"; return true; }
                if (paths.Length != copied.Length)
                    _status.Text = "文档不会添加；已忽略非图片文件。";
                AddImagePaths(paths);
                return true;
            }
            catch
            { _status.Text = "剪贴板读取失败，请重新复制图片。"; return true; }
        }
    }
}
