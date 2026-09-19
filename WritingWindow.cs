using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace GlobalTranslator
{
    internal sealed class WritingWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly TranslationClient _client;
        private readonly TextBox _source = Editor();
        private readonly TextBox _result = Editor();
        private readonly TextBox _requirements = new TextBox { Height = 55, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        private readonly TextBlock _status = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 8) };
        private readonly ComboBox _language = new ComboBox { Width = 125, Margin = new Thickness(0, 0, 8, 0) };
        private readonly CheckBox _rewrite = new CheckBox { Content = "允许适当改写（不增加事实）", Margin = new Thickness(0, 8, 0, 8) };
        private readonly Button _copy;
        private CancellationTokenSource _request;
        private string _beforeJoin;
        private static readonly string[] Codes = { "en", "ja", "ko", "de", "fr", "es", "ru", "ar", "pt", "zh-Hans" };

        public WritingWindow(AppSettings settings, TranslationClient client, HistoryEntry entry)
        {
            _settings = settings; _client = client;
            Title = "鲨译 · 中译外表达";
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            ResizeMode = ResizeMode.CanResize;
            FitToWorkArea();
            Background = new SolidColorBrush(Color.FromRgb(245, 250, 253));
            var root = new DockPanel { Margin = new Thickness(18) }; Content = root;
            var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            var row = new WrapPanel(); top.Children.Add(row);
            row.Children.Add(new TextBlock { Text = "目标语言", Margin = new Thickness(0, 4, 10, 0) });
            foreach (string label in new[] { "英语", "日语", "韩语", "德语", "法语", "西班牙语", "俄语", "阿拉伯语", "葡萄牙语", "简体中文" }) _language.Items.Add(label);
            _language.SelectedIndex = Math.Max(0, Array.IndexOf(Codes, settings.WritingTargetLanguage));
            _language.SelectionChanged += delegate { Invalidate(); };
            row.Children.Add(_language);
            row.Children.Add(Action("整理断行", delegate { _beforeJoin = _source.Text; _source.Text = TextTools.JoinLines(_source.Text); }));
            row.Children.Add(Action("还原整理", delegate { if (_beforeJoin != null) { _source.Text = _beforeJoin; _beforeJoin = null; } }));
            var options = new StackPanel();
            options.Children.Add(_requirements);
            var presets = new WrapPanel(); options.Children.Add(presets);
            foreach (string preset in new[] { "商务邮件", "聊天简洁", "礼貌委婉", "正式专业" })
            {
                string value = preset;
                presets.Children.Add(Action(value, delegate { _requirements.Text = value; }));
            }
            options.Children.Add(_rewrite);
            top.Children.Add(new Expander { Header = "表达要求（可选，例如：给老客户，委婉催交期，不作额外承诺）", Content = options, Margin = new Thickness(0, 10, 0, 8), IsExpanded = true });
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            footer.Children.Add(_status);
            var actions = new WrapPanel(); footer.Children.Add(actions);
            actions.Children.Add(Action("翻译 / 重新翻译", async delegate { await Translate(); }));
            actions.Children.Add(Action("取消", delegate { Invalidate(); _status.Text = "已取消"; }));
            _copy = Action("复制译文", delegate { if (!string.IsNullOrWhiteSpace(_result.Text)) try { Clipboard.SetText(_result.Text); } catch { _status.Text = "剪贴板正忙，请重试。"; } });
            _copy.IsEnabled = false; actions.Children.Add(_copy);
            var body = new Grid(); body.RowDefinitions.Add(new RowDefinition()); body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); body.RowDefinitions.Add(new RowDefinition());
            body.Children.Add(new GroupBox { Header = "原文 · 可编辑 / 粘贴", Content = _source, Padding = new Thickness(4) });
            var splitter = new GridSplitter { Height = 8, HorizontalAlignment = HorizontalAlignment.Stretch }; Grid.SetRow(splitter, 1); body.Children.Add(splitter);
            var resultGroup = new GroupBox { Header = "译文 · 核对后复制", Content = _result, Padding = new Thickness(4) };
            Grid.SetRow(resultGroup, 2); body.Children.Add(resultGroup); root.Children.Add(body);
            _source.ToolTip = "填写中文原意，也可粘贴 PDF 文本。按 Enter 翻译，Shift+Enter 换行"; _result.ToolTip = "译文可编辑，核对后复制";
            _source.TextChanged += delegate { Invalidate(); };
            _requirements.TextChanged += delegate { Invalidate(); };
            _rewrite.Checked += delegate { Invalidate(); }; _rewrite.Unchecked += delegate { Invalidate(); };
            _result.TextChanged += delegate { if (_request == null) { _copy.IsEnabled = !string.IsNullOrWhiteSpace(_result.Text); _status.Text = TextTools.Check(_source.Text, _result.Text); } };
            Closed += delegate { Invalidate(); };
            PreviewKeyDown += async delegate(object sender, System.Windows.Input.KeyEventArgs e)
            {
                bool controlEnter =
                    e.Key == System.Windows.Input.Key.Enter &&
                    System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control;
                bool sourceEnter =
                    e.Key == System.Windows.Input.Key.Enter &&
                    System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None &&
                    ReferenceEquals(System.Windows.Input.Keyboard.FocusedElement, _source);
                if (controlEnter || sourceEnter)
                { e.Handled = true; await Translate(); }
            };
            if (entry != null)
            {
                _source.Text = entry.Source; _requirements.Text = entry.Requirements; _rewrite.IsChecked = entry.Rewrite;
                int index = Array.IndexOf(Codes, entry.Target); if (index >= 0) _language.SelectedIndex = index;
                _result.Text = entry.Translation;
            }
            Loaded += delegate { FitToWorkArea(); };
        }

        private void FitToWorkArea()
        {
            Rect area = SystemParameters.WorkArea;
            double maxWidth = Math.Max(360, area.Width - 24);
            double maxHeight = Math.Max(360, area.Height - 24);
            MinWidth = Math.Min(540, maxWidth);
            MinHeight = Math.Min(480, maxHeight);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            Width = Math.Min(760, maxWidth);
            Height = Math.Min(720, maxHeight);
        }
        private void Invalidate()
        {
            if (_request != null) { _request.Cancel(); _request = null; }
            if (_copy != null) _copy.IsEnabled = false;
            _result.Clear(); _status.Text = "";
        }
        private async System.Threading.Tasks.Task Translate()
        {
            Invalidate();
            if (string.IsNullOrWhiteSpace(_source.Text)) { _status.Text = "请先填写原文。"; return; }
            var request = new CancellationTokenSource(); _request = request;
            string source = _source.Text;
            var snapshot = new AppSettings();
            foreach (var field in typeof(AppSettings).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public)) field.SetValue(snapshot, field.GetValue(_settings));
            snapshot.TargetLanguageMode = "Fixed"; snapshot.TargetLanguage = Codes[_language.SelectedIndex];
            _settings.WritingTargetLanguage = snapshot.TargetLanguage;
            _status.Text = "正在翻译…";
            try
            {
                _settings.Save();
                var result = await _client.TranslateWithRequirementsAsync(source, snapshot, request.Token, null, _requirements.Text, _rewrite.IsChecked == true);
                if (_request != request || request.IsCancellationRequested) return;
                _request = null; _result.Text = result.Text; _copy.IsEnabled = true;
                string warning = TextTools.Check(source, result.Text);
                _status.Text = warning.Length > 0 ? warning : "翻译完成 · " + result.Provider;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { if (_request == request) { _result.Clear(); _copy.IsEnabled = false; _status.Text = ex.Message + " 可点击翻译重试。"; } }
            finally { if (_request == request) _request = null; request.Dispose(); }
        }
        private static TextBox Editor() { return new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontSize = 16, Padding = new Thickness(10) }; }
        internal static Button Action(string label, Action action)
        {
            var b = new Button { Content = label, Padding = new Thickness(10, 5, 10, 5), Margin = new Thickness(0, 3, 7, 3) }; b.Click += delegate { action(); }; return b;
        }
    }

    internal sealed class HistoryWindow : Window
    {
        public HistoryWindow(AppSettings settings, TranslationClient client)
        {
            Title = "鲨译 · 最近翻译（本次会话，最多100条）"; Width = 650; Height = 480; MinWidth = 420; MinHeight = 300;
            var root = new DockPanel { Margin = new Thickness(14) }; Content = root;
            var search = new TextBox { Margin = new Thickness(0, 0, 0, 8), ToolTip = "搜索原文或译文" }; DockPanel.SetDock(search, Dock.Top); root.Children.Add(search);
            var list = new ListBox();
            Action refresh = delegate { list.ItemsSource = TranslationHistory.Search(search.Text); };
            var actions = new WrapPanel(); DockPanel.SetDock(actions, Dock.Bottom); root.Children.Add(actions);
            Action open = delegate { var entry = list.SelectedItem as HistoryEntry; if (entry != null) new WritingWindow(settings, client, entry).Show(); };
            actions.Children.Add(WritingWindow.Action("查看 / 复制 / 再次翻译", open));
            actions.Children.Add(WritingWindow.Action("清空记录", delegate { TranslationHistory.Clear(); refresh(); }));
            root.Children.Add(list); search.TextChanged += delegate { refresh(); }; list.MouseDoubleClick += delegate { open(); }; Activated += delegate { refresh(); }; refresh();
        }
    }
}
