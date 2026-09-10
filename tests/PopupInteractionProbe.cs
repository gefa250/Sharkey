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
            Require(tabs.TabStripPlacement == Dock.Left,
                "Settings navigation is not placed on the left.");
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
                providerPicker.ColumnDefinitions.Count == 3 &&
                providerPicker.Children.Count == 3,
                "Translation engine types are not presented as three top-level choices.");
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
            ComboBox targetLanguage = (ComboBox)settingsWindowType
                .GetField(
                    "_language",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(
                targetLanguage.SelectedIndex == 0,
                "Smart target is not the default target mode.");
            Button officialConfigButton = (Button)settingsWindowType
                .GetField(
                    "_officialConfigButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            officialConfigButton.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "ModelApi" &&
                (string)activeConfigField.GetValue(settingsWindow) ==
                    "Official",
                "Browsing a configuration section changed the pending provider.");
            Button googleOfficial = (Button)settingsWindowType
                .GetField(
                    "_googleOfficial",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            googleOfficial.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "ModelApi",
                "Browsing Google Cloud changed the pending provider.");
            PasswordBox googleKey = (PasswordBox)
                settingsWindowType.GetField(
                    "_googleKey",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            googleKey.Password = "probe-google-key";
            Button activateProvider = (Button)settingsWindowType
                .GetField(
                    "_activateProviderButton",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(activateProvider.IsEnabled,
                "Configured Google Cloud service cannot be activated.");
            activateProvider.RaiseEvent(
                new RoutedEventArgs(Button.ClickEvent));
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "Google",
                "Explicit set-current action did not update the pending provider.");
            settingsWindowType.GetMethod("ShowModelSettings")
                .Invoke(settingsWindow, null);
            Require(tabs.SelectedIndex == 0,
                "Model settings shortcut did not select the translation tab.");
            Require(
                (string)activeConfigField.GetValue(settingsWindow) ==
                    "Model",
                "Model settings shortcut did not open the AI configuration section.");
            Require(
                (string)pendingProviderField.GetValue(settingsWindow) ==
                    "Google",
                "Model settings shortcut changed the pending provider.");
            var browseButtons =
                (System.Collections.IDictionary)
                settingsWindowType.GetField(
                    "_providerBrowseButtons",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(browseButtons.Count == 4,
                "AI model category does not expose four provider choices.");
            TextBlock headerEngine = (TextBlock)
                settingsWindowType.GetField(
                    "_headerCurrentEngine",
                    BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(!string.IsNullOrWhiteSpace(headerEngine.Text),
                "Current engine summary is empty.");
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
            CheckBox aiOcr = (CheckBox)settingsWindowType
                .GetField("_ocrAiFallback", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            TextBox visionModel = (TextBox)settingsWindowType
                .GetField("_ocrVisionModel", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(aiOcr != null && aiOcr.IsChecked == true,
                "DeepSeek Vision is not enabled by default.");
            Require(visionModel != null &&
                    visionModel.Text == "deepseek-v4-flash-vision-exp",
                "Default DeepSeek Vision model is missing.");
            CheckBox localFallback = (CheckBox)settingsWindowType
                .GetField("_ocrLocalFallback", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(settingsWindow);
            Require(localFallback != null && localFallback.IsChecked == true,
                "Optional Windows OCR fallback is not enabled by default.");
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

            VerifyReadingExperience(app);

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

    private static object Field(object instance, string name)
    {
        return instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(instance);
    }

    private static void SetField(object instance, string name, object value)
    {
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }

    private static object Invoke(object instance, string name, params object[] values)
    {
        return instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(instance, values);
    }

    private static void VerifyReadingExperience(Assembly app)
    {
        Window window = (Window)Activator.CreateInstance(app.GetType("GlobalTranslator.PopupWindow"), true);
        try
        {
            object settings = Activator.CreateInstance(app.GetType("GlobalTranslator.AppSettings"), true);
            Require((string)settings.GetType().GetField("PopupFontSize").GetValue(settings) == "Standard", "Old settings must default to standard font.");
            // Redirect only this test process's settings path. Never read or overwrite user credentials.
            FieldInfo settingsPath = settings.GetType().GetField("FilePath", BindingFlags.Static | BindingFlags.NonPublic);
            string originalPath = (string)settingsPath.GetValue(null);
            string testPath = System.IO.Path.GetFullPath("tmp/tests/reading-settings-" + Guid.NewGuid().ToString("N") + ".dat");
            try
            {
                settingsPath.SetValue(null, testPath);
                foreach (string preset in new[] { "Small", "Standard", "Large" })
                {
                    settings.GetType().GetField("PopupFontSize").SetValue(settings, preset);
                    Invoke(settings, "Save");
                    object loaded = settings.GetType().GetMethod("Load").Invoke(null, null);
                    Require((string)settings.GetType().GetField("PopupFontSize").GetValue(loaded) == preset, "Font preference did not round-trip: " + preset);
                }
            }
            finally
            {
                settingsPath.SetValue(null, originalPath);
                if (System.IO.File.Exists(testPath)) System.IO.File.Delete(testPath);
            }
            settings.GetType().GetField("Provider").SetValue(settings, "ModelApi");
            settings.GetType().GetField("ModelVendor").SetValue(settings, "DeepSeek");
            settings.GetType().GetField("DeepSeekModelApiKey").SetValue(settings, "test-only-no-request");
            Invoke(window, "RefreshModelSelector", settings);
            Invoke(window, "SetPinned", true);
            Invoke(window, "SetOcrDirty", true);
            Invoke(window, "SetOcrDirty", false);
            Require((bool)window.GetType().GetProperty("IsPinned").GetValue(window, null), "Completing edits cleared the manual pin.");
            Invoke(window, "SetPinned", false);
            Invoke(window, "SetOcrDirty", true);
            Require((bool)Field(window, "_editProtected") && !(bool)Field(window, "_manualPinned"), "Edit protection became a manual pin.");
            Invoke(window, "SetOcrDirty", false);
            Require(!(bool)window.GetType().GetProperty("IsPinned").GetValue(window, null), "Edit protection survived clearing the result.");

            TextBox text = (TextBox)Field(window, "_translation");
            TextBox source = (TextBox)Field(window, "_source");
            TextBlock meta = (TextBlock)Field(window, "_meta");
            meta.Text = "中文 → English";
            ((ProgressBar)Field(window, "_progress")).Visibility = Visibility.Collapsed;
            ((TextBlock)Field(window, "_loadingText")).Visibility = Visibility.Collapsed;
            Invoke(window, "SetOcrActionsVisible", false);
            window.Width = 440;
            window.Left = 100;
            window.Top = 50;
            window.Show();
            string paragraph = "You can search known problems or submit a new issue in the repository. " +
                "Keep the complete final line visible while reading, writing, working and debugging";
            foreach (string preset in new[] { "Small", "Standard", "Large" })
            {
                Invoke(window, "ApplyReadingFont", preset);
                window.Height = 190;
                text.Text = paragraph;
                SetField(window, "_translatedText", paragraph);
                Invoke(window, "UpdateContentLayout");
                Invoke(window, "UpdateResultHeight", paragraph);
                window.UpdateLayout();
                Invoke(window, "EnsureRenderedContentFit", 0);
                window.UpdateLayout();
                ScrollViewer scroll = (ScrollViewer)text.Template.FindName("PART_ContentHost", text);
                Require(scroll.ExtentHeight <= scroll.ViewportHeight + 1, "F8 last line still overflows at font " + preset);
                Require(text.Padding.Bottom >= 8, "Descender safety padding disappeared.");
                Require(((TextBlock)Field(window, "_translationLabel")).Visibility == Visibility.Collapsed, "F8 still displays a redundant heading.");
                Require(((Border)Field(window, "_translationCard")).BorderThickness.Left == 0, "F8 still has a nested text border.");
            }
            double height = window.Height;
            double top = window.Top;
            Invoke(window, "UpdateResultHeight", "short");
            Require(window.Height >= height && Math.Abs(window.Top - top) < 1, "Streaming fit shrank or repositioned the card unnecessarily.");
            Invoke(window, "ApplyReadingFont", "Standard");
            window.Width = 360;
            window.UpdateLayout();
            Require(((Button)Field(window, "_modelButton")).ActualWidth <= 145.5, "F8 model chip overflows narrow toolbar.");
            window.Width = 440;
            window.Height = 190;
            Invoke(window, "UpdateResultHeight", paragraph);
            Invoke(window, "EnsureRenderedContentFit", 0);
            window.UpdateLayout();
            SaveWindowPreview(window, "tmp/tests/reading-f8-100.png", 96);
            SaveWindowPreview(window, "tmp/tests/reading-f8-150.png", 144);
            SaveWindowPreview(window, "tmp/tests/reading-f8-200.png", 192);

            FieldInfo mode = window.GetType().GetField("_popupMode", BindingFlags.NonPublic | BindingFlags.Instance);
            mode.SetValue(window, Enum.Parse(mode.FieldType, "Ocr"));
            SetField(window, "_ocrMode", true);
            Invoke(window, "SetOcrActionsVisible", true);
            source.Text = "原文完整显示。可以拖动中间的分隔条，为译文分配更多空间。";
            source.IsReadOnly = false;
            source.Visibility = Visibility.Visible;
            ((TextBlock)Field(window, "_sourceSummary")).Visibility = Visibility.Collapsed;
            window.Width = 560;
            window.Height = 480;
            Invoke(window, "UpdateContentLayout");
            window.UpdateLayout();
            Grid grid = (Grid)Field(window, "_contentGrid");
            GridSplitter divider = (GridSplitter)Field(window, "_divider");
            Require(grid.RowDefinitions.Count == 3 && divider.ResizeDirection == GridResizeDirection.Rows, "F9 vertical divider is absent.");
            string longResult = new string('译', 1600);
            text.Text = longResult;
            SetField(window, "_translatedText", longResult);
            Invoke(window, "UpdateResultHeight", longResult);
            window.UpdateLayout();
            Invoke(window, "EnsureRenderedContentFit", 0);
            window.UpdateLayout();
            Require(window.Height > 480 && window.Height <= (double)Invoke(window, "GetOcrHeightLimit") + 1, "F9 failed to expand within its screen limit.");
            text.ScrollToEnd();
            window.UpdateLayout();
            ScrollViewer longScroll = (ScrollViewer)text.Template.FindName("PART_ContentHost", text);
            Require(longScroll.VerticalOffset + longScroll.ViewportHeight >= longScroll.ExtentHeight - 1, "Long F9 result cannot scroll to its final line.");
            text.Text = paragraph;
            SetField(window, "_translatedText", paragraph);
            text.ScrollToHome();
            window.Height = 480;
            window.UpdateLayout();
            divider.RaiseEvent(new DragStartedEventArgs(0, 0) { RoutedEvent = Thumb.DragStartedEvent });
            grid.RowDefinitions[0].Height = new GridLength(25, GridUnitType.Star);
            grid.RowDefinitions[2].Height = new GridLength(75, GridUnitType.Star);
            window.UpdateLayout();
            divider.RaiseEvent(new DragCompletedEventArgs(0, 0, false) { RoutedEvent = Thumb.DragCompletedEvent });
            double share = (double)Field(window, "_ocrSourceShare");
            height = window.Height;
            Invoke(window, "UpdateResultHeight", new string('译', 5000));
            Invoke(window, "UpdateContentLayout");
            Require(Math.Abs(share - 25) < 1 && (double)Field(window, "_ocrSourceShare") == share && window.Height == height,
                "Automatic fit overwrote the user's divider allocation.");
            SaveWindowPreview(window, "tmp/tests/reading-f9-100.png", 96);
            window.Width = 800;
            window.UpdateLayout();
            Require(grid.ColumnDefinitions.Count == 3 && divider.ResizeDirection == GridResizeDirection.Columns, "Wide F9 divider is absent.");
            SaveWindowPreview(window, "tmp/tests/reading-f9-wide-150.png", 144);
            SaveWindowPreview(window, "tmp/tests/reading-f9-wide-200.png", 192);

            // Exercise real request entry/success/failure paths without network access.
            object client = Activator.CreateInstance(app.GetType("GlobalTranslator.TranslationClient"), true);
            try
            {
                settings.GetType().GetField("Provider").SetValue(settings, "Microsoft");
                object bounds = Activator.CreateInstance(app.GetType("GlobalTranslator.NativeMethods+RECT"), true);
                Invoke(window, "SetOcrDirty", true);
                Invoke(window, "BeginTranslation", "edited words", 100, 100, bounds, settings, client, true, "", false);
                Require((bool)Field(window, "_editProtected"), "Failed retranslation cleared edit protection.");
                Require(((StackPanel)Field(window, "_errorPanel")).Visibility == Visibility.Visible, "Missing key did not enter an error state.");
                Invoke(window, "BeginTranslation", "123", 100, 100, bounds, settings, client, true, "", false);
                Require(text.Text == "123" && !(bool)Field(window, "_editProtected"), "Successful retranslation did not release edit protection.");
                Invoke(window, "SetOcrDirty", true);
                Invoke(window, "Translate", "456", 100, 100, settings, client);
                Require(!(bool)Field(window, "_editProtected") && !(bool)Field(window, "_manualLayout"), "New F8 inherited OCR protection or manual layout.");
            }
            finally { ((IDisposable)client).Dispose(); }
            Console.WriteLine("READING font=3 presets pin=isolated divider=preserved fit=rendered toolbar=compact");
        }
        finally { window.Close(); }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
