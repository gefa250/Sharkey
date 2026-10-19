using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

// Guards the F7 composer paste path: WPF keeps the built-in Paste command disabled
// while the clipboard has no text, so screenshots must be taken off the clipboard
// in PreviewKeyDown; copied text and dropped text must still behave normally.
internal static class ComposerPasteProbe
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    const byte VkControl = 0x11, VkV = 0x56;
    const uint KeyUp = 0x0002;

    static int failures;
    static bool ctrlHeld;

    static object Get(object item, string name) { return item.GetType().GetField(name, Hidden).GetValue(item); }
    static object Call(object item, string method, params object[] args) { return item.GetType().GetMethod(method, Hidden).Invoke(item, args); }
    static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,
            new Action(delegate { frame.Continue = false; }));
        Dispatcher.PushFrame(frame);
    }
    static void PumpFor(int milliseconds)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end) { Pump(); Thread.Sleep(5); }
    }
    static void Check(bool ok, string message)
    {
        if (ok) return;
        failures++;
        Console.Error.WriteLine("FAIL " + message);
    }

    static BitmapSource Quote()
    {
        var visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, 160, 100));
            dc.DrawText(new FormattedText("QUOTE 500 pcs", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI"), 16, Brushes.Black, 1.0), new Point(10, 40));
        }
        var bitmap = new RenderTargetBitmap(160, 100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual); bitmap.Freeze();
        return bitmap;
    }

    static byte[] Png(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(source));
        using (var stream = new MemoryStream()) { encoder.Save(stream); return stream.ToArray(); }
    }

    static void HoldCtrl()
    {
        if (ctrlHeld) return;
        keybd_event(VkControl, 0, 0, UIntPtr.Zero); ctrlHeld = true; PumpFor(60);
    }
    static void ReleaseCtrl()
    {
        if (!ctrlHeld) return;
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero); ctrlHeld = false; PumpFor(60);
    }
    static bool RaisePasteKey(Window window, TextBox input)
    {
        HoldCtrl();
        var keyEvent = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(window), 0, Key.V)
            { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        input.RaiseEvent(keyEvent);
        PumpFor(80);
        ReleaseCtrl();
        return keyEvent.Handled;
    }
    static void SendCtrlV()
    {
        keybd_event(VkControl, 0, 0, UIntPtr.Zero);
        keybd_event(VkV, 0, 0, UIntPtr.Zero);
        PumpFor(60);
        keybd_event(VkV, 0, KeyUp, UIntPtr.Zero);
        keybd_event(VkControl, 0, KeyUp, UIntPtr.Zero);
        PumpFor(300);
    }
    static bool Drop(TextBox input, IDataObject data)
    {
        // DragEventArgs is internal in WPF, so build the drop event reflectively.
        var constructor = typeof(DragEventArgs).GetConstructor(
            BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(IDataObject), typeof(DragDropKeyStates), typeof(DragDropEffects),
                typeof(DependencyObject), typeof(Point) }, null);
        var dropEvent = (DragEventArgs)constructor.Invoke(new object[] { data,
            DragDropKeyStates.None, DragDropEffects.Copy, input, new Point(0, 0) });
        dropEvent.RoutedEvent = DragDrop.PreviewDropEvent;
        input.RaiseEvent(dropEvent);
        PumpFor(80);
        return dropEvent.Handled;
    }

    [STAThread] static int Main(string[] args)
    {
        Window window = null; IDisposable client = null;
        try
        {
            Directory.CreateDirectory(Path.GetFullPath("tmp/tests"));
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var application = new Application();
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var settingsType = assembly.GetType("GlobalTranslator.AppSettings", true);
            string isolated = Path.Combine(Path.GetFullPath("tmp/tests/paste-probe"), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolated);
            settingsType.GetField("Folder", Static).SetValue(null, isolated);
            settingsType.GetField("FilePath", Static).SetValue(null, Path.Combine(isolated, "settings.dat"));
            var store = assembly.GetType("GlobalTranslator.ConversationStore", true);
            store.GetField("DirectoryPath", Static).SetValue(null, Path.Combine(isolated, "conversations"));
            object settings = Activator.CreateInstance(settingsType, true);
            client = (IDisposable)Activator.CreateInstance(assembly.GetType("GlobalTranslator.TranslationClient", true), true);
            window = (Window)Activator.CreateInstance(assembly.GetType("GlobalTranslator.WritingWindow", true),
                new object[] { settings, client, null });
            window.Show(); PumpFor(150);
            var input = (TextBox)Get(window, "_intent");
            var images = (IList)Get(window, "_images");
            var status = (TextBlock)Get(window, "_status");
            var imageList = (Panel)Get(window, "_imageList");
            byte[] png = Png(Quote());

            // Screenshot in the clipboard (CF_DIB only) has no text, so the TextBox
            // never raises the paste event; the composer has to claim Ctrl+V itself.
            Clipboard.Clear(); Clipboard.SetImage(Quote());
            images.Clear(); input.Text = ""; status.Text = "";
            bool handled = RaisePasteKey(window, input);
            Check(handled && images.Count == 1 && imageList.Children.Count == 1,
                "screenshot Ctrl+V did not attach an image");

            // Snipping Tool and browser "copy image" often expose only a PNG stream.
            Clipboard.Clear(); Clipboard.SetData("PNG", new MemoryStream(png));
            images.Clear(); status.Text = "";
            handled = RaisePasteKey(window, input);
            Check(handled && images.Count == 1, "PNG-only clipboard was not pasted");

            // Copied image files keep working.
            string pngPath = Path.Combine(isolated, "quote.png"); File.WriteAllBytes(pngPath, png);
            var files = new StringCollection(); files.Add(pngPath);
            Clipboard.Clear(); Clipboard.SetFileDropList(files);
            images.Clear(); status.Text = "";
            handled = RaisePasteKey(window, input);
            Check(handled && images.Count == 1, "copied image file was not pasted");

            // Empty clipboard must not be reported as a busy clipboard.
            Clipboard.Clear();
            IDataObject empty = Clipboard.GetDataObject();
            images.Clear(); status.Text = "";
            handled = RaisePasteKey(window, input);
            Check(empty != null && !handled && status.Text.Length == 0,
                "empty clipboard was not left to the TextBox");

            // Text and non-image files must keep their existing behavior.
            Clipboard.Clear(); Clipboard.SetText("customer arrives Monday");
            images.Clear(); status.Text = "";
            handled = RaisePasteKey(window, input);
            Check(!handled && images.Count == 0, "text Ctrl+V was stolen from the TextBox");
            string textPath = Path.Combine(isolated, "notes.txt"); File.WriteAllText(textPath, "hello");
            files = new StringCollection(); files.Add(textPath);
            Clipboard.Clear(); Clipboard.SetFileDropList(files);
            images.Clear(); status.Text = "";
            handled = RaisePasteKey(window, input);
            Check(handled && images.Count == 0 && status.Text.Contains("仅支持图片"),
                "copied non-image file was not explained");

            // Five attachments stay the cap.
            Clipboard.Clear(); Clipboard.SetImage(Quote());
            images.Clear(); status.Text = "";
            for (int i = 0; i < 7; i++) RaisePasteKey(window, input);
            Check(images.Count == 5 && status.Text.Contains("5 张"), "attachment cap regressed");

            // Drag and drop from a browser passes image data without file paths.
            var dropData = new DataObject(); dropData.SetData("PNG", new MemoryStream(png));
            images.Clear(); status.Text = "";
            handled = Drop(input, dropData);
            Check(handled && images.Count == 1, "dropped image data was ignored");
            images.Clear();
            var textDrop = new DataObject(); textDrop.SetText("dropped text");
            handled = Drop(input, textDrop);
            Check(!handled && images.Count == 0, "text drop was stolen from the TextBox");

            // The context menu exposes image paste because WPF's own Paste item
            // stays disabled for an image-only clipboard.
            Clipboard.Clear(); Clipboard.SetImage(Quote());
            images.Clear(); input.Text = ""; status.Text = "";
            ContextMenu menu = input.ContextMenu;
            Check(menu != null, "composer has no context menu");
            MenuItem pasteImage = null;
            MenuItem pasteText = null;
            foreach (MenuItem item in menu.Items.OfType<MenuItem>())
            {
                if ((string)item.Header == "粘贴图片") pasteImage = item;
                if ((string)item.Header == "粘贴") pasteText = item;
            }
            Check(pasteImage != null && pasteText != null, "context menu lost paste entries");
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(pasteImage.IsEnabled && !pasteText.IsEnabled, "image clipboard did not gate the menu");
            pasteImage.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            PumpFor(60);
            Check(images.Count == 1, "context-menu image paste did nothing");
            Clipboard.Clear(); Clipboard.SetText("menu-text");
            images.Clear();
            menu.RaiseEvent(new RoutedEventArgs(ContextMenu.OpenedEvent));
            Check(pasteText.IsEnabled && !pasteImage.IsEnabled, "text clipboard did not gate the menu");

            // Real keystroke, only when the probe window owns the foreground.
            Clipboard.Clear(); Clipboard.SetImage(Quote());
            images.Clear(); input.Text = ""; status.Text = "";
            window.Activate(); input.Focus(); Keyboard.Focus(input); PumpFor(250);
            IntPtr handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            bool foreground = GetForegroundWindow() == handle && input.IsKeyboardFocusWithin;
            if (foreground)
            {
                SendCtrlV();
                Check(images.Count == 1 && input.Text.Length == 0, "real Ctrl+V did not attach the screenshot");
                images.Clear(); input.Text = "";
                Clipboard.Clear(); Clipboard.SetText("real-paste-text");
                SendCtrlV();
                Check(images.Count == 0 && input.Text.Contains("real-paste-text"),
                    "real Ctrl+V stopped pasting text");
            }

            Console.WriteLine("COMPOSER_PASTE bitmap/png/file/text/filedrop/cap/drop/keystroke=True foreground=" + foreground);
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { ReleaseCtrl(); if (window != null) Call(window, "CloseForExit"); if (client != null) client.Dispose(); }
    }
}
