using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace GlobalTranslator
{
    internal sealed class OcrPreviewWindow : Window
    {
        public event Action<string> TranslateRequested;
        public event Action RecaptureRequested;

        public OcrPreviewWindow(string text)
        {
            Title = "鲨译 · 提取文字";
            Width = 460;
            Height = 280;
            MinWidth = 360;
            MinHeight = 200;
            ShowActivated = true;
            ShowInTaskbar = false;
            Topmost = true;
            Rect workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, workArea.Right - Width - 16);
            Top = Math.Max(workArea.Top, workArea.Bottom - Height - 16);

            Ui.ApplyWindow(this);
            Ui.InstallTextInputStyles(this);
            Background = Ui.Palette.Canvas;

            var card = new Border
            {
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Ui.Radius.Window),
                Padding = new Thickness(Ui.Space.Lg),
                Effect = Ui.Elevation.Popup()
            };
            var root = new DockPanel();
            card.Child = root;
            Content = card;

            var status = new TextBlock
            {
                Margin = new Thickness(0, 0, 0, Ui.Space.Sm),
                FontSize = Ui.Type.Meta,
                Foreground = Ui.Palette.InkMuted
            };
            DockPanel.SetDock(status, Dock.Top);
            root.Children.Add(status);

            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, Ui.Space.Md, 0, 0)
            };
            DockPanel.SetDock(actions, Dock.Bottom);
            root.Children.Add(actions);

            var editor = new TextBox
            {
                Text = text,
                FontSize = Ui.Type.Body,
                FontFamily = Ui.Type.Family,
                Foreground = Ui.Palette.Ink,
                Background = Ui.Palette.Surface,
                BorderBrush = Ui.Palette.Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8),
                AcceptsTab = false,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            root.Children.Add(editor);
            Button copy = AddButton(actions, "复制");
            Button translate = AddButton(actions, "翻译这段文字");
            Button recapture = AddButton(actions, "重新截图");
            Ui.Style(copy, Ui.ButtonKind.Secondary);
            Ui.Style(translate, Ui.ButtonKind.Primary);
            Ui.Style(recapture, Ui.ButtonKind.Ghost);
            copy.Click += delegate { CopyText(editor.Text, status); };
            translate.Click += delegate
            {
                var handler = TranslateRequested;
                if (handler != null) handler(editor.Text);
            };
            recapture.Click += delegate
            {
                var handler = RecaptureRequested;
                if (handler != null) handler();
            };
            editor.TextChanged += delegate
            {
                copy.IsEnabled = translate.IsEnabled = !string.IsNullOrWhiteSpace(editor.Text);
                status.Text = "原文已编辑";
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                e.Handled = true;
                Close();
            };
            CopyText(text, status);
        }

        private static Button AddButton(Panel actions, string label)
        {
            var button = new Button
            {
                Content = label,
                Padding = new Thickness(12, 5, 12, 5),
                Margin = new Thickness(0, 0, Ui.Space.Sm, 0),
                MinHeight = 34,
                FontSize = Ui.Type.BodySmall
            };
            actions.Children.Add(button);
            return button;
        }

        private static void CopyText(string text, TextBlock status)
        {
            try
            {
                var data = new System.Windows.Forms.DataObject(
                    System.Windows.Forms.DataFormats.UnicodeText, text);
                System.Windows.Forms.Clipboard.SetDataObject(data, true, 4, 30);
                status.Text = "已复制到剪贴板";
            }
            catch (ExternalException)
            {
                status.Text = "复制失败，请点击「复制」重试";
            }
        }
    }
}
