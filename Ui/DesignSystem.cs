using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace GlobalTranslator
{
    // The single source of truth for Sharkey's look: colour tokens, type scale,
    // spacing, radii, elevation, icons and the shared control factories.
    //
    // Rules for future UI work:
    //   * never write a literal colour outside this file;
    //   * pick a radius from Radius, never a one-off number;
    //   * pick a size from Type, never 12.5 / 15.5 style half steps;
    //   * a new icon belongs in Ui/Icons.cs so the stroke weight stays equal.
    internal static class Ui
    {
        internal enum ButtonKind
        {
            Primary,
            Secondary,
            Ghost,
            Icon,
            Segment
        }

        internal enum StatusKind
        {
            Info,
            Success,
            Warning,
            Danger
        }

        internal static class Palette
        {
            private static readonly bool HighContrast = SystemParameters.HighContrast;

            internal static readonly Brush Canvas = HighContrast ? SystemColors.WindowBrush : Hex("#F4F7F9");
            internal static readonly Brush Surface = HighContrast ? SystemColors.WindowBrush : Hex("#FFFFFF");
            internal static readonly Brush SurfaceSubtle = HighContrast ? SystemColors.ControlBrush : Hex("#EEF4F7");
            internal static readonly Brush SurfaceHover = HighContrast ? SystemColors.ControlBrush : Hex("#E8F1F5");
            internal static readonly Brush Line = HighContrast ? SystemColors.ActiveBorderBrush : Hex("#E1EAEF");
            internal static readonly Brush LineStrong = HighContrast ? SystemColors.ActiveBorderBrush : Hex("#CFDCE3");

            internal static readonly Brush Ink = HighContrast ? SystemColors.WindowTextBrush : Hex("#132430");
            internal static readonly Brush InkMuted = HighContrast ? SystemColors.GrayTextBrush : Hex("#5A6B78");
            internal static readonly Brush InkFaint = HighContrast ? SystemColors.GrayTextBrush : Hex("#8A97A1");

            internal static readonly Brush Brand = HighContrast ? SystemColors.HighlightBrush : Hex("#0B7C99");
            internal static readonly Brush BrandHover = HighContrast ? SystemColors.HighlightBrush : Hex("#0A6C86");
            internal static readonly Brush BrandPressed = HighContrast ? SystemColors.HighlightBrush : Hex("#085A70");
            internal static readonly Brush BrandText = HighContrast ? SystemColors.HotTrackBrush : Hex("#06657F");
            internal static readonly Brush BrandTint = HighContrast ? SystemColors.ControlBrush : Hex("#E7F4F8");
            internal static readonly Brush BrandTintStrong = HighContrast ? SystemColors.ControlBrush : Hex("#D2EAF2");
            internal static readonly Brush Accent = HighContrast ? SystemColors.HighlightBrush : Hex("#17B0C4");
            internal static readonly Brush OnBrand = HighContrast ? SystemColors.HighlightTextBrush : Brushes.White;

            internal static readonly Brush Success = HighContrast ? SystemColors.WindowTextBrush : Hex("#15704A");
            internal static readonly Brush SuccessTint = HighContrast ? SystemColors.ControlBrush : Hex("#E8F5EE");
            internal static readonly Brush Warning = HighContrast ? SystemColors.WindowTextBrush : Hex("#875106");
            internal static readonly Brush WarningTint = HighContrast ? SystemColors.ControlBrush : Hex("#FBF2E2");
            internal static readonly Brush Danger = HighContrast ? SystemColors.WindowTextBrush : Hex("#B83C3C");
            internal static readonly Brush DangerTint = HighContrast ? SystemColors.ControlBrush : Hex("#FBEDED");
            internal static readonly Brush Focus = HighContrast ? SystemColors.HighlightBrush : Hex("#7FC7DA");

            internal static readonly Brush Shadow = Hex("#0A2836");

            internal static bool IsHighContrast { get { return HighContrast; } }
        }

        internal static class Type
        {
            internal const double Caption = 11;
            internal const double Meta = 12;
            internal const double BodySmall = 13;
            internal const double Body = 14;
            internal const double Subtitle = 16;
            internal const double Title = 20;
            internal const double Display = 26;

            internal const double ReadingLine = 1.55;

            internal static readonly FontFamily Family =
                new FontFamily("Segoe UI, Microsoft YaHei UI");
            internal static readonly FontFamily Mono =
                new FontFamily("Consolas, Microsoft YaHei UI");
        }

        internal static class Space
        {
            internal const double Xs = 4;
            internal const double Sm = 8;
            internal const double Md = 12;
            internal const double Lg = 16;
            internal const double Xl = 20;
            internal const double Xxl = 24;
            internal const double Xxxl = 32;
        }

        internal static class Radius
        {
            internal const double Control = 10;
            internal const double Card = 14;
            internal const double Window = 18;

            internal static CornerRadius Pill { get { return new CornerRadius(999); } }
            internal static CornerRadius Inner { get { return new CornerRadius(Control - 2); } }
        }

        internal static class Elevation
        {
            internal static DropShadowEffect Popup()
            {
                return new DropShadowEffect
                {
                    BlurRadius = 40,
                    ShadowDepth = 10,
                    Direction = 270,
                    Opacity = .18,
                    Color = Color.FromRgb(10, 40, 54)
                };
            }

            internal static DropShadowEffect Menu()
            {
                return new DropShadowEffect
                {
                    BlurRadius = 28,
                    ShadowDepth = 8,
                    Direction = 270,
                    Opacity = .16,
                    Color = Color.FromRgb(10, 40, 54)
                };
            }
        }

        // ---------------------------------------------------------------- setup

        internal static void ApplyWindow(Window window)
        {
            if (window == null) return;
            window.FontFamily = Type.Family;
            window.FontSize = Type.Body;
            window.UseLayoutRounding = true;
            window.Resources[typeof(Button)] = ImplicitButtonStyle();
            if (!window.Resources.Contains(typeof(ToolTip)))
                window.Resources[typeof(ToolTip)] = ImplicitToolTipStyle();
        }

        internal static void InstallTextInputStyles(Window window)
        {
            if (window == null) return;
            window.Resources[typeof(TextBox)] = ImplicitTextBoxStyle();
        }

        // -------------------------------------------------------------- styles

        internal static Style ImplicitButtonStyle()
        {
            var style = new Style(typeof(Button));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Palette.Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Ink));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.LineStrong));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(14, 7, 14, 7)));
            style.Setters.Add(new Setter(Control.CursorProperty, Cursors.Hand));
            style.Setters.Add(new Setter(Control.TemplateProperty, ButtonTemplate(ButtonKind.Secondary)));
            style.Setters.Add(new Setter(Control.FocusVisualStyleProperty, null));
            return style;
        }

        internal static Style ImplicitTextBoxStyle()
        {
            var style = new Style(typeof(TextBox));
            style.Setters.Add(new Setter(Control.BackgroundProperty, Palette.Surface));
            style.Setters.Add(new Setter(Control.ForegroundProperty, Palette.Ink));
            style.Setters.Add(new Setter(Control.BorderBrushProperty, Palette.LineStrong));
            style.Setters.Add(new Setter(Control.BorderThicknessProperty, new Thickness(1)));
            style.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(10, 7, 10, 7)));
            style.Setters.Add(new Setter(TextBoxBase.CaretBrushProperty, Palette.BrandText));
            style.Setters.Add(new Setter(TextBoxBase.SelectionBrushProperty, Palette.BrandTintStrong));
            style.Setters.Add(new Setter(Control.TemplateProperty, TextBoxTemplate()));
            return style;
        }

        internal static Style ImplicitToolTipStyle()
        {
            var style = new Style(typeof(ToolTip));
            style.Setters.Add(new Setter(Control.FontFamilyProperty, Type.Family));
            style.Setters.Add(new Setter(Control.FontSizeProperty, Type.Meta));
            style.Setters.Add(new Setter(Control.ForegroundProperty,
                SystemParameters.HighContrast ? SystemColors.InfoTextBrush : Brushes.White));
            style.Setters.Add(new Setter(Control.TemplateProperty, ToolTipTemplate()));
            return style;
        }

        // ------------------------------------------------------------ factories

        internal static Button PrimaryButton(string text)
        {
            Button button = BaseButton(text, ButtonKind.Primary);
            button.Background = Palette.Brand;
            button.Foreground = Palette.OnBrand;
            button.BorderBrush = Palette.Brand;
            button.FontWeight = FontWeights.SemiBold;
            button.Padding = new Thickness(Space.Lg, 7, Space.Lg, 7);
            button.MinHeight = 36;
            return button;
        }

        internal static Button SecondaryButton(string text)
        {
            Button button = BaseButton(text, ButtonKind.Secondary);
            button.Padding = new Thickness(14, 7, 14, 7);
            button.MinHeight = 36;
            return button;
        }

        internal static Button GhostButton(string text)
        {
            Button button = BaseButton(text, ButtonKind.Ghost);
            button.Background = Brushes.Transparent;
            button.Foreground = Palette.InkMuted;
            button.BorderBrush = Brushes.Transparent;
            button.BorderThickness = new Thickness(0);
            button.Padding = new Thickness(Space.Sm + 2, 5, Space.Sm + 2, 5);
            button.MinHeight = 32;
            return button;
        }

        internal static Button IconButton(Geometry glyph, string accessibleName)
        {
            return IconButton(glyph, accessibleName, 32);
        }

        internal static Button IconButton(Geometry glyph, string accessibleName, double size)
        {
            var button = new Button
            {
                Content = Glyph(glyph, null),
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground = Palette.InkMuted,
                Padding = new Thickness((size - Icons.Size) / 2),
                Width = size,
                Height = size,
                Cursor = Cursors.Hand,
                ToolTip = accessibleName,
                Template = ButtonTemplate(ButtonKind.Icon),
                FocusVisualStyle = null
            };
            var path = button.Content as Path;
            if (path != null)
                path.SetBinding(Shape.StrokeProperty, new Binding("Foreground")
                {
                    RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1)
                });
            AutomationProperties.SetName(button, accessibleName);
            return button;
        }

        internal static Path Glyph(Geometry geometry, Brush stroke)
        {
            return Glyph(geometry, stroke, Icons.Size, Icons.Stroke);
        }

        internal static Path Glyph(Geometry geometry, Brush stroke, double size, double thickness)
        {
            return new Path
            {
                Data = geometry,
                Stroke = stroke ?? Palette.InkMuted,
                StrokeThickness = thickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
        }

        internal static TextBlock Text(string text, double size, Brush color)
        {
            return Text(text, size, color, (FontWeight?)null);
        }

        internal static TextBlock Text(string text, double size, Brush color, FontWeight? weight)
        {
            var block = new TextBlock
            {
                Text = text ?? "",
                FontSize = size,
                Foreground = color ?? Palette.Ink,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            if (weight.HasValue) block.FontWeight = weight.Value;
            return block;
        }

        internal static TextBlock SectionHeader(string text)
        {
            var block = Text(text, Type.Caption, Palette.InkMuted, FontWeights.SemiBold);
            block.TextTrimming = TextTrimming.None;
            return block;
        }

        internal static TextBlock Hint(string text)
        {
            var block = Text(text, Type.Meta, Palette.InkMuted);
            block.TextWrapping = TextWrapping.Wrap;
            block.TextTrimming = TextTrimming.None;
            return block;
        }

        internal static Border Divider()
        {
            return new Border { Height = 1, Background = Palette.Line, SnapsToDevicePixels = true };
        }

        internal static Border Card(UIElement child)
        {
            return Card(child, Space.Lg);
        }

        internal static Border Card(UIElement child, double padding)
        {
            return new Border
            {
                Background = Palette.Surface,
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Radius.Card),
                Padding = new Thickness(padding),
                Child = child
            };
        }

        internal static Border Chip(string text)
        {
            return new Border
            {
                Background = Palette.BrandTint,
                CornerRadius = Radius.Pill,
                Padding = new Thickness(9, 3, 9, 3),
                VerticalAlignment = VerticalAlignment.Center,
                Child = Text(text, Type.Meta, Palette.BrandText, FontWeights.Medium)
            };
        }

        internal static Border KeyCap(string text)
        {
            return new Border
            {
                Background = Palette.SurfaceSubtle,
                BorderBrush = Palette.Line,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(Radius.Control - 2),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock
                {
                    Text = text ?? "",
                    FontFamily = Type.Mono,
                    FontSize = Type.Meta,
                    Foreground = Palette.InkMuted
                }
            };
        }

        internal static FrameworkElement Field(string label, UIElement input, string help)
        {
            var stack = new StackPanel();
            var caption = Text(label, Type.BodySmall, Palette.Ink, FontWeights.Medium);
            caption.TextTrimming = TextTrimming.None;
            caption.Margin = new Thickness(0, 0, 0, 6);
            stack.Children.Add(caption);
            stack.Children.Add(input);
            if (!string.IsNullOrEmpty(help))
            {
                TextBlock note = Hint(help);
                note.Margin = new Thickness(1, 6, 0, 0);
                stack.Children.Add(note);
            }
            return stack;
        }

        internal static FrameworkElement Segmented(string[] items, int selectedIndex, Action<int> onSelect)
        {
            var track = new Border
            {
                Background = Palette.SurfaceSubtle,
                CornerRadius = new CornerRadius(Radius.Control),
                Padding = new Thickness(3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            track.Child = row;
            var buttons = new Button[items.Length];
            for (int i = 0; i < items.Length; i++)
            {
                int index = i;
                var button = new Button
                {
                    Content = items[i],
                    FontSize = Type.BodySmall,
                    Padding = new Thickness(16, 6, 16, 6),
                    MinHeight = 32,
                    BorderThickness = new Thickness(0),
                    Background = Brushes.Transparent,
                    Foreground = Palette.InkMuted,
                    Template = ButtonTemplate(ButtonKind.Segment),
                    FocusVisualStyle = null
                };
                button.Click += delegate
                {
                    ApplySegmentSelection(buttons, index);
                    if (onSelect != null) onSelect(index);
                };
                buttons[i] = button;
                row.Children.Add(button);
            }
            ApplySegmentSelection(buttons, selectedIndex);
            return track;
        }

        private static void ApplySegmentSelection(Button[] buttons, int selectedIndex)
        {
            for (int i = 0; i < buttons.Length; i++)
            {
                bool selected = i == selectedIndex;
                buttons[i].Background = selected ? Palette.Surface : Brushes.Transparent;
                buttons[i].Foreground = selected ? Palette.BrandText : Palette.InkMuted;
                buttons[i].FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
            }
        }

        internal static StatusLine Status()
        {
            return new StatusLine();
        }

        // --------------------------------------------------------------- shapes

        private static Button BaseButton(string text, ButtonKind kind)
        {
            return new Button
            {
                Content = text ?? "",
                FontSize = Type.Body,
                Cursor = Cursors.Hand,
                Template = ButtonTemplate(kind),
                FocusVisualStyle = null,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center
            };
        }

        private static ControlTemplate ButtonTemplate(ButtonKind kind)
        {
            var root = new FrameworkElementFactory(typeof(Grid));

            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "Chrome";
            chrome.SetValue(Border.CornerRadiusProperty,
                new CornerRadius(kind == ButtonKind.Segment ? Radius.Control - 2 : Radius.Control));
            chrome.SetBinding(Border.BackgroundProperty, Templated("Background"));
            chrome.SetBinding(Border.BorderBrushProperty, Templated("BorderBrush"));
            chrome.SetBinding(Border.BorderThicknessProperty, Templated("BorderThickness"));

            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(ContentPresenter.MarginProperty, Templated("Padding"));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            chrome.AppendChild(presenter);
            root.AppendChild(chrome);

            var ring = new FrameworkElementFactory(typeof(Border));
            ring.Name = "Ring";
            ring.SetValue(Border.CornerRadiusProperty,
                new CornerRadius(kind == ButtonKind.Segment ? Radius.Control - 2 : Radius.Control));
            ring.SetValue(Border.BorderThicknessProperty, new Thickness(2));
            ring.SetValue(Border.BorderBrushProperty, Brushes.Transparent);
            ring.SetValue(UIElement.IsHitTestVisibleProperty, false);
            root.AppendChild(ring);

            var template = new ControlTemplate(typeof(Button)) { VisualTree = root };
            switch (kind)
            {
                case ButtonKind.Primary:
                    template.Triggers.Add(Hover("Chrome", Border.BackgroundProperty, Palette.BrandHover));
                    template.Triggers.Add(Pressed("Chrome", Border.BackgroundProperty, Palette.BrandPressed));
                    break;
                case ButtonKind.Secondary:
                    template.Triggers.Add(Hover("Chrome", Border.BackgroundProperty, Palette.SurfaceHover));
                    template.Triggers.Add(Hover("Chrome", Border.BorderBrushProperty, Palette.BrandTintStrong));
                    template.Triggers.Add(Pressed("Chrome", Border.BackgroundProperty, Palette.BrandTint));
                    break;
                case ButtonKind.Ghost:
                case ButtonKind.Segment:
                    template.Triggers.Add(Hover("Chrome", Border.BackgroundProperty, Palette.SurfaceHover));
                    template.Triggers.Add(Pressed("Chrome", Border.BackgroundProperty, Palette.BrandTintStrong));
                    break;
                case ButtonKind.Icon:
                    template.Triggers.Add(Hover("Chrome", Border.BackgroundProperty, Palette.SurfaceHover));
                    template.Triggers.Add(Pressed("Chrome", Border.BackgroundProperty, Palette.BrandTintStrong));
                    var hoverInk = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
                    hoverInk.Setters.Add(new Setter(Control.ForegroundProperty, Palette.BrandText));
                    template.Triggers.Add(hoverInk);
                    break;
            }

            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Palette.Focus, "Ring"));
            template.Triggers.Add(focus);

            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .45));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate TextBoxTemplate()
        {
            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.Name = "Chrome";
            chrome.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius.Control));
            chrome.SetValue(UIElement.SnapsToDevicePixelsProperty, true);
            chrome.SetBinding(Border.BackgroundProperty, Templated("Background"));
            chrome.SetBinding(Border.BorderBrushProperty, Templated("BorderBrush"));
            chrome.SetBinding(Border.BorderThicknessProperty, Templated("BorderThickness"));

            var host = new FrameworkElementFactory(typeof(ScrollViewer));
            host.Name = "PART_ContentHost";
            host.SetValue(ScrollViewer.MarginProperty, new Thickness(0));
            host.SetValue(UIElement.FocusableProperty, false);
            chrome.AppendChild(host);

            var template = new ControlTemplate(typeof(TextBox)) { VisualTree = chrome };
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Palette.LineStrong, "Chrome"));
            template.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusWithinProperty, Value = true };
            focus.Setters.Add(new Setter(Border.BorderBrushProperty, Palette.Brand, "Chrome"));
            focus.Setters.Add(new Setter(Border.BorderThicknessProperty, new Thickness(2), "Chrome"));
            template.Triggers.Add(focus);
            var disabled = new Trigger { Property = UIElement.IsEnabledProperty, Value = false };
            disabled.Setters.Add(new Setter(UIElement.OpacityProperty, .5));
            template.Triggers.Add(disabled);
            return template;
        }

        private static ControlTemplate ToolTipTemplate()
        {
            bool highContrast = Palette.IsHighContrast;
            var border = new FrameworkElementFactory(typeof(Border));
            border.Name = "TipCard";
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(Radius.Control - 2));
            border.SetValue(Border.BackgroundProperty,
                highContrast ? SystemColors.InfoBrush : (Brush)Hex("#1B2A35"));
            border.SetValue(Border.BorderThicknessProperty, new Thickness(0));
            border.SetValue(Border.PaddingProperty, new Thickness(9, 5, 9, 6));
            var render = new FrameworkElementFactory(typeof(ContentPresenter));
            border.AppendChild(render);
            return new ControlTemplate(typeof(ToolTip)) { VisualTree = border };
        }

        // -------------------------------------------------------------- helpers

        private static Trigger Hover(string target, DependencyProperty property, object value)
        {
            var trigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            trigger.Setters.Add(new Setter(property, value, target));
            return trigger;
        }

        private static Trigger Pressed(string target, DependencyProperty property, object value)
        {
            var trigger = new Trigger { Property = ButtonBase.IsPressedProperty, Value = true };
            trigger.Setters.Add(new Setter(property, value, target));
            return trigger;
        }

        private static Binding Templated(string property)
        {
            return new Binding(property)
            {
                RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent)
            };
        }

        private static SolidColorBrush Hex(string hex)
        {
            var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
            brush.Freeze();
            return brush;
        }

        // ---------------------------------------------------------- status line

        internal sealed class StatusLine : Border
        {
            private readonly Path _icon;
            private readonly TextBlock _text;

            internal StatusLine()
            {
                CornerRadius = new CornerRadius(Radius.Control);
                BorderThickness = new Thickness(0);
                Padding = new Thickness(10, 6, 12, 6);
                Visibility = Visibility.Collapsed;
                var row = new Grid();
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _icon = Glyph(Icons.Info, Palette.BrandText, 14, 1.5);
                _icon.Margin = new Thickness(0, 0, 8, 0);
                row.Children.Add(_icon);
                _text = new TextBlock
                {
                    FontSize = Type.Meta,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(_text, 1);
                row.Children.Add(_text);
                Child = row;
            }

            internal void Set(StatusKind kind, string message)
            {
                if (string.IsNullOrEmpty(message))
                {
                    Visibility = Visibility.Collapsed;
                    _text.Text = "";
                    return;
                }
                switch (kind)
                {
                    case StatusKind.Success:
                        Background = Palette.SuccessTint;
                        _icon.Data = Icons.Check;
                        _icon.Stroke = Palette.Success;
                        _text.Foreground = Palette.Success;
                        break;
                    case StatusKind.Warning:
                        Background = Palette.WarningTint;
                        _icon.Data = Icons.Alert;
                        _icon.Stroke = Palette.Warning;
                        _text.Foreground = Palette.Warning;
                        break;
                    case StatusKind.Danger:
                        Background = Palette.DangerTint;
                        _icon.Data = Icons.Alert;
                        _icon.Stroke = Palette.Danger;
                        _text.Foreground = Palette.Danger;
                        break;
                    default:
                        Background = Palette.BrandTint;
                        _icon.Data = Icons.Info;
                        _icon.Stroke = Palette.BrandText;
                        _text.Foreground = Palette.BrandText;
                        break;
                }
                _text.Text = message;
                Visibility = Visibility.Visible;
            }
        }
    }
}
