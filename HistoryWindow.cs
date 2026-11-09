using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace GlobalTranslator
{
    internal sealed class HistoryWindow : Window
    {
        internal static Button ActionButton(string label, Action action)
        {
            var button = new Button
            {
                Content = label,
                Margin = new Thickness(0, 6, 8, 0),
                Padding = new Thickness(12, 5, 12, 5)
            };
            button.Click += delegate { action(); };
            return button;
        }

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
            actions.Children.Add(HistoryWindow.ActionButton("查看 / 复制 / 再次翻译", open));
            actions.Children.Add(HistoryWindow.ActionButton("清空记录", delegate
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
            footer.Children.Add(HistoryWindow.ActionButton("复制译文", delegate
            {
                try { Clipboard.SetText(result.Text); }
                catch { status.Text = "剪贴板正忙，请重试。"; }
            }));
            footer.Children.Add(HistoryWindow.ActionButton("重新翻译", async delegate
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
