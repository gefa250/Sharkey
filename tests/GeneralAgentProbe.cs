using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

internal static class GeneralAgentProbe
{
    const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;
    const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic;
    static readonly string Output = Path.GetFullPath("tmp/tests/agent-review");
    static object Get(object item, string name) { return item.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(item); }
    static object Call(object item, string method, params object[] args) { return item.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Invoke(item, args); }
    static object GetProp(object item, string name) { return item.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).GetValue(item, null); }
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Pump() { var frame = new DispatcherFrame(); Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, new Action(delegate { frame.Continue = false; })); Dispatcher.PushFrame(frame); }
    static void Send(Window window)
    {
        var task = (Task)Call(window, "Generate", false);
        var deadline = DateTime.UtcNow.AddSeconds(20);
        while (!task.IsCompleted && DateTime.UtcNow < deadline) { Pump(); Thread.Sleep(5); }
        Check(task.IsCompleted, "Mock agent request timed out.");
        task.GetAwaiter().GetResult();
        Check(((TextBlock)Get(window, "_status")).Text == "", "Agent request failed: " + ((TextBlock)Get(window, "_status")).Text);
    }
    static void Render(Window window, string name)
    {
        Pump(); window.UpdateLayout();
        var surface = (FrameworkElement)window.Content;
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(surface.ActualWidth), (int)Math.Ceiling(surface.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(surface);
        var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
        using (var file = File.Create(Path.Combine(Output, name + ".png"))) png.Save(file);
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
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            Type settingsType = assembly.GetType("GlobalTranslator.AppSettings", true);
            string isolated = Path.Combine(Output, Guid.NewGuid().ToString("N")); Directory.CreateDirectory(isolated);
            settingsType.GetField("Folder", Static).SetValue(null, isolated);
            settingsType.GetField("FilePath", Static).SetValue(null, Path.Combine(isolated, "settings.dat"));
            var store = assembly.GetType("GlobalTranslator.ConversationStore", true);
            store.GetField("DirectoryPath", Static).SetValue(null, Path.Combine(isolated, "conversations"));

            CheckToolLayer(assembly);

            int port = 19321;
            listener.Prefixes.Add("http://127.0.0.1:" + port + "/");
            listener.Start();
            int requests = 0;
            var bodies = new List<string>();
            var server = Task.Run(delegate
            {
                try
                {
                    while (listener.IsListening)
                    {
                        HttpListenerContext context = listener.GetContext();
                        string body;
                        using (var reader = new StreamReader(context.Request.InputStream)) body = reader.ReadToEnd();
                        int index = Interlocked.Increment(ref requests);
                        bodies.Add(body);
                        string answer = index == 1
                            ? "{\"tool_requests\":[{\"tool\":\"time\",\"operation\":\"\",\"query\":\"\",\"inputs\":{\"cities\":\"纽约,洛杉矶\"}}]}"
                            : "{\"reply\":\"纽约 13:05，洛杉矶 10:05；两城相差 3 小时。\",\"sources\":[{\"title\":\"World Time\",\"url\":\"https://example.test/time\"}]}";
                        bool stream = body.Contains("\"stream\":true");
                        if (stream)
                        {
                            context.Response.ContentType = "text/event-stream";
                            int step = Math.Max(1, answer.Length / 3);
                            for (int i = 0; i < answer.Length; i += step)
                            {
                                string part = answer.Substring(i, Math.Min(step, answer.Length - i));
                                byte[] chunk = Encoding.UTF8.GetBytes(
                                    "data: {\"choices\":[{\"delta\":{\"content\":" + Quote(part) + "},\"finish_reason\":null}]}\n\n");
                                context.Response.OutputStream.Write(chunk, 0, chunk.Length);
                                context.Response.OutputStream.Flush();
                                Thread.Sleep(140);
                            }
                            byte[] tail = Encoding.UTF8.GetBytes(
                                "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n");
                            context.Response.OutputStream.Write(tail, 0, tail.Length);
                        }
                        else
                        {
                            string response = "{\"choices\":[{\"message\":{\"content\":" + Quote(answer) + "},\"finish_reason\":\"stop\"}]}";
                            byte[] bytes = Encoding.UTF8.GetBytes(response);
                            context.Response.ContentType = "application/json";
                            context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                        }
                        context.Response.Close();
                    }
                }
                catch (HttpListenerException) { }
                catch (ObjectDisposedException) { }
            });

            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("ModelVendor").SetValue(settings, "DeepSeek");
            settingsType.GetField("DeepSeekModelBaseUrl").SetValue(settings, "http://127.0.0.1:" + port);
            settingsType.GetField("DeepSeekModelApiKey").SetValue(settings, "probe-key");
            settingsType.GetField("DeepSeekModelName").SetValue(settings, "deepseek-probe");
            client = (IDisposable)Activator.CreateInstance(assembly.GetType("GlobalTranslator.TranslationClient", true), true);
            window = (Window)Activator.CreateInstance(assembly.GetType("GlobalTranslator.WritingWindow", true),
                new object[] { settings, client, null });
            window.Title = "Sharkey · 通用助手验收"; window.Width = 1100; window.Height = 790;
            window.Show(); Pump();

            Call(window, "SetChatMode", "general");
            Check((string)GetProp(window, "CurrentMode") == "general", "Mode switch did not apply.");
            Check(((FrameworkElement)Get(window, "_adviceSection")).Visibility == Visibility.Collapsed,
                "General mode still shows the advice section.");
            Check(((FrameworkElement)Get(window, "_meaningSection")).Visibility == Visibility.Collapsed,
                "General mode still shows the translation section.");
            Check(((FrameworkElement)Get(window, "_fetchToggle")).Visibility == Visibility.Visible,
                "General mode hides the fetch toggle.");
            Check(((TextBlock)Get(window, "_emptyTitle")).Text.Contains("帮你"), "General empty state title missing.");
            Render(window, "general-empty");

            var input = (TextBox)Get(window, "_intent");
            input.Text = "现在纽约和洛杉矶分别是几点？";
            Send(window);
            Check(bodies.Count == 2, "Agent flow did not take exactly two model rounds.");            Check(((string)Get(window, "_streamedText")).Length > 0, "Streaming partials never reached the UI.");
            Check(bodies[0].Contains("general-purpose desktop assistant"),
                "General system prompt missing from the request.");
            Check(bodies[0].Contains("Current local time"), "Current time was not injected.");
            Check(bodies[1].Contains("Tool results"), "Tool results were not fed back to the model.");
            var activities = (IList)Get(window, "_currentActivities");
            Check(activities.Count == 1, "Tool activity was not recorded.");
            Check((string)activities[0].GetType().GetField("Label").GetValue(activities[0]) == "时区",
                "Tool activity label is wrong.");
            Check((string)activities[0].GetType().GetField("Status").GetValue(activities[0]) == "done",
                "Tool activity did not finish.");
            object result = Get(window, "_lastResult");
            Check(((string)result.GetType().GetField("Reply").GetValue(result)).Contains("纽约"),
                "General reply missing.");
            Array items = (Array)result.GetType().GetField("SourceItems").GetValue(result);
            Check(items.Length == 1 && ((string)items.GetValue(0).GetType().GetField("Url").GetValue(items.GetValue(0))) == "https://example.test/time",
                "Source items were not parsed.");            Check(((StackPanel)Get(window, "_sourceList")).Children.Count >= 1, "Source cards were not rendered.");
            Render(window, "general-answer");

            Call(window, "SaveConversation");
            object session = Get(window, "_session");
            Check((string)session.GetType().GetField("Mode").GetValue(session) == "general",
                "Mode was not persisted with the conversation.");

            Call(window, "NewConversation");
            Check((string)GetProp(window, "CurrentMode") == "general", "New conversation lost the last mode.");
            Check((string)Get(window, "_session").GetType().GetField("Mode").GetValue(Get(window, "_session")) == "general",
                "New conversation mode was not carried over.");

            Console.WriteLine("GENERAL_AGENT time/dst/fetch-guard/extract/text-prompt/activity/sources/mode=True");
            return 0;
        }
        catch (Exception error)
        {
            var flattened = error;
            while (flattened is TargetInvocationException && flattened.InnerException != null) flattened = flattened.InnerException;
            Console.Error.WriteLine(flattened);
            return 1;
        }
        finally
        {
            if (window != null) Call(window, "CloseForExit");
            if (client != null) client.Dispose();
            listener.Close();
        }
    }

    static void CheckToolLayer(Assembly assembly)
    {
        Type timeType = assembly.GetType("GlobalTranslator.AgentTimeTool", true);
        Type requestType = assembly.GetType("GlobalTranslator.CommerceToolRequest", true);
        object request = Activator.CreateInstance(requestType, true);
        var inputs = (Dictionary<string, string>)requestType.GetField("Inputs").GetValue(request);
        inputs["cities"] = "纽约,洛杉矶,北京";
        object outcome = timeType.GetMethod("Execute", Static).Invoke(null, new object[] { request });
        string detail = (string)outcome.GetType().GetField("Detail").GetValue(outcome);
        Check(detail.Contains("纽约") && detail.Contains("洛杉矶") && detail.Contains("北京"),
            "Time tool missed a city: " + detail);
        Check(detail.Contains("UTC"), "Time tool missed the UTC offset.");
        object unknown = timeType.GetMethod("Execute", Static).Invoke(null, new object[] { RequestFor(requestType, "火星") });
        string unknownDetail = (string)unknown.GetType().GetField("Detail").GetValue(unknown);
        Check(unknownDetail.Contains("无法识别"), "Unknown city was not reported.");

        TimeZoneInfo eastern = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
        Check(eastern.GetUtcOffset(new DateTime(2026, 1, 15)) != eastern.GetUtcOffset(new DateTime(2026, 7, 15)),
            "Daylight saving difference missing on this machine.");

        Type partialType = assembly.GetType("GlobalTranslator.PartialReply", true);
        MethodInfo extractPartial = partialType.GetMethod("Extract", Static);
        Check((string)extractPartial.Invoke(null, new object[] { "{\"reply\":\"纽约 13:0" }) == "纽约 13:0",
            "Partial reply extraction truncated incorrectly.");
        Check((string)extractPartial.Invoke(null, new object[] { "{\"reply\":\"a\\nb\"}" }) == "a\nb",
            "Partial reply did not unescape newlines.");
        Check((string)extractPartial.Invoke(null, new object[] { "{\"reply\":\"a\\\"b" }) == "a\"b",
            "Partial reply did not unescape quotes.");
        Check((string)extractPartial.Invoke(null, new object[] { "{\"mean" }) == "",
            "Partial reply invented text without a reply key.");        Type fetchType = assembly.GetType("GlobalTranslator.AgentWebFetchTool", true);
        MethodInfo blocked = fetchType.GetMethod("IsBlocked", Static);
        Check((bool)blocked.Invoke(null, new object[] { IPAddress.Parse("127.0.0.1") }), "Loopback not blocked.");
        Check((bool)blocked.Invoke(null, new object[] { IPAddress.Parse("192.168.1.5") }), "Private IPv4 not blocked.");
        Check((bool)blocked.Invoke(null, new object[] { IPAddress.Parse("10.0.0.1") }), "Private IPv4/8 not blocked.");
        Check((bool)blocked.Invoke(null, new object[] { IPAddress.Parse("::1") }), "IPv6 loopback not blocked.");
        Check(!(bool)blocked.Invoke(null, new object[] { IPAddress.Parse("8.8.8.8") }), "Public IPv4 was blocked.");

        MethodInfo extract = fetchType.GetMethod("ExtractText", Static);
        string text = (string)extract.Invoke(null, new object[]
        { "<html><head><title>T</title><script>bad()</script></head><body><h1>标题</h1><p>正文&nbsp;内容</p></body></html>" });
        Check(text.Contains("标题") && text.Contains("正文") && !text.Contains("bad"),
            "HTML extraction is wrong: " + text);

        MethodInfo fetch = fetchType.GetMethod("FetchAsync", Static);
        foreach (string url in new[] { "file:///c:/windows/win.ini", "http://localhost/", "http://127.0.0.1/" })
        {
            try
            {
                var task = (Task)fetch.Invoke(null, new object[] { url, CancellationToken.None });
                task.Wait(5000);
                throw new Exception("Blocked URL was fetched: " + url);
            }
            catch (AggregateException error)
            {
                var inner = error.InnerException;
                Check(inner is InvalidOperationException, "Unexpected error for " + url + ": " + inner);
            }
        }
    }

    static object RequestFor(Type requestType, string city)
    {
        object request = Activator.CreateInstance(requestType, true);
        var inputs = (Dictionary<string, string>)requestType.GetField("Inputs").GetValue(request);
        inputs["cities"] = city;
        return request;
    }

    static string Sse(string content)
    {
        var text = new StringBuilder();
        int step = Math.Max(1, content.Length / 3);
        for (int i = 0; i < content.Length; i += step)
        {
            string part = content.Substring(i, Math.Min(step, content.Length - i));
            text.Append("data: {\"choices\":[{\"delta\":{\"content\":" + Quote(part) + "},\"finish_reason\":null}]}\n\n");
        }
        text.Append("data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n");
        text.Append("data: [DONE]\n\n");
        return text.ToString();
    }    static string Quote(string value)
    {
        return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"";
    }
}
