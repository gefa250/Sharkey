using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Guards the visual language introduced in Ui/DesignSystem.cs:
//   * colours, radii and type sizes only come from tokens;
//   * token colour pairs keep WCAG AA contrast;
//   * shared components keep 32px hit targets and never clip their label;
//   * a component sheet is rendered at 100/150/200% for human review.
internal static class DesignProbe
{
    // Files that have already been migrated to the design system. Every file in
    // here must be free of literal colours; the list grows phase by phase.
    static readonly string[] Migrated =
    {
        "Ui/DesignSystem.cs", "Ui/Icons.cs", "Ui/Motion.cs",
        "ChatReadingView.cs", "CommerceChatLayout.cs", "PopupWindow.cs",
        "SettingsWindow.cs", "ImageTableWindow.cs", "UpdateWindow.cs"
    };

    static int failures;
    static readonly List<string> Notes = new List<string>();

    static void Check(bool ok, string message)
    {
        if (ok) return;
        failures++;
        Console.Error.WriteLine("FAIL " + message);
    }

    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }

    // ------------------------------------------------------------------ tokens

    static Dictionary<string, string> ReadPalette(string source)
    {
        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match line in Regex.Matches(source, @"internal static readonly Brush (\w+) = [^\r\n]+"))
        {
            Match hex = Regex.Match(line.Value, @"Hex\(""#([0-9A-Fa-f]{6})""\)");
            if (hex.Success) tokens[line.Groups[1].Value] = hex.Groups[1].Value.ToUpperInvariant();
        }
        return tokens;
    }

    static double Channel(int value)
    {
        double c = value / 255.0;
        return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
    }

    static double Luminance(string hex)
    {
        int r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber);
        int g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber);
        int b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber);
        return 0.2126 * Channel(r) + 0.7152 * Channel(g) + 0.0722 * Channel(b);
    }

    static double Contrast(string first, string second)
    {
        double a = Luminance(first);
        double b = Luminance(second);
        double light = Math.Max(a, b);
        double dark = Math.Min(a, b);
        return (light + 0.05) / (dark + 0.05);
    }

    static void CheckContrast(Dictionary<string, string> palette, string foreground,
        string background, double minimum)
    {
        string fg, bg;
        if (!palette.TryGetValue(foreground, out fg) || !palette.TryGetValue(background, out bg))
        {
            Check(false, "contrast pair " + foreground + "/" + background + " is missing from Palette");
            return;
        }
        double ratio = Contrast(fg, bg);
        Notes.Add(string.Format(CultureInfo.InvariantCulture, "{0}/{1} {2:0.00}:1", foreground, background, ratio));
        Check(ratio >= minimum, string.Format(CultureInfo.InvariantCulture,
            "{0} on {1} is {2:0.00}:1, below {3:0.0}:1", foreground, background, ratio, minimum));
    }

    // ------------------------------------------------------------------ sheet

    static Type LoadType(Assembly assembly, string name)
    {
        Type type = assembly.GetType("GlobalTranslator." + name, true);
        return type;
    }

    static object Call(Type type, string name, params object[] arguments)
    {
        MethodInfo method = type.GetMethods(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
            .Where(m => m.Name == name && m.GetParameters().Length == arguments.Length)
            .FirstOrDefault();
        if (method == null) throw new MissingMethodException(type.FullName, name);
        return method.Invoke(null, arguments);
    }

    static T Make<T>(Type type, string name, params object[] arguments) where T : class
    {
        return Call(type, name, arguments) as T;
    }

    static UIElement Sheet(Assembly assembly)
    {
        Type ui = LoadType(assembly, "Ui");
        Type icons = LoadType(assembly, "Icons");
        Type paletteType = ui.GetNestedType("Palette", BindingFlags.NonPublic | BindingFlags.Public);
        Type statusKindType = ui.GetNestedType("StatusKind", BindingFlags.NonPublic | BindingFlags.Public);

        var root = new StackPanel { Margin = new Thickness(20, 22, 20, 24) };

        root.Children.Add(Heading(assembly, "按钮 · Buttons"));
        var buttons = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        Button primary = Make<Button>(ui, "PrimaryButton", "保存并应用");
        Button secondary = Make<Button>(ui, "SecondaryButton", "取消");
        Button ghost = Make<Button>(ui, "GhostButton", "更多");
        var search = (Geometry)icons.GetField("Search", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null);
        Button icon = Call(ui, "IconButton", search, "搜索会话") as Button;
        Button disabled = Make<Button>(ui, "PrimaryButton", "禁用状态");
        disabled.IsEnabled = false;
        foreach (Button button in new[] { primary, secondary, ghost, icon, disabled })
        {
            button.Margin = new Thickness(0, 0, 8, 8);
            buttons.Children.Add(button);
        }
        root.Children.Add(buttons);

        root.Children.Add(Heading(assembly, "状态 · Status"));
        foreach (string kind in new[] { "Info", "Success", "Warning", "Danger" })
        {
            object line = Call(ui, "Status");
            MethodInfo set = line.GetType().GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            set.Invoke(line, new[] { Enum.Parse(statusKindType, kind), (object)("这是 " + kind + " 状态：已添加截图 · 1/5") });
            ((FrameworkElement)line).Margin = new Thickness(0, 0, 0, 6);
            root.Children.Add((UIElement)line);
        }

        root.Children.Add(Heading(assembly, "表单 · Fields"));
        var input = new TextBox { Text = "deepseek-v4-flash" };
        FrameworkElement field = Make<FrameworkElement>(ui, "Field", "模型名称", input, "截取图片的模型需要支持图片输入。");
        field.Margin = new Thickness(0, 6, 0, 10);
        root.Children.Add(field);

        var chips = new WrapPanel { Margin = new Thickness(0, 4, 0, 10) };
        chips.Children.Add(Make<Border>(ui, "Chip", "自定义"));
        Border cap = Make<Border>(ui, "KeyCap", "Ctrl+V");
        cap.Margin = new Thickness(8, 0, 0, 0);
        chips.Children.Add(cap);
        root.Children.Add(chips);

        root.Children.Add(Heading(assembly, "分段 · Segmented"));
        Action<int> noop = delegate(int index) { };
        FrameworkElement segmented = Make<FrameworkElement>(ui, "Segmented",
            new[] { "DeepSeek", "MiMo", "Qwen", "自定义" }, 0, noop);
        segmented.Margin = new Thickness(0, 6, 0, 12);
        root.Children.Add(segmented);

        root.Children.Add(Heading(assembly, "色板 · Palette"));
        var swatches = new WrapPanel { Margin = new Thickness(0, 8, 0, 4) };
        foreach (FieldInfo fieldInfo in paletteType.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
        {
            var brush = fieldInfo.GetValue(null) as Brush;
            if (brush == null) continue;
            var cell = new StackPanel { Margin = new Thickness(0, 0, 10, 8), Width = 74 };
            cell.Children.Add(new Border
            {
                Height = 30,
                Background = brush,
                BorderBrush = PaletteBrush(paletteType, "Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6)
            });
            cell.Children.Add(new TextBlock
            {
                Text = fieldInfo.Name,
                FontSize = 10,
                Foreground = PaletteBrush(paletteType, "InkMuted"),
                Margin = new Thickness(1, 3, 0, 0)
            });
            swatches.Children.Add(cell);
        }
        root.Children.Add(swatches);

        var surface = new Border
        {
            Background = PaletteBrush(paletteType, "Canvas"),
            Child = new ScrollViewer { Content = root, VerticalScrollBarVisibility = ScrollBarVisibility.Disabled }
        };
        return surface;
    }

    static double UiToken(Type type, string name)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field == null ? 13 : (double)field.GetValue(null);
    }

    static Brush PaletteBrush(Type paletteType, string name)
    {
        FieldInfo field = paletteType.GetField(name, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
        return field == null ? null : field.GetValue(null) as Brush;
    }

    static TextBlock Heading(Assembly assembly, string text)
    {
        Type ui = LoadType(assembly, "Ui");
        Type type = ui.GetNestedType("Type", BindingFlags.NonPublic | BindingFlags.Public);
        Type paletteType = ui.GetNestedType("Palette", BindingFlags.NonPublic | BindingFlags.Public);
        return new TextBlock
        {
            Text = text,
            FontFamily = (FontFamily)type.GetField("Family", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public).GetValue(null),
            FontSize = UiToken(type, "BodySmall"),
            FontWeight = FontWeights.SemiBold,
            Foreground = PaletteBrush(paletteType, "Ink"),
            Margin = new Thickness(0, 14, 0, 2)
        };
    }

    // -------------------------------------------------------------- visual scan

    static void Walk(DependencyObject element, Action<DependencyObject> visit)
    {
        if (element == null) return;
        visit(element);
        int count = VisualTreeHelper.GetChildrenCount(element);
        for (int i = 0; i < count; i++)
            Walk(VisualTreeHelper.GetChild(element, i), visit);
    }

    static void ScanSheet(FrameworkElement surface)
    {
        Walk(surface, delegate(DependencyObject node)
        {
            var button = node as Button;
            if (button != null && button.IsVisible)
            {
                Check(button.ActualWidth >= 31.5 && button.ActualHeight >= 31.5,
                    "button '" + (button.Content ?? button.ToolTip) + "' is smaller than 32x32 (" +
                    button.ActualWidth.ToString("0") + "x" + button.ActualHeight.ToString("0") + ")");
                return;
            }
            var text = node as TextBlock;
            if (text == null || !text.IsVisible || string.IsNullOrEmpty(text.Text)) return;
            if (text.TextWrapping == TextWrapping.Wrap || text.TextTrimming != TextTrimming.None) return;
            if (text.ActualWidth <= 0) return;
            // DesiredSize is clamped by the parent constraint, so measure the text
            // itself to notice a label that simply does not fit its box.
            var formatted = new FormattedText(text.Text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
                text.FontSize, Brushes.Black, VisualTreeHelper.GetDpi(text).PixelsPerDip);
            Check(formatted.WidthIncludingTrailingWhitespace <= text.ActualWidth + 1.5,
                "text clipped without ellipsis: '" + text.Text + "'");
        });
    }

    static void Render(FrameworkElement surface, string name, double scale)
    {
        surface.UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(surface.ActualWidth * scale),
            (int)Math.Ceiling(surface.ActualHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(name)) encoder.Save(file);
    }

    // ------------------------------------------------------------------- main

    [STAThread] static int Main(string[] args)
    {
        try
        {
            string source = File.ReadAllText("Ui/DesignSystem.cs");
            Dictionary<string, string> palette = ReadPalette(source);
            if (!palette.ContainsKey("OnBrand")) palette["OnBrand"] = "FFFFFF";
            Check(palette.Count >= 15, "palette lost tokens: " + palette.Count);

            // Tokens only: migrated files must not introduce literal colours.
            foreach (string file in Migrated)
            {
                string text = File.ReadAllText(file);
                int literals = Regex.Matches(text, @"(?:Brush|Hex)\(""#").Count;
                if (file.EndsWith("DesignSystem.cs", StringComparison.Ordinal))
                    Check(literals >= 1, "DesignSystem should be the only file holding literal colours");
                else
                    Check(literals == 0, file + " still contains literal colours: " + literals);
            }
            foreach (string file in Directory.GetFiles(".", "*.cs").Select(Path.GetFileName))
            {
                if (Migrated.Contains(file)) continue;
                int literals = Regex.Matches(File.ReadAllText(file), @"(?:Brush|Hex)\(""#").Count;
                if (literals > 0) Notes.Add("pending migration: " + file + " (" + literals + " colours)");
            }

            // WCAG AA for the pairs the UI actually renders text on.
            CheckContrast(palette, "Ink", "Surface", 7.0);
            CheckContrast(palette, "Ink", "Canvas", 7.0);
            CheckContrast(palette, "InkMuted", "Surface", 4.5);
            CheckContrast(palette, "InkMuted", "SurfaceSubtle", 4.5);
            CheckContrast(palette, "BrandText", "Surface", 4.5);
            CheckContrast(palette, "BrandText", "BrandTint", 4.5);
            CheckContrast(palette, "OnBrand", "Brand", 4.5);
            CheckContrast(palette, "Success", "SuccessTint", 4.5);
            CheckContrast(palette, "Warning", "WarningTint", 4.5);
            CheckContrast(palette, "Danger", "DangerTint", 4.5);

            // Component sheet.
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var application = new Application();
            UIElement surface = Sheet(assembly);
            var window = new Window
            {
                Content = surface,
                Width = 760,
                Height = 940,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = false,
                Topmost = true,
                Left = 0,
                Top = 0,
                Background = Brushes.Transparent
            };
            Type ui = LoadType(assembly, "Ui");
            ui.GetMethod("ApplyWindow", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, new object[] { window });
            ui.GetMethod("InstallTextInputStyles", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public)
                .Invoke(null, new object[] { window });
            window.Show();
            Pump();
            window.UpdateLayout();
            Pump();

            var element = (FrameworkElement)surface;
            Directory.CreateDirectory("tmp/tests/design");
            ScanSheet(element);
            Render(element, "tmp/tests/design/components-100.png", 1);
            Render(element, "tmp/tests/design/components-150.png", 1.5);
            Render(element, "tmp/tests/design/components-200.png", 2);
            window.Close();

            foreach (string note in Notes) Console.WriteLine("NOTE " + note);
            Console.WriteLine("DESIGN tokens=" + palette.Count +
                " contrast=AA hit-target=32 sheet=100/150/200 failures=" + failures);
            return failures == 0 ? 0 : 1;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }
}
