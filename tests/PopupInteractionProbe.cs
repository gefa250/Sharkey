using System;
using System.Reflection;
using File = System.IO.File;
using Directory = System.IO.Directory;
using Path = System.IO.Path;
using ShapePath = System.Windows.Shapes.Path;
using StreamWriter = System.IO.StreamWriter;
using System.IO.Compression;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

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
            ProbeCommerceDocuments(app);
            app.GetType("GlobalTranslator.ConversationStore", true)
                .GetField("DirectoryPath", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Sharkey-tests-" + Guid.NewGuid().ToString("N")));
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
            Type tableWindowType = app.GetType("GlobalTranslator.ImageTableWindow", true);
            var tableWindow = (Window)Activator.CreateInstance(tableWindowType, new object[] { settings, client, (Action)delegate { }, null });
            var tableData = new System.Data.DataTable("费用明细");
            tableData.Columns.Add("列 1"); tableData.Columns.Add("列 2"); tableData.Columns.Add("列 3");
            tableData.Rows.Add("项目", "数量", "金额 USD");
            tableData.Rows.Add("产品 A", "0012", "120.00");
            tableData.Rows.Add("运费", "1", "15.50");
            var tablePicker = (ComboBox)Field(tableWindow, "_sheets");
            tablePicker.ItemsSource = new[] { tableData }; tablePicker.SelectedIndex = 0;
            tableWindow.Show(); tableWindow.UpdateLayout();
            Require(((DataGrid)Field(tableWindow, "_grid")).Items.Count == 3, "Table grid lost rows.");
            SaveWindowPreview(tableWindow, "tmp/tests/image-table.png", 96);
            tableWindow.Width = 520; tableWindow.Height = 440; tableWindow.UpdateLayout();
            Require(((DataGrid)Field(tableWindow, "_grid")).ActualHeight > 100, "Small table viewport collapsed.");
            tableWindow.Close();
            Type writingType = app.GetType("GlobalTranslator.WritingWindow", true);
            Window writing = (Window)Activator.CreateInstance(writingType, new object[] { settings, client, null });
            var writingSource = (TextBox)writingType.GetField("_intent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(writing);
            var writingResult = (TextBox)writingType.GetField("_result", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(writing);
            var writingCopy = (Button)writingType.GetField("_copy", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(writing);
            writingSource.Text = "请确认交期"; writingResult.Text = "Please confirm the delivery date.";
            Require(writingCopy.IsEnabled, "Completed writing result cannot be copied.");
            writing.Show(); writing.UpdateLayout(); PumpDispatcher(); writing.UpdateLayout();
            SaveWindowPreview(writing, "tmp/tests/writing-workspace.png", 96);
            SaveWindowPreview(writing, "tmp/tests/writing-workspace-150.png", 144);
            SaveWindowPreview(writing, "tmp/tests/writing-workspace-200.png", 192);
            writing.Width = 540; writing.Height = 480; writing.UpdateLayout();
            Require(writingSource.ActualHeight > 35,
                "Communication input collapsed at minimum size.");
            Require(((Border)Field(writing, "_chatSidebar")).Visibility == Visibility.Collapsed,
                "Narrow assistant did not collapse the sidebar.");
            writingType.GetField("_showingReply", BindingFlags.Instance |
                BindingFlags.NonPublic).SetValue(writing, true);
            writingType.GetMethod("UpdateWorkbenchLayout", BindingFlags.Instance |
                BindingFlags.NonPublic).Invoke(writing, null);
            writing.UpdateLayout();
            Require(writingResult.ActualHeight > 35,
                "Communication result collapsed in narrow layout.");
            SaveWindowPreview(writing, "tmp/tests/writing-workspace-small.png", 96);
            writingSource.Text = "请确认数量";
            Require(writingResult.Text.Length > 0 && writingCopy.IsEnabled, "Changing input erased a successful reply.");
            var addImage = writingType.GetMethod("AddImage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var moveImage = writingType.GetMethod("MoveImage",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var images = (System.Collections.IList)writingType.GetField("_images",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(writing);
            for (int i = 0; i < 6; i++)
            {
                using (var bitmap = new System.Drawing.Bitmap(20, 20))
                using (var stream = new System.IO.MemoryStream())
                {
                    bitmap.SetPixel(0, 0, System.Drawing.Color.FromArgb(10 + i, 20, 30));
                    bitmap.Save(stream, System.Drawing.Imaging.ImageFormat.Png);
                    addImage.Invoke(writing, new object[] { stream.ToArray() });
                }
            }
            Require(images.Count == 5, "Communication image limit is not five.");
            object firstImage = images[0];
            moveImage.Invoke(writing, new object[] { 0, 1 });
            Require(ReferenceEquals(firstImage, images[1]),
                "Communication image reorder changed the wrong item.");
            ProbeAssistantExperience(app, writing, writingType, settings, client);
            writing.Close();
            object ocrResult = Activator.CreateInstance(ocrResultType, true);
            settingsType.GetField("Provider").SetValue(settings, "Microsoft");
            ocrResultType.GetField("Text").SetValue(
                ocrResult, "可编辑的 OCR 原文");
            ocrResultType.GetField("Engine").SetValue(
                ocrResult, "AI 视觉识别");
            popupType.GetMethod("TranslateOcr").Invoke(
                popup, new[] { ocrResult, (object)160, (object)160, settings, client });
            Require(!source.IsReadOnly,
                "OCR source text was not made editable.");
            string[] actionFields = { "_tableButton", "_retranslate", "_textTools" };
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
            settingsType.GetField("ModelVendor")
                .SetValue(settings, "Custom");
            settingsType.GetField("CustomModelName")
                .SetValue(settings, "popup-local-model");
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

            object settingsForSettingsWindow = settingsType.GetMethod("Copy")
                .Invoke(settings, null);
            settingsType.GetField("ModelVendor")
                .SetValue(settingsForSettingsWindow, "DeepSeek");
            TestModernSettingsWindow(app, settingsForSettingsWindow);

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
                collapseButton.Content is ShapePath &&
                expandButton.Content is ShapePath,
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
                Type settingsWindowType = app.GetType(
                    "GlobalTranslator.SettingsWindow", true);
                settingsWindow = (Window)Activator.CreateInstance(
                    settingsWindowType,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { settings },
                    null);
                settingsWindow.Show();
                Invoke(settingsWindow, "ShowModelSettings");
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
            Console.Error.WriteLine(ex.StackTrace);
            return 1;
        }
        finally
        {
            if (settingsWindow != null) settingsWindow.Hide();
            if (popup != null) popup.Close();
        }
    }

    private static void TestModernSettingsWindow(Assembly app, object settings)
    {
        Type type = app.GetType("GlobalTranslator.SettingsWindow", true);
        Window window = (Window)Activator.CreateInstance(type,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null, new[] { settings }, null);
        try
        {
            Require(window.ResizeMode == ResizeMode.CanResize &&
                Math.Abs(window.Width - 960) < 1 && Math.Abs(window.Height - 720) < 1,
                "Modern settings default size or resize behavior is incorrect.");
            StackPanel navigation = (StackPanel)Field(window, "_navigation");
            Require(navigation.Children.Count == 4,
                "Settings navigation must contain AI model, assistant preference, shortcuts, and general.");
            Require((string)Field(window, "_selectedPage") == "Model",
                "Settings do not open on the AI model page.");
            ComboBox target = (ComboBox)Field(window, "_language");
            Require(target.SelectedIndex == 0,
                "Smart translation target is not the default.");
            ComboBox vendors = (ComboBox)Field(window, "_modelVendor");
            Require(vendors.Items.Count == 4,
                "Four independent AI provider profiles are missing.");
            var vendorButtons =
                (System.Collections.Generic.Dictionary<string, Button>)Field(window, "_vendorButtons");
            Require(vendorButtons.Count == 4,
                "Visible segmented provider selector is incomplete.");
            Require((string)Field(window, "_pendingModelVendor") == "DeepSeek",
                "Current AI profile was not loaded.");

            TextBox modelName = (TextBox)Field(window, "_modelName");
            TextBox baseUrl = (TextBox)Field(window, "_modelBaseUrl");
            PasswordBox apiKey = (PasswordBox)Field(window, "_modelApiKey");
            string savedModelName = modelName.Text;
            string savedModelKey = apiKey.Password;
            Require(modelName.Text == "deepseek-v4-flash" &&
                baseUrl.Text == "https://api.deepseek.com",
                "Saved DeepSeek profile was not loaded into the draft.");
            apiKey.Password = "probe-deepseek-key";
            vendorButtons["MiMo"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(baseUrl.Text == "https://api.xiaomimimo.com/v1" &&
                modelName.Text.Length == 0 && apiKey.Password.Length == 0,
                "MiMo draft did not remain independent from DeepSeek.");
            modelName.Text = "mimo-probe";
            apiKey.Password = "probe-mimo-key";
            vendorButtons["Qwen"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(baseUrl.Text == "https://dashscope.aliyuncs.com/compatible-mode/v1" &&
                apiKey.Password.Length == 0,
                "Qwen default connection profile was not isolated.");
            vendorButtons["DeepSeek"].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(apiKey.Password == "probe-deepseek-key",
                "Switching provider overwrote the independent DeepSeek draft.");

            Button cancel = (Button)Field(window, "_cancelSettings");
            Button checkUpdate = (Button)Field(window, "_checkUpdate");
            Require((string)cancel.Content == "取消" &&
                (string)checkUpdate.Content == "检查更新" &&
                Field(window, "_updateStatus") != null,
                "Fixed footer, update controls, or cancel action is missing.");
            window.Show();
            PumpDispatcher();
            window.UpdateLayout();
            modelName.ApplyTemplate();
            apiKey.ApplyTemplate();
            Require(modelName.Template.FindName("Chrome", modelName) is Border &&
                apiKey.Template.FindName("PART_ContentHost", apiKey) is ScrollViewer,
                "Rounded text and password input templates were not applied.");
            ScrollViewer modelContent = modelName.Template.FindName(
                "PART_ContentHost", modelName) as ScrollViewer;
            ScrollViewer keyContent = apiKey.Template.FindName(
                "PART_ContentHost", apiKey) as ScrollViewer;
            Require(modelContent != null && keyContent != null &&
                modelContent.Margin == new Thickness(0) &&
                keyContent.Margin == new Thickness(0),
                "Rounded text input template applies padding twice and clips field contents.");
            SaveWindowPreview(window, "tmp/tests/settings-ai-100.png", 96);
            SaveWindowPreview(window, "tmp/tests/settings-ai-150.png", 144);
            SaveWindowPreview(window, "tmp/tests/settings-ai-200.png", 192);
            window.Height = 560;
            window.UpdateLayout();
            Rect cancelBounds = cancel.TransformToAncestor(window)
                .TransformBounds(new Rect(cancel.RenderSize));
            Require(cancelBounds.Bottom <= window.ActualHeight + 1,
                "Settings footer is not visible at the minimum window height.");

            window.Height = 720;
            window.UpdateLayout();
            Invoke(window, "ShowOcrSettings");
            Require((string)Field(window, "_selectedPage") == "Assistant",
                "OCR guidance did not navigate to assistant preferences.");
            window.UpdateLayout();
            Grid contentHost = (Grid)Field(window, "_contentHost");
            bool assistantVisible = false;
            foreach (UIElement child in contentHost.Children)
            {
                FrameworkElement page = child as FrameworkElement;
                if (page != null && (string)page.Tag == "Assistant" &&
                    page.Visibility == Visibility.Visible) assistantVisible = true;
            }
            Require(assistantVisible, "Assistant page content did not become visible.");
            PumpDispatcher();
            ComboBox language = (ComboBox)Field(window, "_language");
            language.ApplyTemplate();
            Popup languagePopup = language.Template.FindName("PART_Popup", language) as Popup;
            Require(languagePopup != null,
                "Rounded language selector is missing its accessible popup part.");
            Require(!languagePopup.StaysOpen,
                "Rounded language selector must dismiss when focus moves outside it.");
            ToggleButton dropDownToggle = language.Template.FindName(
                "DropDownToggle", language) as ToggleButton;
            Require(dropDownToggle != null,
                "Rounded language selector arrow toggle is missing.");
            dropDownToggle.IsChecked = true;
            PumpDispatcher();
            window.UpdateLayout();
            Require(language.IsDropDownOpen && languagePopup.IsOpen &&
                language.Template.FindName("DropDownChrome", language) is Border,
                "Rounded language selector popup failed to open.");
            SaveWindowPreview(window,
                "tmp/tests/settings-assistant-dropdown-100.png", 96);
            language.IsDropDownOpen = false;
            PumpDispatcher();
            window.UpdateLayout();
            CheckBox ocrEnabled = (CheckBox)Field(window, "_ocrAiFallback");
            ocrEnabled.ApplyTemplate();
            Require(ocrEnabled.Template.FindName("CheckChrome", ocrEnabled) is Border &&
                ocrEnabled.Template.FindName("CheckMark", ocrEnabled) is ShapePath,
                "Rounded checkbox template was not applied.");
            Rect assistantCancelBounds = cancel.TransformToAncestor(window)
                .TransformBounds(new Rect(cancel.RenderSize));
            Button save = (Button)Field(window, "_saveButton");
            Rect assistantSaveBounds = save.TransformToAncestor(window)
                .TransformBounds(new Rect(save.RenderSize));
            Require(cancel.IsVisible && save.IsVisible &&
                assistantCancelBounds.Bottom <= window.ActualHeight + 1 &&
                assistantSaveBounds.Bottom <= window.ActualHeight + 1 &&
                assistantCancelBounds.Height >= 38 && assistantSaveBounds.Height >= 38,
                "Fixed settings actions disappeared or were clipped after navigating to assistant preferences.");
            SaveWindowPreview(window, "tmp/tests/settings-assistant-100.png", 96);
            SaveWindowPreview(window, "tmp/tests/settings-assistant-150.png", 144);
            SaveWindowPreview(window, "tmp/tests/settings-assistant-200.png", 192);
            MethodInfo constrainWindow = type.GetMethod("ApplyWorkAreaBounds",
                BindingFlags.Instance | BindingFlags.NonPublic);
            constrainWindow.Invoke(window, new object[] { 640.0, 420.0 });
            PumpDispatcher();
            window.UpdateLayout();
            Rect compactCancelBounds = cancel.TransformToAncestor(window)
                .TransformBounds(new Rect(cancel.RenderSize));
            Rect compactSaveBounds = save.TransformToAncestor(window)
                .TransformBounds(new Rect(save.RenderSize));
            StackPanel modelTestRow = (StackPanel)Field(window, "_modelTestRow");
            Require(window.ActualWidth <= 641 && window.ActualHeight <= 421 &&
                compactCancelBounds.Bottom <= window.ActualHeight + 1 &&
                compactSaveBounds.Bottom <= window.ActualHeight + 1 &&
                modelTestRow.Orientation == Orientation.Vertical,
                "Settings controls or fixed actions were clipped at a compact work area.");
            SaveWindowPreview(window, "tmp/tests/settings-assistant-compact.png", 96);
            constrainWindow.Invoke(window, new object[] { 1920.0, 1080.0 });
            PumpDispatcher();
            window.UpdateLayout();
            Invoke(window, "ShowModelSettings");
            Require((string)Field(window, "_selectedPage") == "Model",
                "Model settings shortcut did not navigate to AI model page.");

            Invoke(window, "Navigate", "Shortcuts");
            PumpDispatcher();
            window.UpdateLayout();
            TextBox writing = (TextBox)Field(window, "_writingHotkey");
            Require(writing.Text == "F7", "Writing shortcut default was not loaded.");
            writing.ApplyTemplate();
            ScrollViewer hotkeyContent = writing.Template.FindName(
                "PART_ContentHost", writing) as ScrollViewer;
            Require(hotkeyContent != null && hotkeyContent.Margin == new Thickness(0) &&
                writing.ActualHeight >= 36 && writing.FontSize >= 12,
                "Hotkey text is inset or clipped by the rounded input template.");
            SaveWindowPreview(window, "tmp/tests/settings-shortcuts-100.png", 96);
            MethodInfo validate = type.GetMethod("ValidateHotkeys",
                BindingFlags.Instance | BindingFlags.NonPublic);
            writing.Text = "Ctrl+Shift+J";
            Require((bool)validate.Invoke(window,
                new object[] { null, null, null, null }),
                "Custom writing shortcut failed validation.");
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Require(!window.IsVisible && modelName.Text == savedModelName &&
                apiKey.Password == savedModelKey && writing.Text == "F7" &&
                (string)Field(window, "_pendingModelVendor") == "DeepSeek",
                "Cancel did not discard model/profile and shortcut drafts.");
            object fresh = Activator.CreateInstance(
                app.GetType("GlobalTranslator.AppSettings", true), true);
            Require((string)fresh.GetType().GetField("ModelVendor").GetValue(fresh) == "DeepSeek" &&
                (string)fresh.GetType().GetField("DeepSeekModelName").GetValue(fresh) == "",
                "A new profile must open on DeepSeek without an unverified example model.");
            Require(type.GetField("_googleKey", BindingFlags.Instance | BindingFlags.NonPublic) == null,
                "Removed Google/Microsoft provider form fields are still present.");
        }
        finally { window.Hide(); }
    }

    private static void SaveWindowPreview(
        Window window, string path, double dpi)
    {
        var surface = window.Content as FrameworkElement;
        if (surface == null) throw new InvalidOperationException("Preview window has no content.");
        double scale = dpi / 96.0;
        int width = Math.Max(
            1,
            (int)Math.Ceiling(
                surface.ActualWidth * scale));
        int height = Math.Max(
            1,
            (int)Math.Ceiling(
                surface.ActualHeight * scale));
        var bitmap = new RenderTargetBitmap(
            width,
            height,
            dpi,
            dpi,
            PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var output =
            System.IO.File.Create(path))
            encoder.Save(output);
    }

    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,
            new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
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
                foreach (string modeName in new[] { "Auto", "Vertical", "Horizontal" })
                {
                    settings.GetType().GetField("OcrLayoutMode").SetValue(settings, modeName);
                    Invoke(settings, "Save");
                    object loaded = settings.GetType().GetMethod("Load").Invoke(null, null);
                    Require((string)settings.GetType().GetField("OcrLayoutMode").GetValue(loaded) == modeName, "OCR layout preference did not round-trip: " + modeName);
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
            settings.GetType().GetField("DeepSeekModelName").SetValue(settings, "reading-probe-model");
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
            Invoke(window, "SetOcrLayoutVisible", true);
            Button layoutButton = (Button)Field(window, "_layoutButton");
            Require(layoutButton.Visibility == Visibility.Visible &&
                layoutButton.ToolTip.ToString().Contains("布局"), "F9 layout control is missing.");
            Invoke(window, "ToggleLayoutPopup");
            Popup layoutPopup = (Popup)Field(window, "_layoutPopup");
            StackPanel layoutMenu = (StackPanel)Field(window, "_layoutMenuItems");
            Require(layoutPopup.IsOpen && layoutMenu.Children.Count == 3, "F9 layout menu is incomplete.");
            Invoke(window, "ToggleLayoutPopup");
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
            Invoke(window, "ApplyOcrLayoutMode", "Horizontal");
            window.UpdateLayout();
            Require((string)Field(window, "_ocrLayoutMode") == "Horizontal" &&
                window.Width >= 760 && grid.ColumnDefinitions.Count == 3 &&
                divider.ResizeDirection == GridResizeDirection.Columns,
                "Manual horizontal F9 layout did not switch or widen the workspace.");
            Invoke(window, "ApplyOcrLayoutMode", "Vertical");
            window.UpdateLayout();
            Require((string)Field(window, "_ocrLayoutMode") == "Vertical" &&
                grid.RowDefinitions.Count == 3 && divider.ResizeDirection == GridResizeDirection.Rows,
                "Manual vertical F9 layout did not switch back.");
            Invoke(window, "ApplyOcrLayoutMode", "Auto");
            window.Width = 560;
            window.UpdateLayout();
            Require((string)Field(window, "_ocrLayoutMode") == "Auto" &&
                grid.RowDefinitions.Count == 3 && divider.ResizeDirection == GridResizeDirection.Rows,
                "Automatic F9 layout did not follow a narrow workspace.");
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
                settings.GetType().GetField("DeepSeekModelBaseUrl").SetValue(settings, "not-a-valid-endpoint");
                object bounds = Activator.CreateInstance(app.GetType("GlobalTranslator.NativeMethods+RECT"), true);
                Invoke(window, "SetOcrDirty", true);
                Invoke(window, "BeginTranslation", "edited words", 100, 100, bounds, settings, client, true, "", false);
                var errorFrame = new System.Windows.Threading.DispatcherFrame();
                var errorTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                errorTimer.Tick += delegate { errorTimer.Stop(); errorFrame.Continue = false; };
                errorTimer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(errorFrame);
                Require((bool)Field(window, "_editProtected"), "Failed retranslation cleared edit protection.");
                Require(((StackPanel)Field(window, "_errorPanel")).Visibility == Visibility.Visible, "Missing key did not enter an error state.");
                SetField(window, "_translatedText", "partial translation");
                text.Text = "partial translation";
                ((Button)Field(window, "_copyTranslation")).Visibility = Visibility.Visible;
                Invoke(
                    window,
                    "ShowTranslationError",
                    new TimeoutException("stream timeout"));
                Require(
                    (string)Field(window, "_translatedText") == "" &&
                    text.Text == "" &&
                    text.Visibility == Visibility.Collapsed &&
                    ((Button)Field(window, "_copyTranslation")).Visibility == Visibility.Collapsed,
                    "Failed stream retained a visible or copyable partial translation.");
                Invoke(window, "BeginTranslation", "123", 100, 100, bounds, settings, client, true, "", false);
                Require(text.Text == "123" && !(bool)Field(window, "_editProtected"), "Successful retranslation did not release edit protection.");
                Invoke(window, "SetOcrDirty", true);
                Invoke(window, "Translate", "456", 100, 100, settings, client);
                Require(!(bool)Field(window, "_editProtected") && !(bool)Field(window, "_manualLayout"), "New F8 inherited OCR protection or manual layout.");
                Invoke(window, "DismissImmediately");
                Invoke(window, "Translate", "456", 100, 100, settings, client);
                Require(window.IsVisible && window.Opacity == 1 &&
                    !DependencyPropertyHelper.GetValueSource(window, UIElement.OpacityProperty).IsAnimated,
                    "F8 still uses an opacity entrance animation.");
                Invoke(window, "Dismiss");
                Invoke(window, "Translate", "789", 100, 100, settings, client);
                var frame = new System.Windows.Threading.DispatcherFrame();
                var timer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180) };
                timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
                timer.Start();
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                Require(window.IsVisible && window.Opacity == 1 && text.Text == "789", "Old dismissal hid or faded a new F8 result.");
                Button copy = (Button)Field(window, "_copyTranslation");
                foreach (string preset in new[] { "Small", "Standard", "Large" })
                foreach (double width in new[] { 360.0, 440.0 })
                {
                    Invoke(window, "ApplyReadingFont", preset);
                    window.Width = width;
                    window.Height = window.MinHeight;
                    text.Text = new string('g', 500);
                    double previousTop = -1;
                    foreach (bool loading in new[] { false, true, false })
                    {
                        ((ProgressBar)Field(window, "_progress")).Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                        ((TextBlock)Field(window, "_loadingText")).Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
                        copy.Content = loading ? "已复制" : "复制译文";
                        window.UpdateLayout();
                        Rect buttonBounds = copy.TransformToAncestor(window).TransformBounds(new Rect(copy.RenderSize));
                        Require(buttonBounds.Bottom <= window.ActualHeight - 8 && buttonBounds.Right <= window.ActualWidth - 8 && copy.ActualHeight >= 32,
                            "F8 copy button is clipped at minimum size, font=" + preset);
                        Require(previousTop < 0 || Math.Abs(previousTop - buttonBounds.Top) < .5, "Loading moved the F8 copy button.");
                        previousTop = buttonBounds.Top;
                    }
                }
                Console.WriteLine("F8_FIX footer=complete-and-stable entrance=opaque staleDismiss=ignored");
            }
            finally { ((IDisposable)client).Dispose(); }
            Console.WriteLine("READING font=3 presets pin=isolated divider=preserved fit=rendered toolbar=compact");
        }
        finally { window.Close(); }
    }

    private static void ProbeCommerceDocuments(Assembly app)
    {
        string dir = Path.Combine(Path.GetTempPath(), "Sharkey-doc-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string csv = Path.Combine(dir, "inquiry.csv");
            File.WriteAllText(csv, "Item,Quantity\r\n\"Cup, black\",0012\r\n", System.Text.Encoding.UTF8);
            string legacyCsv = Path.Combine(dir, "chinese.csv");
            File.WriteAllText(legacyCsv, "品名,数量\r\n杯子,0012\r\n", System.Text.Encoding.GetEncoding(936));
            string docx = Path.Combine(dir, "spec.docx");
            using (var zip = ZipFile.Open(docx, ZipArchiveMode.Create))
                WriteZip(zip, "word/document.xml", "<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Logo size</w:t></w:r></w:p></w:body></w:document>");
            string xlsx = Path.Combine(dir, "quote.xlsx");
            using (var zip = ZipFile.Open(xlsx, ZipArchiveMode.Create))
            {
                WriteZip(zip, "xl/workbook.xml", "<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Prices' r:id='rId1'/></sheets></workbook>");
                WriteZip(zip, "xl/_rels/workbook.xml.rels", "<Relationships><Relationship Id='rId1' Target='worksheets/sheet1.xml'/></Relationships>");
                WriteZip(zip, "xl/sharedStrings.xml", "<sst xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><si><t>Product code</t></si><si><t>0012</t></si></sst>");
                WriteZip(zip, "xl/worksheets/sheet1.xml", "<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData><row r='1'><c r='A1' t='s'><v>0</v></c><c r='B1' t='s'><v>1</v></c></row></sheetData></worksheet>");
            }
            string pdf = Path.Combine(dir, "inquiry.pdf");
            WriteTextPdf(pdf);
            Type reader = app.GetType("GlobalTranslator.CommerceDocuments", true);
            MethodInfo read = reader.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic);
            string csvText = DocumentText(read, csv);
            string legacyText = DocumentText(read, legacyCsv);
            string wordText = DocumentText(read, docx);
            string excelText = DocumentText(read, xlsx);
            string pdfText = DocumentText(read, pdf);
            Require(csvText.Contains("0012") && csvText.Contains("Cup, black"), "CSV values were changed.");
            Require(legacyText.Contains("杯子") && legacyText.Contains("0012"), "Legacy Chinese CSV was garbled.");
            Require(wordText.Contains("Logo size"), "DOCX text missing.");
            Require(excelText.Contains("Prices") && excelText.Contains("B1: 0012"), "XLSX provenance or text missing.");
            Require(pdfText.Contains("Hello PDF sample"), "PDF text missing.");
            Console.WriteLine("ASSISTANT_DOCUMENTS csv/docx/xlsx/pdf=True");
        }
        finally { Directory.Delete(dir, true); }
    }

    private static void WriteZip(ZipArchive zip, string name, string value)
    {
        using (var writer = new StreamWriter(zip.CreateEntry(name).Open(), new System.Text.UTF8Encoding(false)))
            writer.Write(value);
    }

    private static void WriteTextPdf(string path)
    {
        const string stream = "BT /F1 12 Tf 72 720 Td (Hello PDF sample) Tj ET";
        string[] objects = {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            "<< /Length " + stream.Length + " >>\nstream\n" + stream + "\nendstream"
        };
        var pdf = new System.Text.StringBuilder("%PDF-1.4\n");
        var offsets = new System.Collections.Generic.List<int> { 0 };
        for (int i = 0; i < objects.Length; i++)
        {
            offsets.Add(pdf.Length);
            pdf.Append(i + 1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");
        }
        int xref = pdf.Length;
        pdf.Append("xref\n0 ").Append(offsets.Count).Append("\n0000000000 65535 f \n");
        for (int i = 1; i < offsets.Count; i++)
            pdf.Append(offsets[i].ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size ").Append(offsets.Count).Append(" /Root 1 0 R >>\nstartxref\n")
            .Append(xref).Append("\n%%EOF\n");
        File.WriteAllBytes(path, System.Text.Encoding.ASCII.GetBytes(pdf.ToString()));
    }

    private static string DocumentText(MethodInfo read, string path)
    {
        object value = read.Invoke(null, new object[] { path });
        return (string)value.GetType().GetField("Text").GetValue(value);
    }

    private static void ProbeAssistantExperience(Assembly app, Window writing, Type writingType, object settings, object client)
    {
        const BindingFlags hidden = BindingFlags.Static | BindingFlags.NonPublic;
        Type clipboard = app.GetType("GlobalTranslator.ClipboardImages", true);
        var encode = clipboard.GetMethod("Encode", hidden);
        byte[] pixels = { 20, 40, 200, 0 };
        BitmapSource transparent = BitmapSource.Create(1, 1, 96, 96, PixelFormats.Bgra32, null, pixels, 4);
        byte[] original = (byte[])encode.Invoke(null, new object[] { transparent, false });
        byte[] repaired = (byte[])encode.Invoke(null, new object[] { transparent, true });
        using (var stream = new System.IO.MemoryStream(repaired))
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            byte[] decoded = new byte[4]; bgra.CopyPixels(decoded, 4, 0);
            Require(decoded[3] == 255 && decoded[2] == 200, "CF_BITMAP alpha repair lost color.");
        }
        var data = new DataObject(); data.SetData("PNG", new System.IO.MemoryStream(original));
        var read = (System.Collections.IList)clipboard.GetMethod("Read", hidden).Invoke(null, new object[] { data });
        using (var stream = new System.IO.MemoryStream((byte[])read[0]))
        {
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            var bgra = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
            byte[] decoded = new byte[4]; bgra.CopyPixels(decoded, 4, 0);
            Require(decoded[3] == 0, "PNG transparency should be preserved.");
        }
        const BindingFlags instance = BindingFlags.Instance | BindingFlags.NonPublic;
        var language = (ComboBox)Field(writing, "_language");
        language.SelectedIndex = -1; language.Text = "巴西葡萄牙语";
        Require((string)writingType.GetMethod("SelectedLanguage", instance).Invoke(writing, null) == "巴西葡萄牙语",
            "Custom reply language lost.");
        Require((bool)writingType.GetMethod("SaveConversation", instance).Invoke(writing, null), "Conversation save failed.");
        Type store = app.GetType("GlobalTranslator.ConversationStore", true);
        string[] paths = (string[])store.GetMethod("Files", hidden).Invoke(null, null);
        Require(paths.Length == 1, "Conversation did not persist.");
        Require(!System.Text.Encoding.UTF8.GetString(System.IO.File.ReadAllBytes(paths[0])).Contains("请确认数量"),
            "Conversation stored plaintext.");
        object saved = store.GetMethod("Load", hidden).Invoke(null, new object[] { paths[0] });
        writingType.GetMethod("NewConversation", instance).Invoke(writing, null);
        Require(((TextBox)Field(writing, "_intent")).Text.Length == 0, "New conversation did not reset editor.");
        Require(System.IO.File.Exists(paths[0]), "New conversation deleted prior session.");
        writingType.GetMethod("RestoreConversation", instance).Invoke(writing, new[] { saved });
        Require(((TextBox)Field(writing, "_intent")).Text == "请确认数量", "Saved material did not restore.");
        var directoryField = store.GetField("DirectoryPath", hidden);
        object actualDirectory = directoryField.GetValue(null);
        directoryField.SetValue(null, paths[0]); // Existing file cannot serve as a directory.
        try
        {
            writingType.GetMethod("NewConversation", instance).Invoke(writing, null);
            Require(((TextBox)Field(writing, "_intent")).Text == "请确认数量", "Failed save discarded material.");
        }
        finally { directoryField.SetValue(null, actualDirectory); }
        Require(((System.Collections.IList)Field(writing, "_images")).Count == 5, "Saved attachments did not restore.");
        Require((string)writingType.GetMethod("SelectedLanguage", instance).Invoke(writing, null) == "巴西葡萄牙语",
            "Saved custom language did not restore.");
        Type resultType = app.GetType("GlobalTranslator.CommunicationResult", true);
        object inquiry = resultType.GetMethod("Parse").Invoke(null, new object[] {
            "{\"reply\":\"Thanks\",\"meaning_zh\":\"谢谢\",\"advice_zh\":\"核对\",\"inquiry_fields\":[{\"field\":\"数量\",\"value\":\"0012\"}],\"missing_fields\":[\"交期\"]}", false });
        Require(((Array)resultType.GetField("InquiryFields").GetValue(inquiry)).Length == 1 &&
            ((string[])resultType.GetField("MissingFields").GetValue(inquiry))[0] == "交期",
            "Inquiry structure was not parsed.");
        writingType.GetMethod("NewConversation", instance).Invoke(writing, null);
        Type turnType = app.GetType("GlobalTranslator.CommunicationTurn", true);
        object turn = Activator.CreateInstance(turnType, true);
        turnType.GetField("Instruction").SetValue(turn, "请整理这份询盘");
        ((System.Collections.IList)Field(writing, "_turns")).Add(turn);
        Require((bool)writingType.GetMethod("SaveConversation", instance).Invoke(writing, null), "Sent conversation save failed.");
        object session = Field(writing, "_session");
        Require((string)session.GetType().GetField("Title").GetValue(session) == "请整理这份询盘",
            "Sent conversation lost its title after the composer cleared.");
        Type search = app.GetType("GlobalTranslator.CommerceSearch", true);
        var parse = search.GetMethod("ParseNativeResult", hidden);
        bool rejected = false;
        try { parse.Invoke(null, new object[] { "{\"content\":[{\"type\":\"text\",\"text\":\"pretend search\"}]}" }); }
        catch (TargetInvocationException) { rejected = true; }
        Require(rejected, "Native search accepted ungrounded text as search.");
        object outcome = parse.Invoke(null, new object[] { "{\"content\":[{\"type\":\"web_search_tool_result\",\"content\":[{\"title\":\"Source\",\"url\":\"https://example.com\"}]},{\"type\":\"text\",\"text\":\"Evidence\"}]}" });
        Require(outcome.GetType().GetField("Sources").GetValue(outcome).ToString().Contains("https://example.com"), "Native search dropped source.");
        Console.WriteLine("ASSISTANT_EXPERIENCE clipboard/alpha/sessions/encryption/custom-language/native-search=True");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
