using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

internal static class PopupInteractionProbe
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length < 1 || args.Length > 4) return 2;
        Window popup = null;
        Window settingsWindow = null;
        try
        {
            Assembly app = Assembly.LoadFrom(args[0]);
            Type popupType = app.GetType("GlobalTranslator.PopupWindow", true);
            popup = (Window)Activator.CreateInstance(popupType, true);
            TextBox source = (TextBox)popupType
                .GetField("_source", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            TextBox translation = (TextBox)popupType
                .GetField("_translation", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);

            string longText = new string('源', 2400);
            source.Text = longText;
            translation.Text = new string('译', 3200);
            Require(source.Text.Length == 2400, "Source text was truncated.");
            Require(translation.Text.Length == 3200, "Translation text was truncated.");
            Require(source.IsReadOnly && source.IsReadOnlyCaretVisible,
                "Source text is not selectable read-only text.");
            Require(translation.IsReadOnly && translation.IsReadOnlyCaretVisible,
                "Translation text is not selectable read-only text.");
            Require(source.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Source scrollbar is not automatic.");
            Require(translation.VerticalScrollBarVisibility == ScrollBarVisibility.Auto,
                "Translation scrollbar is not automatic.");
            Require(popup.ResizeMode == ResizeMode.CanResize,
                "Popup resize mode is disabled.");

            Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
            Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
            Type ocrResultType =
                app.GetType("GlobalTranslator.OcrRecognitionResult", true);
            object settings = Activator.CreateInstance(settingsType, true);
            object client = Activator.CreateInstance(clientType, true);
            object ocrResult = Activator.CreateInstance(ocrResultType, true);
            settingsType.GetField("Provider").SetValue(settings, "Microsoft");
            ocrResultType.GetField("Text").SetValue(
                ocrResult, "可编辑的 OCR 原文");
            ocrResultType.GetField("Engine").SetValue(
                ocrResult, "Windows OCR · 本地增强");
            popupType.GetMethod("TranslateOcr").Invoke(
                popup, new[] { ocrResult, (object)160, (object)160, settings, client });
            Require(!source.IsReadOnly,
                "OCR source text was not made editable.");
            string[] actionFields = { "_copySource", "_retranslate", "_recapture" };
            foreach (string field in actionFields)
            {
                Button button = (Button)popupType
                    .GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(popup);
                Require(button.Visibility == Visibility.Visible,
                    field + " is not visible in OCR mode.");
            }
            Require(popupType.GetEvent("OcrRecaptureRequested") != null,
                "OCR recapture event is missing.");
            settingsType.GetField("Provider")
                .SetValue(settings, "ModelApi");
            settingsType.GetField("ModelVendor")
                .SetValue(settings, "DeepSeek");
            settingsType.GetField("DeepSeekModelApiKey")
                .SetValue(settings, "popup-deepseek-key");
            settingsType.GetField("DeepSeekModelName")
                .SetValue(
                    settings,
                    "deepseek-a-very-long-model-name-for-popup-layout-validation");
            popupType.GetMethod(
                "RefreshModelSelector",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(popup, new[] { settings });
            FrameworkElement modelChipHost = (FrameworkElement)popupType
                .GetField(
                    "_modelChipHost",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            Button popupModelButton = (Button)popupType
                .GetField(
                    "_modelButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            TextBlock popupModelButtonText = (TextBlock)popupType
                .GetField(
                    "_modelButtonText",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            Popup popupModelMenu = (Popup)popupType
                .GetField(
                    "_modelPopup",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            StackPanel popupModelItems = (StackPanel)popupType
                .GetField(
                    "_modelMenuItems",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            int availableModelItems = 0;
            bool containsMiMoOrQwen = false;
            bool containsCustom = false;
            bool containsManageModels = false;
            foreach (UIElement rawItem in popupModelItems.Children)
            {
                Button menuItem = rawItem as Button;
                if (menuItem == null) continue;
                if (string.Equals(
                    menuItem.Tag as string,
                    "ManageModels",
                    StringComparison.Ordinal))
                {
                    containsManageModels = true;
                    continue;
                }
                if (menuItem.Tag != null)
                {
                    availableModelItems++;
                    string display = menuItem.Tag.ToString();
                    if (display.IndexOf(
                            "自定义",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        containsCustom = true;
                    if (display.IndexOf(
                            "MiMo",
                            StringComparison.OrdinalIgnoreCase) >= 0 ||
                        display.IndexOf(
                            "Qwen",
                            StringComparison.OrdinalIgnoreCase) >= 0)
                        containsMiMoOrQwen = true;
                }
            }
            Require(
                modelChipHost.Visibility == Visibility.Visible &&
                modelChipHost.HorizontalAlignment ==
                    HorizontalAlignment.Right &&
                popupModelButton != null &&
                popupModelButton.Height == 32 &&
                popupModelButton.MaxWidth == 315 &&
                popupModelMenu.AllowsTransparency &&
                !popupModelMenu.StaysOpen &&
                availableModelItems == 1 &&
                !containsCustom &&
                !containsMiMoOrQwen &&
                containsManageModels &&
                popupModelButtonText.Text.IndexOf(
                    "DeepSeek",
                    StringComparison.OrdinalIgnoreCase) >= 0,
                "Popup usable-model menu is missing, unfiltered, or not synchronized.");
            settingsType.GetField("CustomModelBaseUrl")
                .SetValue(settings, "http://127.0.0.1:11434/v1");
            popupType.GetMethod(
                "RefreshModelSelector",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(popup, new[] { settings });
            availableModelItems = 0;
            containsCustom = false;
            foreach (UIElement rawItem in popupModelItems.Children)
            {
                Button menuItem = rawItem as Button;
                if (menuItem == null ||
                    menuItem.Tag == null ||
                    menuItem.Tag is string)
                    continue;
                availableModelItems++;
                string display = menuItem.Tag.ToString();
                if (display.IndexOf(
                        "自定义",
                        StringComparison.OrdinalIgnoreCase) >= 0)
                    containsCustom = true;
            }
            Require(
                availableModelItems == 2 &&
                containsCustom,
                "A local keyless custom model was not shown.");
            Require(popupType.GetEvent("ModelSettingsRequested") != null,
                "Popup model settings event is missing.");
            settingsType.GetField("DeepSeekModelName")
                .SetValue(settings, "deepseek-v4-flash");

            Type settingsWindowType =
                app.GetType("GlobalTranslator.SettingsWindow", true);
            settingsWindow = (Window)Activator.CreateInstance(
                settingsWindowType,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { settings },
                null);
            TabControl tabs = (TabControl)settingsWindowType
                .GetField("_tabs", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(tabs.Items.Count == 4,
                "Translation, OCR, shortcut or general settings tab is missing.");
            TabItem translationTab =
                (TabItem)tabs.Items[0];
            Require(
                translationTab.Content is Grid,
                "Translation page still uses one long page-level scroll view.");
            Grid providerPicker = (Grid)settingsWindowType
                .GetField(
                    "_providerPickerGrid",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(
                providerPicker.ColumnDefinitions.Count == 5 &&
                providerPicker.Children.Count == 5,
                "All five translation providers are not visible in the quick selector.");
            FieldInfo pendingProviderField = settingsWindowType.GetField(
                "_pendingProvider",
                BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo activeConfigField = settingsWindowType.GetField(
                "_activeConfigSection",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "ModelApi",
                "Saved provider was not loaded as the single pending provider.");
            Button googleFreeButton = (Button)settingsWindowType
                .GetField(
                    "_googleFree",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            googleFreeButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "GoogleFree",
                "Set-current action did not update the pending provider.");
            Require(
                (string)activeConfigField.GetValue(settingsWindow) ==
                    "Free",
                "Provider selection did not open its configuration section.");
            Button officialConfigButton = (Button)settingsWindowType
                .GetField(
                    "_officialConfigButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            officialConfigButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "GoogleFree" &&
                (string)activeConfigField.GetValue(settingsWindow) ==
                    "Official",
                "Browsing a configuration section changed the pending provider.");
            settingsWindowType.GetMethod("ShowModelSettings")
                .Invoke(settingsWindow, null);
            Require(tabs.SelectedIndex == 0,
                "Model settings shortcut did not select the translation tab.");
            Require(
                (string)activeConfigField.GetValue(settingsWindow) ==
                    "Model",
                "Model settings shortcut did not open the AI configuration section.");
            TextBox translateHotkey = (TextBox)settingsWindowType
                .GetField(
                    "_translateHotkey",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            TextBox ocrHotkey = (TextBox)settingsWindowType
                .GetField(
                    "_ocrHotkey",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            TextBox settingsHotkey = (TextBox)settingsWindowType
                .GetField(
                    "_settingsHotkey",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(
                !string.IsNullOrWhiteSpace(translateHotkey.Text) &&
                !string.IsNullOrWhiteSpace(ocrHotkey.Text) &&
                !string.IsNullOrWhiteSpace(settingsHotkey.Text),
                "One or more hotkey recorders are empty.");
            ComboBox modelVendor = (ComboBox)settingsWindowType
                .GetField(
                    "_modelVendor",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(modelVendor.Items.Count >= 4,
                "AI vendor presets are incomplete.");
            modelVendor.SelectedIndex = 1;
            TextBox modelName = (TextBox)settingsWindowType
                .GetField(
                    "_modelName",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            TextBox modelBase = (TextBox)settingsWindowType
                .GetField(
                    "_modelBaseUrl",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            PasswordBox modelApiKey = (PasswordBox)
                settingsWindowType.GetField(
                    "_modelApiKey",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(
                modelBase.Text == "https://api.deepseek.com" &&
                modelName.Text == "deepseek-v4-flash",
                "DeepSeek vendor preset did not populate defaults.");
            modelApiKey.Password = "deepseek-secret";
            modelVendor.SelectedIndex = 2;
            Require(
                modelBase.Text ==
                    "https://api.xiaomimimo.com/v1" &&
                modelName.Text == "mimo-v2.5" &&
                modelApiKey.Password == "",
                "MiMo vendor preset did not populate defaults.");
            modelApiKey.Password = "mimo-secret";
            modelVendor.SelectedIndex = 1;
            Require(
                modelApiKey.Password == "deepseek-secret",
                "DeepSeek API key was not restored independently.");
            modelVendor.SelectedIndex = 2;
            Require(
                modelApiKey.Password == "mimo-secret",
                "MiMo API key was not restored independently.");
            modelVendor.SelectedIndex = 3;
            Require(
                modelBase.Text ==
                    "https://dashscope.aliyuncs.com/compatible-mode/v1" &&
                modelName.Text == "qwen-plus" &&
                modelApiKey.Password == "",
                "Qwen vendor preset did not populate defaults.");
            settingsWindowType.GetMethod(
                "LoadValues",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(settingsWindow, null);
            ComboBox ocrLanguage = (ComboBox)settingsWindowType
                .GetField("_ocrLanguage", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(ocrLanguage.Items.Count >= 1,
                "OCR language choices are empty.");
            TextBlock englishStatus = (TextBlock)settingsWindowType
                .GetField(
                    "_englishOcrStatus",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Button installEnglish = (Button)settingsWindowType
                .GetField(
                    "_installEnglishOcr",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Button copyEnglishCommand = (Button)settingsWindowType
                .GetField(
                    "_copyEnglishOcrCommand",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(!string.IsNullOrWhiteSpace(englishStatus.Text),
                "English OCR status is empty.");
            Require(
                installEnglish.Visibility == copyEnglishCommand.Visibility,
                "English OCR install actions have inconsistent visibility.");
            Button cancelSettings = (Button)settingsWindowType
                .GetField(
                    "_cancelSettings",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(
                string.Equals(cancelSettings.Content as string, "取消"),
                "Settings cancel button is missing.");
            TextBox modelBaseUrl = (TextBox)settingsWindowType
                .GetField(
                    "_modelBaseUrl",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            string savedModelBaseUrl = modelBaseUrl.Text;
            modelBaseUrl.Text = "unsaved-probe-value";
            settingsWindow.Show();
            cancelSettings.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(!settingsWindow.IsVisible,
                "Settings cancel button did not hide the window.");
            Require(modelBaseUrl.Text == savedModelBaseUrl,
                "Settings cancel button did not discard unsaved changes.");

            popup.Left = 120;
            popup.Top = 120;
            popup.Width = popup.MinWidth;
            popup.Show();
            popup.UpdateLayout();
            Require(
                popupModelButton.ActualWidth <= 315.5 &&
                modelChipHost.ActualWidth <=
                    popup.ActualWidth - 36,
                "Model chip overflowed the minimum popup width.");

            MethodInfo hitTest = popupType.GetMethod(
                "WindowProc", BindingFlags.Instance | BindingFlags.NonPublic);
            int x = (int)(popup.Left + popup.ActualWidth - 2);
            int y = (int)(popup.Top + popup.ActualHeight - 2);
            long packed = ((long)(ushort)y << 16) | (ushort)x;
            object[] call = {
                IntPtr.Zero, 0x0084, IntPtr.Zero, new IntPtr(packed), false
            };
            IntPtr result = (IntPtr)hitTest.Invoke(popup, call);
            Require(result.ToInt32() == 17 && (bool)call[4],
                "Bottom-right resize hit test failed.");

            Button collapseButton = (Button)popupType
                .GetField(
                    "_collapseButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            Button expandButton = (Button)popupType
                .GetField(
                    "_expandButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            FrameworkElement resizeGrip = (FrameworkElement)popupType
                .GetField(
                    "_resizeGrip",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(popup);
            Require(
                collapseButton.Width == 30 &&
                expandButton.Width == 30 &&
                collapseButton.BorderThickness.Left == 0 &&
                expandButton.BorderThickness.Left == 0 &&
                collapseButton.Content is Path &&
                expandButton.Content is Path,
                "Modern ghost window controls are missing.");
            Canvas resizeDots = resizeGrip as Canvas;
            Require(
                resizeDots != null &&
                resizeDots.Children.Count == 3,
                "Modern resize grip dots are missing.");

            popup.WindowState = WindowState.Maximized;
            Require(popup.WindowState == WindowState.Maximized,
                "Popup could not maximize.");
            Require(
                string.Equals(
                    expandButton.ToolTip as string,
                    "还原",
                    StringComparison.Ordinal),
                "Expand control did not switch to restore state.");
            popup.WindowState = WindowState.Normal;
            Require(popup.WindowState == WindowState.Normal,
                "Popup could not restore.");

            if (args.Length >= 2)
            {
                source.Text =
                    "Sharkey makes translation feel calm and effortless.";
                translation.Text =
                    "鲨译让翻译体验更加轻松、自然。";
                translation.Foreground =
                    (Brush)new BrushConverter().ConvertFromString(
                        "#0B2942");
                popup.Width = 480;
                popup.Height = 520;
                popup.UpdateLayout();
                int renderWidth =
                    (int)Math.Ceiling(popup.ActualWidth * 1.5);
                int renderHeight =
                    (int)Math.Ceiling(popup.ActualHeight * 1.5);
                var bitmap = new RenderTargetBitmap(
                    renderWidth,
                    renderHeight,
                    144,
                    144,
                    PixelFormats.Pbgra32);
                bitmap.Render(popup);
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var output =
                    System.IO.File.Create(args[1]))
                    encoder.Save(output);
            }
            if (args.Length >= 3)
            {
                popupModelMenu.IsOpen = true;
                popupModelMenu.Child.Dispatcher.Invoke(
                    new Action(delegate
                    {
                        popupModelMenu.Child.Measure(
                            new Size(310, 600));
                        popupModelMenu.Child.Arrange(
                            new Rect(
                                new Point(0, 0),
                                popupModelMenu.Child.DesiredSize));
                        popupModelMenu.Child.UpdateLayout();
                    }));
                int menuWidth = (int)Math.Ceiling(
                    popupModelMenu.Child.RenderSize.Width * 1.5);
                int menuHeight = (int)Math.Ceiling(
                    popupModelMenu.Child.RenderSize.Height * 1.5);
                var menuBitmap = new RenderTargetBitmap(
                    menuWidth,
                    menuHeight,
                    144,
                    144,
                    PixelFormats.Pbgra32);
                menuBitmap.Render(popupModelMenu.Child);
                var menuEncoder = new PngBitmapEncoder();
                menuEncoder.Frames.Add(
                    BitmapFrame.Create(menuBitmap));
                using (var output =
                    System.IO.File.Create(args[2]))
                    menuEncoder.Save(output);
                popupModelMenu.IsOpen = false;
            }
            if (args.Length >= 4)
            {
                settingsWindow.Show();
                tabs.SelectedIndex = 0;
                settingsWindow.Width = 760;
                settingsWindow.Height = 820;
                settingsWindow.UpdateLayout();
                SaveWindowPreview(
                    settingsWindow, args[3], 96);
                string previewFolder =
                    System.IO.Path.GetDirectoryName(args[3]);
                string previewName =
                    System.IO.Path.GetFileNameWithoutExtension(
                        args[3]);
                SaveWindowPreview(
                    settingsWindow,
                    System.IO.Path.Combine(
                        previewFolder,
                        previewName + "-150.png"),
                    144);
                SaveWindowPreview(
                    settingsWindow,
                    System.IO.Path.Combine(
                        previewFolder,
                        previewName + "-200.png"),
                    192);
                settingsWindow.Hide();
            }

            Console.WriteLine(
                "POPUP source={0} translation={1} resizeHit={2} selectable=True",
                source.Text.Length, translation.Text.Length, result.ToInt32());
            ((IDisposable)client).Dispose();
            return 0;
        }
        catch (Exception ex)
        {
            while (ex is TargetInvocationException && ex.InnerException != null)
                ex = ex.InnerException;
            string message;
            try { message = ex.Message; }
            catch { message = "(message unavailable)"; }
            Console.Error.WriteLine(
                "FAILED: " + ex.GetType().FullName + ": " + message);
            return 1;
        }
        finally
        {
            if (settingsWindow != null) settingsWindow.Hide();
            if (popup != null) popup.Close();
        }
    }

    private static void SaveWindowPreview(
        Window window, string path, double dpi)
    {
        double scale = dpi / 96.0;
        int width = Math.Max(
            1,
            (int)Math.Ceiling(
                window.ActualWidth * scale));
        int height = Math.Max(
            1,
            (int)Math.Ceiling(
                window.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output =
            System.IO.File.Create(path))
            encoder.Save(output);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
