using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class ChatWorkflowProbe
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly List<string> Requests = new List<string>();
    static readonly string Output = Path.GetFullPath("tmp/tests/chat-review");
    static object Get(object item, string name) { return item.GetType().GetField(name, Hidden).GetValue(item); }
    static object Call(object item, string method, params object[] args) { return item.GetType().GetMethod(method, Hidden).Invoke(item, args); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; })); Dispatcher.PushFrame(frame); }
    static void Send(Window window)
    {
        var task = (Task)Call(window, "Generate", false);
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); System.Threading.Thread.Sleep(5); }
        Check(task.IsCompleted, "Mock translation timed out."); task.GetAwaiter().GetResult();
        Check(((TextBlock)Get(window, "_status")).Text == "", "Translation failed: " + ((TextBlock)Get(window, "_status")).Text);
    }
    static void Render(Window window, string name, double scale)
    {
        Pump(); window.UpdateLayout();
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth * scale), (int)Math.Ceiling(surface.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(surface); var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Output, name + ".png"))) png.Save(file);
    }
    static byte[] QuoteImage()
    {
        var content = new TextBlock { Text = "QUOTATION\n\nItem             Qty        USD\nVacuum flask     500     1,600.00\nPackaging        500       125.00\nFreight                    180.00\n\nTOTAL                    1,905.00", FontFamily = new FontFamily("Consolas"), FontSize = 16, Background = Brushes.White, Padding = new Thickness(18) };
        content.Measure(new Size(420, 220)); content.Arrange(new Rect(0, 0, 420, 220));
        var bitmap = new RenderTargetBitmap(420, 220, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = new MemoryStream()) { png.Save(stream); return stream.ToArray(); }
    }
    [STAThread] static int Main(string[] args)
    {
        Directory.CreateDirectory(Output);
        Window window = null; IDisposable client = null;
        var listener = new HttpListener();
        try
        {
            var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
            var application = new Application();
            System.Threading.SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var settingsType = assembly.GetType("GlobalTranslator.AppSettings", true);
            string isolated = Path.Combine(Output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated);
            settingsType.GetField("Folder", Static).SetValue(null, isolated);
            settingsType.GetField("FilePath", Static).SetValue(null, Path.Combine(isolated, "settings.dat"));
            var store = assembly.GetType("GlobalTranslator.ConversationStore", true);
            store.GetField("DirectoryPath", Static).SetValue(null, Path.Combine(isolated, "conversations"));
            var portSource = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); portSource.Start();
            int port = ((IPEndPoint)portSource.LocalEndpoint).Port; portSource.Stop();
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/"); listener.Start();
            var server = Task.Run(delegate
            {
                while (listener.IsListening)
                {
                    try
                    {
                        var connection = listener.GetContext(); string body;
                        using (var reader = new StreamReader(connection.Request.InputStream)) body = reader.ReadToEnd();
                        lock (Requests) Requests.Add(body);
                        File.AppendAllText(Path.Combine(Output, "interactive-requests.log"), "request " + Requests.Count + "\n");
                        var json = new JavaScriptSerializer();
                        string answer = json.Serialize(new { reply = "已按截图整理如下：\n\n| 项目 | 数量 | 单价（USD） | 金额（USD） |\n| --- | --- | --- | --- |\n| 保温杯 | 500 | 3.20 | 1,600.00 |\n| 包装盒 | 500 | 0.25 | 125.00 |\n| 运费 | — | — | 180.00 |\n| **合计** | | | **1,905.00** |\n\n合计 **USD 1,905.00**。\n\n待确认：报价是否包含税费。", meaning_zh = "报价合计 1,905 美元。", advice_zh = "报价是否包含税费。" });
                        byte[] response = Encoding.UTF8.GetBytes(json.Serialize(new { choices = new[] { new { message = new { content = answer }, finish_reason = "stop" } } }));
                        connection.Response.ContentType = "application/json"; connection.Response.OutputStream.Write(response, 0, response.Length); connection.Response.Close();
                    }
                    catch (HttpListenerException) { break; }
                    catch (ObjectDisposedException) { break; }
                }
            });
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("ModelVendor").SetValue(settings, "Custom");
            settingsType.GetField("CustomModelBaseUrl").SetValue(settings, "http://127.0.0.1:" + port + "/v1");
            settingsType.GetField("CustomModelName").SetValue(settings, "review-model");
            client = (IDisposable)Activator.CreateInstance(assembly.GetType("GlobalTranslator.TranslationClient", true), true);
            window = (Window)Activator.CreateInstance(assembly.GetType("GlobalTranslator.WritingWindow", true), new object[] { settings, client, null });
            window.Title = "Sharkey · 本地交互验收"; window.Width = 1100; window.Height = 790;
            var input = (TextBox)Get(window, "_intent");
            window.Show(); Pump();
            Check(((Border)Get(window, "_chatResultCard")).Visibility == Visibility.Collapsed, "Empty reply card visible.");
            Render(window, "empty", 1);
            if (args.Length > 1 && args[1] == "--interactive")
            {
                input.Text = ""; input.Focus(); application.Run(); return 0;
            }
            input.Text = "old-order-0012：帮我核对这张报价截图，整理成表格。";
            byte[] screenshot = QuoteImage(); Call(window, "AddImage", screenshot); Send(window);
            Check(((IList)Get(window, "_images")).Count == 0 && input.Text == "", "Successful send did not clear composer.");
            var turns = (IList)Get(window, "_turns");
            Check(((byte[][])turns[0].GetType().GetField("Images").GetValue(turns[0])).Length == 1, "Sent image not archived with message.");
            input.Text = "再帮我算一下每个产品分摊运费后的成本。";
            Call(window, "AddImage", screenshot);
            Call(window, "SaveConversation");
            Render(window, "conversation-100", 1); Render(window, "conversation-150", 1.5); Render(window, "conversation-200", 2);
            ((IList)Get(window, "_images")).Clear(); Call(window, "RefreshImages");
            Send(window);
            Check(Requests[1].Contains("old-order-0012") && Requests[1].Contains("data:image/png;base64,"), "Follow-up lost history or image.");
            Call(window, "StartNewTopic");
            input.Text = "new-order-7777"; Send(window);
            Check(!Requests[2].Contains("old-order-0012") && !Requests[2].Contains("data:image/png;base64,"), "New topic leaked old messages/images.");
            Call(window, "SaveConversation");
            string[] files = (string[])store.GetMethod("Files", Static).Invoke(null, null);
            object saved = store.GetMethod("Load", Static).Invoke(null, new object[] { files[0] });
            Check(((int[])saved.GetType().GetField("TopicBreaks").GetValue(saved))[0] == 2, "Topic boundary not persisted.");
            Array savedTurns = (Array)saved.GetType().GetField("Turns").GetValue(saved);
            Check(((byte[][])savedTurns.GetValue(0).GetType().GetField("Images").GetValue(savedTurns.GetValue(0))).Length == 1, "Archived image not persisted.");
            Call(window, "NewConversation");
            Call(window, "RestoreConversation", saved);
            input.Text = "继续本话题"; Send(window);
            Check(!Requests[3].Contains("old-order-0012") && Requests[3].Contains("new-order-7777"), "Restored topic boundary lost.");
            var history = (IList)Get(window, "_turns");
            var turnType = assembly.GetType("GlobalTranslator.CommunicationTurn");
            for (int i = 0; i < 12; i++) { object turn = Activator.CreateInstance(turnType, true); turnType.GetField("Instruction").SetValue(turn, "marker-" + i); history.Add(turn); }
            Call(window, "UpdateContextNotice");
            Check(((TextBlock)Get(window, "_contextNotice")).Visibility == Visibility.Visible, "History limit is silent.");
            // IME completion can be handled by a child; the window must still clear its composing state.
            window.GetType().GetField("_composing", Hidden).SetValue(window, true);
            input.RaiseEvent(new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, new TextComposition(InputManager.Current, input, "确认")) { RoutedEvent = TextCompositionManager.PreviewTextInputEvent, Handled = true });
            Check(!(bool)Get(window, "_composing"), "Handled IME completion left composing state stuck.");
            Call(window, "NewConversation"); Render(window, "empty-after-conversation", 1);
            Console.WriteLine("CHAT_WORKFLOW mock-send/followup-images/topic-isolation/encrypted-restore/limit-notice/handled-IME=True");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { if (window != null) Call(window, "CloseForExit"); if (client != null) client.Dispose(); listener.Close(); }
    }
}
