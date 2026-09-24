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
        private readonly WrapPanel _imageList = new WrapPanel();
        private readonly TextBlock _status = new TextBlock
            { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        private readonly ComboBox _language = new ComboBox
            { Width = 160, Margin = new Thickness(0, 0, 8, 0) };
        private readonly Button _copy;
        private readonly Button _generate;
        private readonly Button _adviceOnly;
        private CancellationTokenSource _request;
        private CommunicationResult _lastResult;
        private int _successful;
        private bool _composing;
        private bool _loading;
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

            var root = new DockPanel { Margin = new Thickness(14) };
            Content = root;
            var footer = new StackPanel();
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);
            footer.Children.Add(_status);
            var actions = new WrapPanel();
            footer.Children.Add(actions);
            _generate = Action("生成回复", async delegate { await Generate(false); });
            _adviceOnly = Action("仅给建议", async delegate { await Generate(true); });
            actions.Children.Add(_generate);
            actions.Children.Add(_adviceOnly);
            actions.Children.Add(Action("取消", CancelRequest));
            _copy = Action("复制回复", CopyReply);
            _copy.IsEnabled = false;
            actions.Children.Add(_copy);
            actions.Children.Add(Action("新建沟通", NewConversation));
            actions.Children.Add(Action("AI 设置", delegate
                { if (_openModelSettings != null) _openModelSettings(); }));

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
            };
            root.Children.Add(scroll);
            var content = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            scroll.Content = content;
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
            content.Children.Add(Label("继续调整（生成后可用）"));
            content.Children.Add(_adjustment);
            _adjustment.ToolTip = "例如“短一点”“不要提价格”。Enter 提交，Shift+Enter 换行";
            content.Children.Add(Label("可发送的回复 · 可编辑"));
            content.Children.Add(_result);
            content.Children.Add(Label("中文意思"));
            _meaning.IsReadOnly = true;
            content.Children.Add(_meaning);
            content.Children.Add(Label("沟通建议 / 待确认事项"));
            _advice.IsReadOnly = true;
            content.Children.Add(_advice);

            _background.TextChanged += delegate { OnMaterialChanged(); };
            _intent.TextChanged += delegate { OnMaterialChanged(); };
            _adjustment.TextChanged += delegate
                { if (!_loading && _request != null) CancelRequest(); };
            _result.TextChanged += delegate
                { _copy.IsEnabled = !string.IsNullOrWhiteSpace(_result.Text); };
            PreviewKeyDown += HandleKeyDown;
            TextCompositionManager.AddPreviewTextInputStartHandler(this,
                delegate { _composing = true; });
            TextCompositionManager.AddTextInputHandler(this,
                delegate { _composing = false; });
            Loaded += delegate { FitToWorkArea(); _intent.Focus(); };
            Closed += delegate { CancelRequest(); };
            _loading = false;
        }

        private void FitToWorkArea()
        {
            Rect area = SystemParameters.WorkArea;
            double maxWidth = Math.Max(360, area.Width - 24);
            double maxHeight = Math.Max(360, area.Height - 24);
            MinWidth = Math.Min(540, maxWidth);
            MinHeight = Math.Min(380, maxHeight);
            MaxWidth = maxWidth;
            MaxHeight = maxHeight;
            Width = Math.Min(800, maxWidth);
            Height = Math.Min(790, maxHeight);
        }

        private static TextBox Editor(double height)
        {
            return new TextBox
            {
                Height = height, MinHeight = 40, AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Padding = new Thickness(8), FontSize = 14
            };
        }

        private static TextBlock Label(string text)
        {
            return new TextBlock
                { Text = text, Margin = new Thickness(0, 8, 0, 4), FontWeight = FontWeights.SemiBold };
        }

        internal static Button Action(string label, Action action)
        {
            var button = new Button
                { Content = label, Padding = new Thickness(9, 5, 9, 5),
                  Margin = new Thickness(0, 2, 6, 2), MinHeight = 30 };
            button.Click += delegate { action(); };
            return button;
        }

        private void HandleKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { _composing = false; return; }
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
            }
        }

        private async Task Generate(bool adviceOnly)
        {
            if (_request != null) return;
            if (_successful >= 10)
            {
                _status.Text = "本次沟通已生成 10 次，请点“新建沟通”。";
                return;
            }
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
            var turns = _turns.Select(t => new CommunicationTurn
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
                Turns = turns
            };
            var pending = new CancellationTokenSource();
            _request = pending;
            _generate.IsEnabled = false;
            _adviceOnly.IsEnabled = false;
            _status.Text = "正在分析沟通内容…";
            try
            {
                _settings.CommunicationLanguage = input.Language;
                _settings.Save();
                CommunicationResult answer = await _client.ComposeCommunicationAsync(
                    input, _settings, pending.Token);
                if (_request != pending || pending.IsCancellationRequested) return;
                _lastResult = answer;
                _result.Text = answer.Reply;
                _meaning.Text = answer.MeaningZh;
                _advice.Text = answer.AdviceZh;
                _turns.Add(new CommunicationTurn
                    { Instruction = input.Adjustment, Reply = answer.Reply });
                _successful++;
                _loading = true;
                _adjustment.Clear();
                _loading = false;
                _status.Text = answer.Reply.Length > 0
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
        }

        private void CopyReply()
        {
            if (string.IsNullOrWhiteSpace(_result.Text)) return;
            try { Clipboard.SetText(_result.Text); }
            catch { _status.Text = "剪贴板正忙，请重试。"; }
        }

        private void NewConversation()
        {
            CancelRequest();
            _loading = true;
            _background.Clear(); _intent.Clear(); _adjustment.Clear();
            _result.Clear(); _meaning.Clear(); _advice.Clear();
            _images.Clear(); _turns.Clear();
            _lastResult = null; _successful = 0;
            RefreshImages();
            _status.Text = "已新建沟通。";
            _loading = false;
            _intent.Focus();
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
