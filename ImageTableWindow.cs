using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Bitmap = System.Drawing.Bitmap;

namespace GlobalTranslator
{
    internal sealed class ImageTableWindow : Window
    {
        private readonly AppSettings _settings;
        private readonly TranslationClient _client;
        private readonly Action _consent;
        private Bitmap _image;
        private CancellationTokenSource _request;
        private readonly Image _preview = new Image { Stretch = Stretch.Uniform };
        private readonly DataGrid _grid = new DataGrid
        {
            AutoGenerateColumns = true, CanUserAddRows = false, CanUserDeleteRows = false,
            ClipboardCopyMode = DataGridClipboardCopyMode.None,
            CanUserSortColumns = false, CanUserReorderColumns = false, SelectionUnit = DataGridSelectionUnit.CellOrRowHeader,
            GridLinesVisibility = DataGridGridLinesVisibility.Horizontal, RowHeight = 34,
            AlternatingRowBackground = new SolidColorBrush(Color.FromRgb(245, 249, 251)), FontSize = 14
        };
        private readonly ComboBox _sheets = new ComboBox { MinWidth = 160, Height = 32, DisplayMemberPath = "TableName" };
        private readonly TextBlock _status = new TextBlock { Text = "添加截图或图片，识别后可逐格修改。", TextWrapping = TextWrapping.Wrap };
        private readonly Button _recognize;
        private readonly Button _copy;
        private readonly Button _save;
        private readonly Button _stop;
        private DataTable[] _tables = new DataTable[0];

        public ImageTableWindow(AppSettings settings, TranslationClient client, Action consent, Bitmap image)
        {
            _settings = settings; _client = client; _consent = consent;
            Title = "识图转表格 · Sharkey";
            Width = Math.Min(1000, SystemParameters.WorkArea.Width);
            Height = Math.Min(700, SystemParameters.WorkArea.Height);
            MinWidth = 500; MinHeight = 360;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Background = new SolidColorBrush(Color.FromRgb(245, 249, 252));
            FontFamily = new FontFamily("Microsoft YaHei UI"); FontSize = 14;
            var root = new DockPanel { Margin = new Thickness(20) }; Content = root;
            var heading = new TextBlock { Text = "识图转表格", FontSize = 22, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) };
            DockPanel.SetDock(heading, Dock.Top); root.Children.Add(heading);
            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
            actions.Children.Add(ActionButton("框选截图", async delegate { await Capture(); }));
            actions.Children.Add(ActionButton("添加图片", delegate { AddImage(); }));
            actions.Children.Add(ActionButton("粘贴图片", delegate { PasteImage(); }));
            _recognize = ActionButton("识别表格", async delegate { await Recognize(); });
            actions.Children.Add(_recognize);
            _stop = ActionButton("停止", delegate { if (_request != null) _request.Cancel(); });
            _stop.Visibility = Visibility.Collapsed; actions.Children.Add(_stop);
            DockPanel.SetDock(actions, Dock.Top); root.Children.Add(actions);
            var footer = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
            _copy = ActionButton("复制到 Excel", delegate { Export(false); });
            _save = ActionButton("导出 CSV…", delegate { Export(true); });
            _copy.IsEnabled = _save.IsEnabled = false;
            footer.Children.Add(_copy); footer.Children.Add(_save);
            DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            _status.Margin = new Thickness(0, 10, 0, 0);
            DockPanel.SetDock(_status, Dock.Bottom); root.Children.Add(_status);
            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.3, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.7, GridUnitType.Star) });
            var imageBorder = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(12), Padding = new Thickness(10), Margin = new Thickness(0, 0, 12, 0), Child = _preview };
            content.Children.Add(imageBorder);
            var result = new DockPanel(); Grid.SetColumn(result, 1); content.Children.Add(result);
            _sheets.Margin = new Thickness(0, 0, 0, 8); DockPanel.SetDock(_sheets, Dock.Top); result.Children.Add(_sheets); result.Children.Add(_grid);
            _sheets.SelectionChanged += delegate { var table = _sheets.SelectedItem as DataTable; _grid.ItemsSource = table == null ? null : table.DefaultView; };
            root.Children.Add(content);
            Closed += delegate { if (_request != null) _request.Cancel(); if (_image != null) { _image.Dispose(); _image = null; } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape) { if (_request != null) _request.Cancel(); else Close(); e.Handled = true; }
            };
            if (image != null) { SetImage(new Bitmap(image)); Loaded += async delegate { await Recognize(); }; }
        }

        private static Button ActionButton(string text, RoutedEventHandler handler)
        {
            var button = new Button { Content = text, Padding = new Thickness(12, 6, 12, 6), Margin = new Thickness(0, 0, 8, 4), MinHeight = 34,
                Background = Brushes.White, Foreground = new SolidColorBrush(Color.FromRgb(18, 85, 107)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(205, 224, 233)), BorderThickness = new Thickness(1), Cursor = Cursors.Hand };
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(9));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            border.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            border.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            border.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            button.Template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            button.IsEnabledChanged += delegate { button.Opacity = button.IsEnabled ? 1 : .45; };
            button.Click += handler; return button;
        }

        private bool CanReplace()
        {
            if (_request != null) { _status.Text = "请先停止当前识别。"; return false; }
            return _tables.Length == 0 || MessageBox.Show(this, "更换图片会清空当前表格，包括手动修改。", "更换图片", MessageBoxButton.OKCancel) == MessageBoxResult.OK;
        }

        private void SetImage(Bitmap image)
        {
            if (_image != null) _image.Dispose(); _image = image;
            using (var stream = new MemoryStream())
            {
                image.Save(stream, System.Drawing.Imaging.ImageFormat.Png); stream.Position = 0;
                var source = new BitmapImage(); source.BeginInit(); source.CacheOption = BitmapCacheOption.OnLoad; source.StreamSource = stream; source.EndInit(); source.Freeze(); _preview.Source = source;
            }
            _tables = new DataTable[0]; _sheets.ItemsSource = _tables; _grid.ItemsSource = null;
            _copy.IsEnabled = _save.IsEnabled = false;
            _status.Text = "图片已就绪 · 点击识别表格";
        }

        private async Task Capture()
        {
            if (!CanReplace()) return;
            Hide(); await Task.Delay(160);
            try
            {
                using (var selector = new ScreenshotSelector())
                    if (selector.ShowDialog() == System.Windows.Forms.DialogResult.OK && selector.SelectedBitmap != null)
                        SetImage(selector.SelectedBitmap);
            }
            catch (Exception) { _status.Text = "截图失败，请重试。"; }
            finally { Show(); Activate(); }
        }

        private void AddImage()
        {
            if (!CanReplace()) return;
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "图片|*.png;*.jpg;*.jpeg;*.bmp" };
            if (dialog.ShowDialog(this) != true) return;
            try
            {
                if (new FileInfo(dialog.FileName).Length > 20 * 1024 * 1024) throw new InvalidOperationException();
                using (var image = new Bitmap(dialog.FileName)) SetImage(new Bitmap(image));
            }
            catch (Exception) { _status.Text = "无法读取图片，请使用不超过 20 MB 的 PNG、JPEG 或 BMP。"; }
        }

        private void PasteImage()
        {
            if (!CanReplace()) return;
            try
            {
                var source = Clipboard.GetImage();
                if (source == null) { _status.Text = "剪贴板中没有图片。"; return; }
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
                using (var stream = new MemoryStream())
                {
                    encoder.Save(stream); stream.Position = 0;
                    using (var image = new Bitmap(stream)) SetImage(new Bitmap(image));
                }
            }
            catch (Exception) { _status.Text = "无法粘贴图片，请重试。"; }
        }

        private async Task Recognize()
        {
            if (_request != null) return;
            if (_image == null) { _status.Text = "请先添加图片。"; return; }
            if (_tables.Length != 0 && MessageBox.Show(this, "重新识别将替换当前表格和修改。", "重新识别", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return;
            var pending = new CancellationTokenSource(); _request = pending;
            _recognize.IsEnabled = false; _grid.IsReadOnly = true; _stop.Visibility = Visibility.Visible;
            _status.Text = "正在识别表格 · " + _settings.ModelVendor;
            try
            {
                _consent();
                TableDocument document;
                using (var copy = new Bitmap(_image)) document = await _client.RecognizeTableAsync(copy, _settings, pending.Token);
                if (pending.IsCancellationRequested || !IsVisible) return;
                if (document.tables.Length == 0) { _status.Text = "未找到表格，请重新框选表格区域。"; return; }
                _tables = document.tables.Select((sheet, index) =>
                {
                    var table = new DataTable(string.IsNullOrWhiteSpace(sheet.title) ? "表格 " + (index + 1) : sheet.title);
                    for (int c = 0; c < sheet.rows[0].Length; c++) table.Columns.Add("列 " + (c + 1), typeof(string));
                    foreach (var row in sheet.rows) table.Rows.Add(row.Cast<object>().ToArray());
                    return table;
                }).ToArray();
                _sheets.ItemsSource = _tables; _sheets.SelectedIndex = 0;
                _copy.IsEnabled = _save.IsEnabled = true;
                _status.Text = "已识别 " + _tables.Length + " 张表格 · 可双击修改单元格，请核对金额与看不清的内容。";
            }
            catch (OperationCanceledException) { _status.Text = "已停止，保留现有表格。"; }
            catch (Exception ex) { _status.Text = "识别失败：" + (ex is InvalidOperationException ? ex.Message : "请检查网络与模型的图片能力后重试。"); }
            finally { _request = null; pending.Dispose(); _recognize.IsEnabled = true; _grid.IsReadOnly = false; _stop.Visibility = Visibility.Collapsed; }
        }

        private void Export(bool file)
        {
            try
            {
                _grid.CommitEdit(DataGridEditingUnit.Cell, true); _grid.CommitEdit(DataGridEditingUnit.Row, true);
                var table = _sheets.SelectedItem as DataTable; if (table == null) return;
                var rows = table.Rows.Cast<DataRow>().Select(row => row.ItemArray.Select(x => Convert.ToString(x)).ToArray()).ToArray();
                string text = TableDocument.Export(rows, file ? ',' : '\t');
                if (file)
                {
                    var dialog = new Microsoft.Win32.SaveFileDialog { Filter = "CSV 表格|*.csv", FileName = "Sharkey-表格.csv" };
                    if (dialog.ShowDialog(this) != true) return;
                    File.WriteAllText(dialog.FileName, text, new System.Text.UTF8Encoding(true));
                    _status.Text = "已导出当前表格。导入 Excel 时可将编号列设置为文本，保留前导零。";
                }
                else { Clipboard.SetText(text); _status.Text = "已复制当前表格，可粘贴到 Excel；编号列建议预先设为文本。"; }
            }
            catch (Exception) { _status.Text = "操作未完成，请检查文件是否被占用或重试复制。"; }
        }
    }
}
