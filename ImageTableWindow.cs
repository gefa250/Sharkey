using System;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
            AlternatingRowBackground = null, FontSize = 14
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
            Background = Ui.Palette.Canvas;
            Ui.ApplyWindow(this);
            Ui.InstallTextInputStyles(this);

            var root = new DockPanel { Margin = new Thickness(24, 18, 24, 18) };
            Content = root;

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            header.Children.Add(Ui.Text("识图转表格", Ui.Type.Title, Ui.Palette.Ink, FontWeights.SemiBold));
            TextBlock subtitle = Ui.Text("从截图提取表格，识别后可逐格修改，再复制到 Excel 或导出 CSV。",
                Ui.Type.BodySmall, Ui.Palette.InkMuted);
            subtitle.TextWrapping = TextWrapping.Wrap;
            subtitle.TextTrimming = TextTrimming.None;
            subtitle.Margin = new Thickness(0, 4, 0, 0);
            header.Children.Add(subtitle);
            DockPanel.SetDock(header, Dock.Top);
            root.Children.Add(header);

            var actions = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            actions.Children.Add(ActionButton("框选截图", async delegate { await Capture(); }, true));
            actions.Children.Add(ActionButton("添加图片", delegate { AddImage(); }, false));
            actions.Children.Add(ActionButton("粘贴图片", delegate { PasteImage(); }, false));
            _recognize = ActionButton("识别表格", async delegate { await Recognize(); }, true);
            actions.Children.Add(_recognize);
            _stop = ActionButton("停止", delegate { if (_request != null) _request.Cancel(); }, false);
            _stop.Visibility = Visibility.Collapsed;
            actions.Children.Add(_stop);
            DockPanel.SetDock(actions, Dock.Top);
            root.Children.Add(actions);

            var footer = new DockPanel { Margin = new Thickness(0, 14, 0, 0) };
            var footerActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            DockPanel.SetDock(footerActions, Dock.Right);
            footer.Children.Add(footerActions);
            _copy = ActionButton("复制到 Excel", delegate { Export(false); }, false);
            _save = ActionButton("导出 CSV…", delegate { Export(true); }, false);
            _copy.IsEnabled = _save.IsEnabled = false;
            footerActions.Children.Add(_copy);
            footerActions.Children.Add(_save);
            _status.Margin = new Thickness(2, 0, 12, 0);
            _status.VerticalAlignment = VerticalAlignment.Center;
            footer.Children.Add(_status);
            DockPanel.SetDock(footer, Dock.Bottom);
            root.Children.Add(footer);

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.32, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(0.68, GridUnitType.Star) });
            var imageBorder = new Border
            {
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Ui.Radius.Card),
                Padding = new Thickness(8),
                Margin = new Thickness(0, 0, 12, 0),
                Child = _preview
            };
            content.Children.Add(imageBorder);
            var right = new Border
            {
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Ui.Radius.Card),
                Padding = new Thickness(12)
            };
            Grid.SetColumn(right, 1);
            content.Children.Add(right);
            var result = new DockPanel();
            _sheets.Margin = new Thickness(0, 0, 0, 10);
            _sheets.FontSize = Ui.Type.BodySmall;
            _sheets.Style = SettingsWindow.CreateComboBoxStyle();
            _sheets.Height = 34;
            _sheets.MinHeight = 34;
            DockPanel.SetDock(_sheets, Dock.Top);
            result.Children.Add(_sheets);
            result.Children.Add(_grid);
            right.Child = result;
            _sheets.SelectionChanged += delegate
            {
                var table = _sheets.SelectedItem as DataTable;
                _grid.ItemsSource = table == null ? null : table.DefaultView;
            };
            root.Children.Add(content);

            _status.FontSize = Ui.Type.Meta;
            _status.Foreground = Ui.Palette.InkMuted;
            _grid.FontFamily = Ui.Type.Family;
            _grid.FontSize = Ui.Type.BodySmall;
            _grid.RowHeight = 36;
            _grid.Background = Ui.Palette.Surface;
            _grid.BorderThickness = new Thickness(0);
            _grid.HorizontalGridLinesBrush = Ui.Palette.Line;
            _grid.VerticalGridLinesBrush = Brushes.Transparent;
            _grid.AlternatingRowBackground = null;
            _grid.RowHeaderWidth = 0;
            _grid.ColumnHeaderStyle = TableHeaderStyle();
            _grid.CellStyle = TableCellStyle();
            _grid.RowStyle = TableRowStyle();

            Closed += delegate { if (_request != null) _request.Cancel(); if (_image != null) { _image.Dispose(); _image = null; } };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    if (_request != null) _request.Cancel(); else Close();
                    e.Handled = true;
                    return;
                }
                if (e.Key != Key.V || Keyboard.Modifiers != ModifierKeys.Control) return;
                if (Keyboard.FocusedElement is TextBox) return;
                e.Handled = true;
                PasteImage();
            };
            if (image != null) { SetImage(new Bitmap(image)); Loaded += async delegate { await Recognize(); }; }
        }

        private static Style TableHeaderStyle()
        {
            var style = new Style(typeof(DataGridColumnHeader));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Palette.SurfaceSubtle));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Ui.Palette.InkMuted));
            style.Setters.Add(new Setter(Control.FontSizeProperty, Ui.Type.Meta));
            style.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 8, 10, 8)));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Ui.Palette.Line));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0, 0, 0, 1)));
            style.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Left));
            return style;
        }

        private static Style TableCellStyle()
        {
            var style = new Style(typeof(DataGridCell));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            var template = new ControlTemplate(typeof(DataGridCell));
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "CellChrome";
            border.SetValue(Border.BackgroundProperty, Brushes.Transparent);
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.PaddingProperty, new Thickness(10, 0, 10, 0));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(presenter);
            template.VisualTree = border;
            var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Border.BackgroundProperty, Ui.Palette.BrandTint, "CellChrome"));
            template.Triggers.Add(selected);
            style.Setters.Add(new Setter(Control.TemplateProperty, template));
            return style;
        }

        private static Style TableRowStyle()
        {
            var style = new Style(typeof(DataGridRow));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Palette.Surface));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(0)));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Palette.SurfaceHover));
            style.Triggers.Add(hover);
            var selected = new Trigger { Property = DataGridRow.IsSelectedProperty, Value = true };
            selected.Setters.Add(new Setter(Control.BackgroundProperty, Ui.Palette.BrandTint));
            style.Triggers.Add(selected);
            return style;
        }

        private static Button ActionButton(string text, RoutedEventHandler handler, bool primary)
        {
            Button button = primary ? Ui.PrimaryButton(text) : Ui.SecondaryButton(text);
            button.MinHeight = 34;
            button.MinWidth = 0;
            button.Padding = new Thickness(14, 6, 14, 6);
            button.Margin = new Thickness(0, 0, 8, 4);
            button.Click += handler;
            return button;
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
            _sheets.Visibility = Visibility.Collapsed;
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
                _sheets.Visibility = Visibility.Visible;
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
