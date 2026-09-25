using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

internal static class FeatureProbe
{
    private static int Main(string[] args)
    {
        if (args.Length != 1) return 2;
        Assembly app = Assembly.LoadFrom(args[0]);
        try
        {
            TestHotkeysAndStartup(app);
            TestSmartTargetResolver(app);
            TestTextToolsAndWriting(app);
            TestCommunicationApi(app);
            TestCommerceTools(app);
            TestCommerceToolRoundTrip(app);
            TestCommerceSearch(app);
            TestAiBalance(app);
            TestModelApi(app);
            TestAnthropicModelApi(app);
            TestModelStreamReliability(app);
            TestFailedTranslationNotCached(app);
            TestUpdateService(app);
            TestVisionApi(app);
            TestAnthropicVisionApi(app);
            TestMicrosoftFree(app);
            TestScreenshotRightClickCancellation(app);
            return 0;
        }
        catch (Exception ex)
        {
            Exception current = ex;
            while (current != null)
            {
                Console.Error.WriteLine(
                    "FAILED: " + current.GetType().FullName + ": " + current.Message);
                current = current.InnerException;
            }
            return 1;
        }
    }

    private sealed class BalanceProbeHandler : HttpMessageHandler
    {
        public int Calls;
        public HttpStatusCode Status = HttpStatusCode.OK;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Calls++;
            if (request.Method != HttpMethod.Get || request.RequestUri.AbsoluteUri != "https://api.deepseek.com/user/balance" ||
                request.Headers.Authorization.Scheme != "Bearer" || request.Headers.Authorization.Parameter != "balance-test-key")
                throw new Exception("Wrong balance endpoint or authorization.");
            return Task.FromResult(new HttpResponseMessage(Status)
            {
                Content = new StringContent("{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"12.3456\"},{\"currency\":\"USD\",\"total_balance\":\"1.20\"}]}")
            });
        }
    }

    private static void TestAiBalance(Assembly app)
    {
        Type serviceType = app.GetType("GlobalTranslator.AiBalanceService", true);
        Type connectionType = app.GetType("GlobalTranslator.ModelConnectionSettings", true);
        object connection = Activator.CreateInstance(connectionType, true);
        connectionType.GetField("BaseUrl").SetValue(connection, "https://api.deepseek.com/v1");
        connectionType.GetField("ApiKey").SetValue(connection, "balance-test-key");
        var handler = new BalanceProbeHandler();
        object service = Activator.CreateInstance(serviceType, BindingFlags.Instance | BindingFlags.NonPublic,
            null, new object[] { handler }, null);
        Func<string, Task<string>> query = vendor => (Task<string>)serviceType.GetMethod("QueryAsync")
            .Invoke(service, new object[] { vendor, connection, CancellationToken.None });
        try
        {
            if (query("DeepSeek").GetAwaiter().GetResult() != "CNY 12.3456 / USD 1.20")
                throw new Exception("Balance precision or currency failed.");
            foreach (string endpoint in new[] { "https://api.deepseek.com.evil.test", "http://api.deepseek.com", "https://example.com/v1" })
            {
                connectionType.GetField("BaseUrl").SetValue(connection, endpoint);
                try { query("DeepSeek").GetAwaiter().GetResult(); throw new Exception("Unsafe balance endpoint accepted."); }
                catch (InvalidOperationException) { }
            }
            connectionType.GetField("BaseUrl").SetValue(connection, "https://api.deepseek.com");
            try { query("MiMo").GetAwaiter().GetResult(); throw new Exception("Unsupported vendor queried."); }
            catch (InvalidOperationException) { }
            if (handler.Calls != 1) throw new Exception("Unsupported endpoint received credentials.");
            handler.Status = HttpStatusCode.Unauthorized;
            try { query("DeepSeek").GetAwaiter().GetResult(); throw new Exception("Unauthorized balance reported success."); }
            catch (InvalidOperationException) { }
            var parse = serviceType.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (string json in new[] { "{}", "{\"is_available\":true,\"balance_infos\":[{\"currency\":\"CNY\",\"total_balance\":\"NaN\"}]}" })
            {
                try { parse.Invoke(null, new object[] { json }); throw new Exception("Malformed balance reported as zero."); }
                catch (TargetInvocationException ex) { if (!(ex.InnerException is FormatException)) throw; }
            }
            Console.WriteLine("AI_BALANCE endpoint/auth/currency/precision/unsupported/error=True");
        }
        finally { ((IDisposable)service).Dispose(); }
    }

    private static void TestTextToolsAndWriting(Assembly app)
    {
        Type tools = app.GetType("GlobalTranslator.TextTools", true);
        Func<string, string> join = s => (string)tools.GetMethod("JoinLines").Invoke(null, new object[] { s });
        Func<string, string, string> check = (a, b) => (string)tools.GetMethod("Check").Invoke(null, new object[] { a, b });
        if (join("Please confirm the\nshipping date.") != "Please confirm the shipping date." ||
            join("请确认\n交期") != "请确认交期" ||
            join("First paragraph.\n\nSecond") != "First paragraph.\n\nSecond" ||
            join("- Item one\n- Item two") != "- Item one\n- Item two" ||
            join("Name  Qty\nABC  12") != "Name  Qty\nABC  12")
            throw new Exception("PDF paragraph/list/table preservation failed.");
        if (check("USD 1,200 AB-120", "USD 1200 AB-120") != "" ||
            check("型号AB-120，数量1200", "AB-121 quantity 120").Length == 0 ||
            check("12.5%", "125%").Length == 0)
            throw new Exception("Number/model validation failed.");
        Type history = app.GetType("GlobalTranslator.TranslationHistory", true);
        history.GetMethod("Clear").Invoke(null, null);
        Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
        object settings = Activator.CreateInstance(settingsType, true);
        settingsType.GetField("Provider").SetValue(settings, "ModelApi");
        settingsType.GetField("TargetLanguageMode").SetValue(settings, "Fixed");
        settingsType.GetField("TargetLanguage").SetValue(settings, "en");
        settingsType.GetField("CustomModelName").SetValue(settings, "probe-model");
        var listener = new HttpListener(); listener.Prefixes.Add("http://127.0.0.1:18939/"); listener.Start();
        settingsType.GetField("CustomModelBaseUrl").SetValue(settings, "http://127.0.0.1:18939/v1");
        Exception serverError = null;
        var server = Task.Run(delegate
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    var context = listener.GetContext(); string body;
                    using (var reader = new StreamReader(context.Request.InputStream)) body = reader.ReadToEnd();
                    if ((i == 0 && !body.Contains("formal-mail")) || (i == 1 && !body.Contains("brief-chat")) ||
                        (i == 2 && body.Contains("Writing preferences")) || !body.Contains("English"))
                        throw new Exception("Writing preferences leaked or target language missing.");
                    byte[] response = Encoding.UTF8.GetBytes("{\"choices\":[{\"message\":{\"content\":\"Result " + i + "\"},\"finish_reason\":\"stop\"}]}");
                    context.Response.ContentLength64 = response.Length; context.Response.OutputStream.Write(response, 0, response.Length); context.Response.Close();
                }
            }
            catch (Exception ex) { serverError = ex; }
        });
        Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
        object client = Activator.CreateInstance(clientType, true);
        try
        {
            var method = clientType.GetMethod("TranslateWithRequirementsAsync");
            foreach (string requirement in new[] { "formal-mail", "formal-mail", "brief-chat", "" })
            {
                var task = (Task)method.Invoke(client, new object[] { "请确认交期", settings, CancellationToken.None, null, requirement, false });
                if (!task.Wait(10000)) throw new Exception("Writing request/cache test timed out.");
            }
            if (!server.Wait(2000)) throw new Exception("Expected distinct requests not received.");
            if (serverError != null) throw serverError;
            Array found = (Array)history.GetMethod("Search").Invoke(null, new object[] { "交期" });
            if (found.Length != 3) throw new Exception("History deduplication/search failed.");
            settingsType.GetField("Provider").SetValue(settings, "GoogleFree");
            var unsupported = (Task)method.Invoke(client, new object[] { "中文", settings, CancellationToken.None, null, "formal", false });
            try { unsupported.GetAwaiter().GetResult(); throw new Exception("Unsupported requirements were silently ignored."); }
            catch (InvalidOperationException) { }
            Console.WriteLine("WRITING requirements/cache/isolation/history/pdf/numbers=True");
        }
        finally { listener.Close(); ((IDisposable)client).Dispose(); history.GetMethod("Clear").Invoke(null, null); }
    }

    private static void TestCommunicationApi(Assembly app)
    {
        Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
        Type inputType = app.GetType("GlobalTranslator.CommunicationRequest", true);
        Type turnType = app.GetType("GlobalTranslator.CommunicationTurn", true);
        Type resultType = app.GetType("GlobalTranslator.CommunicationResult", true);
        Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
        object settings = Activator.CreateInstance(settingsType, true);
        settingsType.GetField("Provider").SetValue(settings, "GoogleFree");
        settingsType.GetField("ModelVendor").SetValue(settings, "Custom");
        settingsType.GetField("CustomModelBaseUrl").SetValue(settings,
            "http://127.0.0.1:18944/v1");
        settingsType.GetField("CustomModelApiKey").SetValue(settings, "probe-secret");
        settingsType.GetField("CustomModelName").SetValue(settings, "probe-chat");
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:18944/");
        listener.Start();
        Exception serverError = null;
        var serializer = new JavaScriptSerializer();
        var server = Task.Run(delegate
        {
            try
            {
                for (int i = 0; i < 4; i++)
                {
                    HttpListenerContext context = listener.GetContext();
                    string body;
                    using (var reader = new StreamReader(context.Request.InputStream))
                        body = reader.ReadToEnd();
                    if (!body.Contains("Customer conversation") ||
                        !body.Contains("reply, meaning_zh, advice_zh") ||
                        !body.Contains("probe-chat"))
                        throw new Exception("Communication prompt or model missing.");
                    if (i == 0 && (!body.Contains("data:image/png;base64,AQID") ||
                        !body.Contains("data:image/png;base64,BAUG") ||
                        !body.Contains("Match the customer's language")))
                        throw new Exception("Ordered screenshots or auto language missing.");
                    if (i == 1 && (!body.Contains("shorter") ||
                        !body.Contains("Previous reply") ||
                        !body.Contains("Give advice only")))
                        throw new Exception("Adjustment history or advice-only mode missing.");
                    if (i == 2)
                    {
                        context.Response.StatusCode = 400;
                        byte[] error = Encoding.UTF8.GetBytes(
                            "{\"error\":{\"message\":\"image input unsupported\"}}");
                        context.Response.OutputStream.Write(error, 0, error.Length);
                        context.Response.Close();
                        continue;
                    }
                    string answer = i == 0 || i == 3
                        ? "{\"reply\":\"Please confirm the quantity.\",\"meaning_zh\":\"请确认数量。\",\"advice_zh\":\"价格待核实。\"}"
                        : "{\"reply\":\"\",\"meaning_zh\":\"\",\"advice_zh\":\"先核实交期。\"}";
                    string response = serializer.Serialize(new
                    {
                        choices = new[] { new { message = new { content = answer },
                            finish_reason = "stop" } }
                    });
                    byte[] bytes = Encoding.UTF8.GetBytes(response);
                    context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                    context.Response.Close();
                }
            }
            catch (Exception error) { serverError = error; }
        });
        object client = Activator.CreateInstance(clientType, true);
        try
        {
            object input = Activator.CreateInstance(inputType, true);
            inputType.GetField("Background").SetValue(input, "客户说交期太久");
            inputType.GetField("Intent").SetValue(input, "想请客户确认数量，价格未核实");
            inputType.GetField("Images").SetValue(input, new[]
                { new byte[] { 1, 2, 3 }, new byte[] { 4, 5, 6 } });
            object first = CommunicationResultFor(clientType, client, input,
                settings);
            if ((string)resultType.GetField("Reply").GetValue(first) !=
                    "Please confirm the quantity." ||
                (string)resultType.GetField("MeaningZh").GetValue(first) !=
                    "请确认数量。")
                throw new Exception("Communication result was not parsed.");
            object turn = Activator.CreateInstance(turnType, true);
            turnType.GetField("Instruction").SetValue(turn, "initial");
            turnType.GetField("Reply").SetValue(turn,
                "Please confirm the quantity.");
            Array turns = Array.CreateInstance(turnType, 1);
            turns.SetValue(turn, 0);
            inputType.GetField("Turns").SetValue(input, turns);
            inputType.GetField("Adjustment").SetValue(input, "shorter");
            inputType.GetField("AdviceOnly").SetValue(input, true);
            object second = CommunicationResultFor(clientType, client, input,
                settings);
            if ((string)resultType.GetField("Reply").GetValue(second) != "" ||
                (string)resultType.GetField("AdviceZh").GetValue(second) !=
                    "先核实交期。")
                throw new Exception("Advice-only result was not parsed.");
            inputType.GetField("AdviceOnly").SetValue(input, false);
            inputType.GetField("Background").SetValue(input, "");
            inputType.GetField("Intent").SetValue(input, "");
            try
            {
                CommunicationResultFor(clientType, client, input, settings);
                throw new Exception("Unsupported image was accepted.");
            }
            catch (Exception error)
            {
                if (Unwrap(error).GetType().Name !=
                    "UnsupportedCommunicationImageException") throw;
            }
            settingsType.GetField("CustomModelApiKey").SetValue(settings, "");
            settingsType.GetField("CustomModelBaseUrl").SetValue(settings,
                "https://api.openai.com/v1");
            settingsType.GetField("DeepSeekModelBaseUrl").SetValue(settings,
                "http://127.0.0.1:18944/v1");
            settingsType.GetField("DeepSeekModelApiKey").SetValue(settings,
                "probe-secret");
            settingsType.GetField("DeepSeekModelName").SetValue(settings,
                "probe-chat");
            inputType.GetField("Intent").SetValue(input, "请确认数量");
            inputType.GetField("Images").SetValue(input, new byte[0][]);
            inputType.GetField("Adjustment").SetValue(input, "");
            inputType.GetField("Turns").SetValue(input,
                Array.CreateInstance(turnType, 0));
            try
            {
                CommunicationResultFor(clientType, client, input, settings);
                throw new Exception("An invalid current AI model was silently replaced.");
            }
            catch (InvalidOperationException) { }
            settingsType.GetField("ModelVendor").SetValue(settings, "DeepSeek");
            CommunicationResultFor(clientType, client, input, settings);
            if (!server.Wait(10000) || serverError != null)
                throw serverError ?? new Exception("Communication server timed out.");
            try
            {
                resultType.GetMethod("Parse").Invoke(null, new object[]
                    { "{\"reply\":\"missing meaning\",\"advice_zh\":\"提示\"}", false });
                throw new Exception("Malformed communication result was accepted.");
            }
            catch (TargetInvocationException error)
            {
                if (!(error.InnerException is InvalidOperationException)) throw;
            }
        }
        finally { listener.Close(); ((IDisposable)client).Dispose(); }

        var anthropic = new HttpListener();
        anthropic.Prefixes.Add("http://127.0.0.1:18945/");
        anthropic.Start();
        Exception anthropicError = null;
        var anthropicServer = Task.Run(delegate
        {
            try
            {
                var context = anthropic.GetContext();
                string body;
                using (var reader = new StreamReader(context.Request.InputStream))
                    body = reader.ReadToEnd();
                if (!body.Contains("\"type\":\"image\"") ||
                    !body.Contains("\"source\":{\"type\":\"base64\"") ||
                    context.Request.Headers["x-api-key"] != "probe-secret")
                    throw new Exception("Anthropic image body or auth missing.");
                context.Response.ContentType = "text/event-stream";
                string answer = "{\"reply\":\"Hello.\",\"meaning_zh\":\"你好。\",\"advice_zh\":\"确认条款。\"}";
                string stream = "data: " + serializer.Serialize(new
                {
                    type = "content_block_delta",
                    delta = new { type = "text_delta", text = answer }
                }) + "\n\n" +
                    "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n" +
                    "data: {\"type\":\"message_stop\"}\n\n";
                byte[] bytes = Encoding.UTF8.GetBytes(stream);
                context.Response.OutputStream.Write(bytes, 0, bytes.Length);
                context.Response.Close();
            }
            catch (Exception error) { anthropicError = error; }
        });
        settingsType.GetField("CustomModelBaseUrl").SetValue(settings,
            "http://127.0.0.1:18945/anthropic");
        settingsType.GetField("ModelVendor").SetValue(settings, "Custom");
        settingsType.GetField("CustomModelApiKey").SetValue(settings,
            "probe-secret");
        settingsType.GetField("CustomModelProtocol").SetValue(settings,
            "Anthropic");
        client = Activator.CreateInstance(clientType, true);
        try
        {
            object input = Activator.CreateInstance(inputType, true);
            inputType.GetField("Intent").SetValue(input, "礼貌回复");
            inputType.GetField("Images").SetValue(input,
                new[] { new byte[] { 1, 2, 3 } });
            object answer = CommunicationResultFor(clientType, client, input,
                settings);
            if ((string)resultType.GetField("Reply").GetValue(answer) !=
                "Hello.") throw new Exception("Anthropic reply missing.");
            if (!anthropicServer.Wait(10000) || anthropicError != null)
                throw anthropicError ?? new Exception("Anthropic server timed out.");
            Console.WriteLine("COMMUNICATION openai/anthropic/images/advice/adjustment=True");
        }
        finally { anthropic.Close(); ((IDisposable)client).Dispose(); }
    }

    private static void TestCommerceTools(Assembly app)
    {
        Type requestType = app.GetType(
            "GlobalTranslator.CommerceToolRequest", true);
        Type calculator = app.GetType(
            "GlobalTranslator.CommerceCalculator", true);
        object request = Activator.CreateInstance(requestType, true);
        requestType.GetField("Operation").SetValue(request, "quote");
        requestType.GetField("Inputs").SetValue(request,
            new Dictionary<string, string>
            {
                { "quantity", "100" }, { "unit_price", "12.5" },
                { "discount_percent", "10" }, { "fee", "25" }
            });
        MethodInfo execute = calculator.GetMethod("Execute",
            BindingFlags.Static | BindingFlags.NonPublic);
        object quote = execute.Invoke(null, new[] { request });
        string detail = (string)quote.GetType().GetField("Detail")
            .GetValue(quote);
        if (!detail.Contains("1,150.00"))
            throw new Exception("Quote calculation lost its exact amount.");
        requestType.GetField("Operation").SetValue(request, "margin");
        requestType.GetField("Inputs").SetValue(request,
            new Dictionary<string, string>
            {
                { "revenue", "125" }, { "cost", "100" }
            });
        object margin = execute.Invoke(null, new[] { request });
        detail = (string)margin.GetType().GetField("Detail")
            .GetValue(margin);
        if (!detail.Contains("20.00%"))
            throw new Exception("Margin calculation is incorrect.");
        requestType.GetField("Operation").SetValue(request, "boxes");
        requestType.GetField("Inputs").SetValue(request,
            new Dictionary<string, string>
            {
                { "quantity", "101" }, { "units_per_box", "20" }
            });
        object boxes = execute.Invoke(null, new[] { request });
        detail = (string)boxes.GetType().GetField("Detail")
            .GetValue(boxes);
        if (!detail.Contains("6 箱") || !detail.Contains("尾箱 1 件"))
            throw new Exception("Box count or remainder is incorrect.");
        requestType.GetField("Operation").SetValue(request, "convert");
        requestType.GetField("Inputs").SetValue(request,
            new Dictionary<string, string>
            {
                { "value", "100" }, { "from_unit", "cm" },
                { "to_unit", "m" }
            });
        object converted = execute.Invoke(null, new[] { request });
        detail = (string)converted.GetType().GetField("Detail")
            .GetValue(converted);
        if (!detail.Contains("1.0000 m"))
            throw new Exception("Unit conversion is incorrect.");
        requestType.GetField("Operation").SetValue(request, "freight");
        requestType.GetField("Inputs").SetValue(request,
            new Dictionary<string, string>
            {
                { "actual_kg", "5" }, { "length_cm", "30" },
                { "width_cm", "40" }, { "height_cm", "50" },
                { "box_count", "2" }, { "divisor", "6000" },
                { "rate_per_kg", "2" },
                { "rounding_increment_kg", "0.5" }, { "fee", "3" }
            });
        object freight = execute.Invoke(null, new[] { request });
        detail = (string)freight.GetType().GetField("Detail")
            .GetValue(freight);
        if (!detail.Contains("20.00 kg") ||
            !detail.Contains("43.00"))
            throw new Exception("Freight weight or fee is incorrect.");
        requestType.GetField("Operation").SetValue(request, "fx");
        AssertInvocationFailure(delegate { execute.Invoke(null,
            new[] { request }); }, typeof(InvalidOperationException),
            "缺少有效参数");
        Type resultType = app.GetType(
            "GlobalTranslator.CommunicationResult", true);
        object parsed = resultType.GetMethod("Parse").Invoke(null,
            new object[]
            {
                "{\"tool_requests\":[{\"tool\":\"calculate\"," +
                "\"operation\":\"quote\",\"inputs\":{\"quantity\":100}}]}",
                false
            });
        Array requests = (Array)resultType.GetField("ToolRequests")
            .GetValue(parsed);
        if (requests.Length != 1)
            throw new Exception("Assistant tool request was not parsed.");
        Console.WriteLine("COMMERCE_TOOLS decimal/validation/structured=True");
    }

    private static void TestCommerceToolRoundTrip(Assembly app)
    {
        var listener = new HttpListener();
        listener.Prefixes.Add("http://127.0.0.1:18948/");
        listener.Start();
        Exception serverError = null;
        var serializer = new JavaScriptSerializer();
        var server = Task.Run(delegate
        {
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    HttpListenerContext context = listener.GetContext();
                    string body;
                    using (var reader = new StreamReader(
                        context.Request.InputStream))
                        body = reader.ReadToEnd();
                    if (i == 1 && !body.Contains("1,150.00"))
                        throw new Exception("Verified local calculation was not supplied to the model.");
                    if (i == 2 && !body.Contains("联网搜索未启用"))
                        throw new Exception("Disabled search did not stay offline.");
                    string answer = i == 0
                        ? "{\"tool_requests\":[{\"tool\":\"calculate\"," +
                          "\"operation\":\"quote\",\"inputs\":{\"quantity\":100," +
                          "\"unit_price\":12.5,\"discount_percent\":10,\"fee\":25}}]}"
                        : i == 1
                        ? "{\"tool_requests\":[{\"tool\":\"search\"," +
                          "\"query\":\"current tariff\"}]}"
                        : "{\"reply\":\"Total is 1150.\",\"meaning_zh\":\"合计1150。\"," +
                          "\"advice_zh\":\"请核对币种和报价条款。\"}";
                    byte[] response = Encoding.UTF8.GetBytes(
                        serializer.Serialize(new
                        {
                            choices = new[] { new
                            {
                                message = new { content = answer },
                                finish_reason = "stop"
                            } }
                        }));
                    context.Response.ContentType = "application/json";
                    context.Response.OutputStream.Write(response, 0,
                        response.Length);
                    context.Response.Close();
                }
            }
            catch (Exception error) { serverError = error; }
        });
        Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
        Type inputType = app.GetType("GlobalTranslator.CommunicationRequest", true);
        Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
        object settings = Activator.CreateInstance(settingsType, true);
        object input = Activator.CreateInstance(inputType, true);
        settingsType.GetField("CustomModelBaseUrl").SetValue(settings,
            "http://127.0.0.1:18948/v1");
        settingsType.GetField("CustomModelApiKey").SetValue(settings,
            "probe-secret");
        settingsType.GetField("CustomModelName").SetValue(settings,
            "probe-chat");
        inputType.GetField("Intent").SetValue(input, "计算报价");
        object client = Activator.CreateInstance(clientType, true);
        try
        {
            object answer = CommunicationResultFor(clientType, client,
                input, settings);
            string calculations = (string)answer.GetType()
                .GetField("CalculationDetails").GetValue(answer);
            if (!calculations.Contains("1,150.00"))
                throw new Exception("Local calculation was not returned to the workbench.");
            if (!server.Wait(10000) || serverError != null)
                throw serverError ?? new Exception("Tool round trip timed out.");
        }
        finally { listener.Close(); ((IDisposable)client).Dispose(); }
        Console.WriteLine("COMMERCE_ROUNDTRIP calculate/offline/model=True");
    }

    private sealed class SearchProbeHandler : HttpMessageHandler
    {
        internal string Body;
        internal string Authorization;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content.ReadAsStringAsync().GetAwaiter()
                .GetResult();
            Authorization = request.Headers.Authorization.ToString();
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"results\":[{\"title\":\"Reference\"," +
                    "\"url\":\"https://example.test/source\"," +
                    "\"content\":\"Published tariff data\"}]}"
                )
            });
        }
    }

    private static void TestCommerceSearch(Assembly app)
    {
        Type searchType = app.GetType("GlobalTranslator.CommerceSearch", true);
        var handler = new SearchProbeHandler();
        object search = Activator.CreateInstance(searchType,
            BindingFlags.Instance | BindingFlags.NonPublic |
            BindingFlags.Public, null, new object[] { handler }, null);
        try
        {
            MethodInfo method = searchType.GetMethod("SearchAsync",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var task = (Task)method.Invoke(search,
                new object[] { "current tariff", "probe-search-key",
                    CancellationToken.None });
            task.GetAwaiter().GetResult();
            object outcome = task.GetType().GetProperty("Result")
                .GetValue(task);
            string sources = (string)outcome.GetType().GetField("Sources")
                .GetValue(outcome);
            if (handler.Authorization != "Bearer probe-search-key" ||
                !handler.Body.Contains("\"search_depth\":\"basic\"") ||
                !handler.Body.Contains("\"max_results\":5") ||
                !sources.Contains("https://example.test/source"))
                throw new Exception("Search request or source display is incorrect.");
        }
        finally { ((IDisposable)search).Dispose(); }
        Console.WriteLine("COMMERCE_SEARCH bearer/basic/sources=True");
    }

    private static object CommunicationResultFor(Type clientType, object client,
        object input, object settings)
    {
        object task = clientType.GetMethod("ComposeCommunicationAsync")
            .Invoke(client, new[] { input, settings, (object)CancellationToken.None });
        ((Task)task).GetAwaiter().GetResult();
        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static void TestSmartTargetResolver(Assembly app)
    {
        Type resolver = app.GetType(
            "GlobalTranslator.SmartTargetResolver", true);
        MethodInfo resolve = resolver.GetMethod(
            "Resolve",
            BindingFlags.Static | BindingFlags.Public);
        AssertTarget(resolve, "这是简体中文。", "Smart", "ja", "en");
        AssertTarget(resolve, "這是繁體中文。", "Smart", "ja", "en");
        AssertTarget(resolve, "This is English.", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "日本語の文章です。", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "한국어 문장입니다.", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "APB协议验证", "Smart", "en", "en");
        AssertTarget(
            resolve,
            "Please send the updated quotation to 王先生.",
            "Smart",
            "en",
            "zh-Hans");
        AssertTarget(
            resolve,
            "请查看型号 ABC-123 和 https://example.com/item",
            "Smart",
            "en",
            "en");
        AssertTarget(resolve, "https://example.com/123", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "if (count > 0) return;", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "任意文本", "Fixed", "ko", "ko");
        MethodInfo preserve = resolver.GetMethod(
            "ShouldPreserveContent",
            BindingFlags.Static | BindingFlags.Public);
        AssertPreserve(preserve, "https://example.com/123", true);
        AssertPreserve(preserve, "sales@example.com", true);
        AssertPreserve(preserve, "2026-07-29", true);
        AssertPreserve(preserve, "ABC-123", true);
        AssertPreserve(preserve, "if (count > 0) return;", true);
        AssertPreserve(preserve, "var total = price * quantity;", true);
        AssertPreserve(preserve, "Hello world", false);
        AssertPreserve(
            preserve,
            "Please confirm the price; delivery is required next week.",
            false);
        AssertPreserve(
            preserve,
            "Total amount = USD 1,200. Please confirm.",
            false);

        Type settingsType = app.GetType(
            "GlobalTranslator.AppSettings", true);
        object defaults = Activator.CreateInstance(
            settingsType, true);
        string mode = (string)settingsType.GetField(
            "TargetLanguageMode").GetValue(defaults);
        if (mode != "Smart")
            throw new InvalidOperationException(
                "Old/default settings must use Smart target mode.");
        bool aiOcr = (bool)settingsType.GetField(
            "OcrAiFallback").GetValue(defaults);
        string visionModel = (string)settingsType.GetField(
            "OcrVisionModel").GetValue(defaults);
        if (!aiOcr || visionModel != "deepseek-v4-flash-vision-exp")
            throw new InvalidOperationException(
                "DeepSeek Vision is not the default OCR engine.");
        Console.WriteLine(
            "SMART_TARGET zh=>en other=>zh-Hans fixed=True vision=DeepSeek");
    }

    private static void AssertPreserve(
        MethodInfo preserve,
        string text,
        bool expected)
    {
        bool actual = (bool)preserve.Invoke(
            null,
            new object[] { text, "Smart" });
        if (actual != expected)
            throw new InvalidOperationException(
                "Content-preservation mismatch for '" + text +
                "': expected " + expected +
                ", actual " + actual + ".");
    }

    private static void AssertTarget(
        MethodInfo resolve,
        string text,
        string mode,
        string fixedTarget,
        string expected)
    {
        string actual = (string)resolve.Invoke(
            null,
            new object[] { text, mode, fixedTarget });
        if (actual != expected)
            throw new InvalidOperationException(
                "Smart target mismatch for '" + text +
                "': expected " + expected +
                ", actual " + actual + ".");
    }

    private static void TestHotkeysAndStartup(Assembly app)
    {
        Type hotkeyType = app.GetType(
            "GlobalTranslator.HotkeyGesture", true);
        MethodInfo tryParse = hotkeyType.GetMethod(
            "TryParse",
            BindingFlags.Static | BindingFlags.Public);
        object[] functionCall = { "F8", null, null };
        if (!(bool)tryParse.Invoke(null, functionCall))
            throw new InvalidOperationException(
                "Default F8 hotkey did not parse.");
        object functionGesture = functionCall[1];
        uint functionKey = (uint)hotkeyType
            .GetField("VirtualKey").GetValue(functionGesture);
        if (functionKey != 0x77)
            throw new InvalidOperationException(
                "Default F8 virtual key is incorrect.");

        object[] combinationCall = {
            "Ctrl+Shift+S", null, null
        };
        if (!(bool)tryParse.Invoke(null, combinationCall))
            throw new InvalidOperationException(
                "Combination hotkey did not parse.");
        object combination = combinationCall[1];
        string display = (string)hotkeyType
            .GetField("Display").GetValue(combination);
        uint modifiers = (uint)hotkeyType
            .GetField("Modifiers").GetValue(combination);
        if (display != "Ctrl+Shift+S" ||
            (modifiers & 0x0002) == 0 ||
            (modifiers & 0x0004) == 0)
            throw new InvalidOperationException(
                "Combination hotkey normalization is incorrect.");

        Type startupType = app.GetType(
            "GlobalTranslator.StartupManager", true);
        string command = (string)startupType.GetProperty(
            "CurrentCommand",
            BindingFlags.Static | BindingFlags.Public)
            .GetValue(null, null);
        if (!command.Contains("Sharkey.exe") ||
            !command.EndsWith(
                "--startup",
                StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "Startup command does not target Sharkey.exe.");
        Console.WriteLine(
            "HOTKEY defaults=F7/F8/F9/F10 combo={0} startupCommandReady=True",
            display);
    }


    private static void TestModelApi(Assembly app)
    {
        const string prefix = "http://127.0.0.1:18931/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        Exception serverError = null;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                HttpListenerContext context = listener.GetContext();
                string body;
                using (var reader = new StreamReader(
                    context.Request.InputStream, context.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                if (context.Request.Url.AbsolutePath != "/v1/chat/completions")
                    throw new InvalidOperationException("Unexpected model API path.");
                if (context.Request.Headers["Authorization"] != "Bearer probe-secret")
                    throw new InvalidOperationException("Bearer API key was not sent.");
                if (!body.Contains("\"model\":\"probe-model\"") ||
                    !body.Contains("\"messages\""))
                    throw new InvalidOperationException("Model request body is incomplete.");
                if (body.Contains("<text>") ||
                    body.Contains("</text>"))
                    throw new InvalidOperationException(
                        "Model request still wraps source text in XML tags.");

                byte[] response = Encoding.UTF8.GetBytes(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\"," +
                    "\"content\":\"<text>模型接口翻译成功。</text>\"}," +
                    "\"finish_reason\":\"stop\"}]}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = response.Length;
                context.Response.OutputStream.Write(response, 0, response.Length);
                context.Response.Close();
            }
            catch (Exception ex) { serverError = ex; }
        });

        try
        {
            Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("Provider").SetValue(settings, "ModelApi");
            settingsType.GetField("TargetLanguage").SetValue(settings, "zh-Hans");
            settingsType.GetField("CustomModelBaseUrl").SetValue(settings, prefix + "v1");
            settingsType.GetField("CustomModelApiKey").SetValue(settings, "probe-secret");
            settingsType.GetField("CustomModelName").SetValue(settings, "probe-model");

            Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
            object client = Activator.CreateInstance(clientType, true);
            try
            {
                MethodInfo translate = clientType.GetMethod(
                    "TranslateAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), settingsType, typeof(CancellationToken) },
                    null);
                object task = translate.Invoke(client, new[]
                {
                    (object)"Model API translation works.",
                    settings,
                    CancellationToken.None
                });
                ((Task)task).Wait();
                object result = task.GetType().GetProperty("Result").GetValue(task, null);
                string translated = (string)result.GetType().GetField("Text").GetValue(result);
                string effectiveTarget = (string)result.GetType()
                    .GetField("EffectiveTargetLanguage")
                    .GetValue(result);
                if (translated != "模型接口翻译成功。")
                    throw new InvalidOperationException("Model response was not parsed.");
                if (effectiveTarget != "zh-Hans")
                    throw new InvalidOperationException(
                        "Model API did not receive the smart target.");
                if (serverError != null) throw serverError;
                Console.WriteLine("MODEL_API text=" + translated);
            }
            finally { ((IDisposable)client).Dispose(); }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static void TestAnthropicModelApi(Assembly app)
    {
        const string prefix = "http://127.0.0.1:18933/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        Exception serverError = null;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                HttpListenerContext context = listener.GetContext();
                string body;
                using (var reader = new StreamReader(
                    context.Request.InputStream, context.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                if (context.Request.Url.AbsolutePath !=
                    "/anthropic/v1/messages")
                    throw new InvalidOperationException(
                        "Unexpected Anthropic model API path.");
                if (context.Request.Headers["x-api-key"] !=
                    "anthropic-probe-secret")
                    throw new InvalidOperationException(
                        "Anthropic API key was not sent.");
                if (!body.Contains("\"model\":\"anthropic-probe\"") ||
                    !body.Contains("\"max_tokens\":") ||
                    !body.Contains("\"system\"") ||
                    !body.Contains("\"messages\"") ||
                    body.Contains("\"role\":\"system\""))
                    throw new InvalidOperationException(
                        "Anthropic request body is incomplete or uses an OpenAI system role.");

                context.Response.StatusCode = 200;
                context.Response.ContentType = "text/event-stream";
                context.Response.SendChunked = true;
                using (var writer = new StreamWriter(
                    context.Response.OutputStream,
                    new UTF8Encoding(false)))
                {
                    writer.NewLine = "\n";
                    writer.Write(
                        "event: message_start\n" +
                        "data: {\"type\":\"message_start\"}\n\n");
                    writer.Write(
                        "event: content_block_delta\n" +
                        "data: {\"type\":\"content_block_delta\",\"index\":0," +
                        "\"delta\":{\"type\":\"text_delta\",\"text\":\"Anthropic \"}}\n\n");
                    writer.Write(
                        "event: content_block_delta\n" +
                        "data: {\"type\":\"content_block_delta\",\"index\":0," +
                        "\"delta\":{\"type\":\"text_delta\",\"text\":\"协议翻译成功。\"}}\n\n");
                    writer.Write(
                        "event: message_stop\n" +
                        "data: {\"type\":\"message_stop\"}\n\n");
                    writer.Flush();
                }
                context.Response.Close();
            }
            catch (Exception ex) { serverError = ex; }
        });

        try
        {
            Type settingsType = app.GetType(
                "GlobalTranslator.AppSettings", true);
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("Provider").SetValue(settings, "ModelApi");
            settingsType.GetField("TargetLanguage").SetValue(settings, "zh-Hans");
            settingsType.GetField("CustomModelProtocol").SetValue(settings, "Anthropic");
            settingsType.GetField("CustomModelBaseUrl").SetValue(
                settings, prefix + "anthropic");
            settingsType.GetField("CustomModelApiKey").SetValue(
                settings, "anthropic-probe-secret");
            settingsType.GetField("CustomModelName").SetValue(
                settings, "anthropic-probe");

            Type clientType = app.GetType(
                "GlobalTranslator.TranslationClient", true);
            object client = Activator.CreateInstance(clientType, true);
            try
            {
                MethodInfo translate = clientType.GetMethod(
                    "TranslateAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), settingsType, typeof(CancellationToken) },
                    null);
                object task = translate.Invoke(client, new[]
                {
                    (object)"Anthropic protocol translation works.",
                    settings,
                    CancellationToken.None
                });
                ((Task)task).Wait();
                object result = task.GetType().GetProperty("Result").GetValue(task, null);
                string translated = (string)result.GetType()
                    .GetField("Text").GetValue(result);
                if (translated != "Anthropic 协议翻译成功。")
                    throw new InvalidOperationException(
                        "Anthropic model response was not parsed.");
                if (serverError != null) throw serverError;
                Console.WriteLine("ANTHROPIC_MODEL_API text=" + translated);
            }
            finally { ((IDisposable)client).Dispose(); }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static void TestModelStreamReliability(Assembly app)
    {
        Type clientType = app.GetType(
            "GlobalTranslator.TranslationClient", true);

        string openAiDone =
            "data: {\"choices\":[{\"delta\":{\"content\":\"正常完成\"}}]}\n\n" +
            "data: [DONE]\n\n";
        AssertStreamText(
            clientType,
            "ReadModelStreamAsync",
            openAiDone,
            "正常完成");

        string openAiStop =
            "data: {\"choices\":[{\"delta\":{\"content\":\"正常停止\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\n";
        AssertStreamText(
            clientType,
            "ReadModelStreamAsync",
            openAiStop,
            "正常停止");
        AssertStreamFailure(
            clientType,
            "ReadModelStreamAsync",
            "data: {\"choices\":[{\"delta\":{\"content\":\"部分\"}}]}\n\n" +
            "data: {\"choices\":[{\"delta\":{},\"finish_reason\":\"length\"}]}\n\n",
            typeof(InvalidOperationException),
            "长度限制");
        AssertStreamFailure(
            clientType,
            "ReadModelStreamAsync",
            "data: {\"error\":{\"type\":\"server_error\",\"message\":\"probe error\"}}\n\n",
            typeof(InvalidOperationException),
            "probe error");
        AssertStreamFailure(
            clientType,
            "ReadModelStreamAsync",
            "data: {\"choices\":[{\"delta\":{\"content\":\"意外断流\"}}]}\n\n",
            typeof(InvalidOperationException),
            "意外中断");

        string anthropicComplete =
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"完整译文\"}}\n\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"end_turn\"}}\n\n";
        AssertStreamText(
            clientType,
            "ReadAnthropicModelStreamAsync",
            anthropicComplete,
            "完整译文");
        AssertStreamFailure(
            clientType,
            "ReadAnthropicModelStreamAsync",
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"部分\"}}\n\n" +
            "data: {\"type\":\"message_delta\",\"delta\":{\"stop_reason\":\"max_tokens\"}}\n\n",
            typeof(InvalidOperationException),
            "长度限制");
        AssertStreamFailure(
            clientType,
            "ReadAnthropicModelStreamAsync",
            "data: {\"type\":\"error\",\"error\":{\"type\":\"overloaded_error\",\"message\":\"anthropic probe error\"}}\n\n",
            typeof(InvalidOperationException),
            "anthropic probe error");
        AssertStreamFailure(
            clientType,
            "ReadAnthropicModelStreamAsync",
            "data: {\"type\":\"content_block_delta\",\"delta\":{\"type\":\"text_delta\",\"text\":\"意外断流\"}}\n\n",
            typeof(InvalidOperationException),
            "意外中断");

        AssertStreamTimeout(
            clientType,
            "ReadModelStreamAsync",
            CancellationToken.None,
            typeof(TimeoutException));
        AssertStreamTimeout(
            clientType,
            "ReadAnthropicModelStreamAsync",
            CancellationToken.None,
            typeof(TimeoutException));
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            AssertStreamTimeout(
                clientType,
                "ReadModelStreamAsync",
                cancelled.Token,
                typeof(OperationCanceledException));
            AssertStreamTimeout(
                clientType,
                "ReadAnthropicModelStreamAsync",
                cancelled.Token,
                typeof(OperationCanceledException));
        }

        MethodInfo openAiFinish = clientType.GetMethod(
            "ValidateOpenAiFinishReason",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo anthropicFinish = clientType.GetMethod(
            "ValidateAnthropicStopReason",
            BindingFlags.Static | BindingFlags.NonPublic);
        AssertInvocationFailure(
            delegate { openAiFinish.Invoke(null, new object[] { "length" }); },
            typeof(InvalidOperationException),
            "长度限制");
        AssertInvocationFailure(
            delegate
            {
                anthropicFinish.Invoke(
                    null,
                    new object[] { "max_tokens", false });
            },
            typeof(InvalidOperationException),
            "长度限制");
        Console.WriteLine(
            "MODEL_STREAM terminal/truncation/error/eof/timeout/cancel=True");
    }

    private static void AssertStreamText(
        Type clientType,
        string methodName,
        string payload,
        string expected)
    {
        using (var response = CreateStreamResponse(
            new MemoryStream(Encoding.UTF8.GetBytes(payload))))
        {
            object result = WaitForStreamResult(
                clientType,
                methodName,
                response,
                CancellationToken.None,
                TimeSpan.FromSeconds(1));
            string text = (string)result.GetType()
                .GetField("Text").GetValue(result);
            if (text != expected)
                throw new InvalidOperationException(
                    methodName + " returned '" + text +
                    "' instead of '" + expected + "'.");
        }
    }

    private static void AssertStreamFailure(
        Type clientType,
        string methodName,
        string payload,
        Type expectedType,
        string expectedMessage)
    {
        AssertInvocationFailure(
            delegate
            {
                using (var response = CreateStreamResponse(
                    new MemoryStream(Encoding.UTF8.GetBytes(payload))))
                    WaitForStreamResult(
                        clientType,
                        methodName,
                        response,
                        CancellationToken.None,
                        TimeSpan.FromSeconds(1));
            },
            expectedType,
            expectedMessage);
    }

    private static void AssertStreamTimeout(
        Type clientType,
        string methodName,
        CancellationToken token,
        Type expectedType)
    {
        AssertInvocationFailure(
            delegate
            {
                using (var response = CreateStreamResponse(
                    new NeverEndingStream()))
                    WaitForStreamResult(
                        clientType,
                        methodName,
                        response,
                        token,
                        TimeSpan.FromMilliseconds(80));
            },
            expectedType,
            "");
    }

    private static HttpResponseMessage CreateStreamResponse(Stream stream)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK);
        response.Content = new StreamContent(stream);
        response.Content.Headers.ContentType =
            new MediaTypeHeaderValue("text/event-stream");
        return response;
    }

    private static object WaitForStreamResult(
        Type clientType,
        string methodName,
        HttpResponseMessage response,
        CancellationToken token,
        TimeSpan timeout)
    {
        MethodInfo method = null;
        foreach (MethodInfo candidate in clientType.GetMethods(
            BindingFlags.Static | BindingFlags.NonPublic))
            if (candidate.Name == methodName &&
                candidate.GetParameters().Length == 5)
            {
                method = candidate;
                break;
            }
        if (method == null)
            throw new MissingMethodException(methodName);
        object task = method.Invoke(
            null,
            new object[]
            {
                response,
                "probe-model",
                null,
                token,
                timeout
            });
        ((Task)task).Wait();
        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static void AssertInvocationFailure(
        Action action,
        Type expectedType,
        string expectedMessage)
    {
        try
        {
            action();
        }
        catch (Exception error)
        {
            Exception actual = Unwrap(error);
            if (!expectedType.IsAssignableFrom(actual.GetType()))
                throw new InvalidOperationException(
                    "Expected " + expectedType.Name +
                    " but received " + actual.GetType().Name + ".",
                    actual);
            if (!string.IsNullOrEmpty(expectedMessage) &&
                actual.Message.IndexOf(
                    expectedMessage,
                    StringComparison.OrdinalIgnoreCase) < 0)
                throw new InvalidOperationException(
                    "Failure message did not contain '" +
                    expectedMessage + "': " + actual.Message,
                    actual);
            return;
        }
        throw new InvalidOperationException(
            "Expected " + expectedType.Name + " was not thrown.");
    }

    private static void TestFailedTranslationNotCached(Assembly app)
    {
        const string prefix = "http://127.0.0.1:18935/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        int requestCount = 0;
        Exception serverError = null;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                for (int index = 0; index < 2; index++)
                {
                    HttpListenerContext context = listener.GetContext();
                    Interlocked.Increment(ref requestCount);
                    using (var reader = new StreamReader(
                        context.Request.InputStream,
                        context.Request.ContentEncoding))
                        reader.ReadToEnd();
                    context.Response.StatusCode = 200;
                    context.Response.ContentType = "text/event-stream";
                    context.Response.SendChunked = true;
                    byte[] body = Encoding.UTF8.GetBytes(
                        "data: {\"choices\":[{\"delta\":{\"content\":\"部分译文\"}}]}\n\n");
                    context.Response.OutputStream.Write(body, 0, body.Length);
                    context.Response.Close();
                }
            }
            catch (Exception error) { serverError = error; }
        });

        try
        {
            Type settingsType = app.GetType(
                "GlobalTranslator.AppSettings", true);
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("Provider").SetValue(settings, "ModelApi");
            settingsType.GetField("CustomModelProtocol").SetValue(settings, "OpenAI");
            settingsType.GetField("CustomModelBaseUrl").SetValue(settings, prefix + "v1");
            settingsType.GetField("CustomModelApiKey").SetValue(settings, "probe-secret");
            settingsType.GetField("CustomModelName").SetValue(settings, "deepseek-cache-probe");

            Type clientType = app.GetType(
                "GlobalTranslator.TranslationClient", true);
            object client = Activator.CreateInstance(clientType, true);
            try
            {
                MethodInfo translate = clientType.GetMethod(
                    "TranslateAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(string), settingsType, typeof(CancellationToken) },
                    null);
                for (int attempt = 0; attempt < 2; attempt++)
                    AssertInvocationFailure(
                        delegate
                        {
                            object task = translate.Invoke(
                                client,
                                new object[]
                                {
                                    "Please translate this uncached text.",
                                    settings,
                                    CancellationToken.None
                                });
                            ((Task)task).Wait();
                        },
                        typeof(InvalidOperationException),
                        "意外中断");
            }
            finally { ((IDisposable)client).Dispose(); }

            if (serverError != null) throw serverError;
            if (requestCount != 2)
                throw new InvalidOperationException(
                    "Incomplete translations were cached; request count=" +
                    requestCount + ".");
            Console.WriteLine("MODEL_CACHE incompleteCached=False");
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static void TestUpdateService(Assembly app)
    {
        Type serviceType = app.GetType(
            "GlobalTranslator.UpdateService", true);
        MethodInfo due = serviceType.GetMethod("IsCheckDue",
            BindingFlags.Static | BindingFlags.NonPublic);
        DateTime now = new DateTime(2026, 9, 25, 8, 0, 0,
            DateTimeKind.Utc);
        Func<DateTime, DateTime, int, bool> isDue =
            (success, attempt, hours) => (bool)due.Invoke(null,
                new object[] { now, success, attempt, hours });
        if (isDue(now.AddHours(-23), DateTime.MinValue, 24) ||
            !isDue(now.AddHours(-24), DateTime.MinValue, 24) ||
            !isDue(now.AddHours(-6), DateTime.MinValue, 6) ||
            isDue(now.AddDays(-8), now.AddMinutes(-10), 24) ||
            !isDue(now.AddDays(-8), now.AddMinutes(-30), 24) ||
            isDue(DateTime.MinValue, DateTime.MinValue, 0) ||
            !isDue(DateTime.MinValue, DateTime.MinValue, 24))
            throw new InvalidOperationException(
                "Automatic update interval or retry backoff is incorrect.");
        MethodInfo parse = serviceType.GetMethod(
            "ParseLatestReleaseJson",
            BindingFlags.Static | BindingFlags.NonPublic);
        string json =
            "{\"tag_name\":\"v0.2.3\",\"name\":\"Sharkey v0.2.3\"," +
            "\"body\":\"Update notes\",\"html_url\":\"https://example.test/release\"," +
            "\"draft\":false,\"prerelease\":false,\"assets\":[" +
            "{\"name\":\"Sharkey-win-x64.exe\",\"browser_download_url\":\"https://example.test/Sharkey.exe\"}," +
            "{\"name\":\"Sharkey-win-x64.exe.sha256\",\"browser_download_url\":\"https://example.test/Sharkey.sha256\"}]}";
        object update = parse.Invoke(
            null,
            new object[] { json, "0.2.2" });
        if (update == null ||
            (string)update.GetType().GetField("Version").GetValue(update) !=
            "0.2.3")
            throw new InvalidOperationException(
                "GitHub release update was not parsed.");
        if (parse.Invoke(
                null,
                new object[] { json, "0.2.3" }) != null)
            throw new InvalidOperationException(
                "Current GitHub release was reported as newer.");

        MethodInfo normalizeHash = serviceType.GetMethod(
            "NormalizeSha256",
            BindingFlags.Static | BindingFlags.NonPublic);
        string hash = new string('a', 64);
        string normalized = (string)normalizeHash.Invoke(
            null,
            new object[] { hash + "  Sharkey-win-x64.exe\r\n" });
        if (normalized != hash)
            throw new InvalidOperationException(
                "SHA-256 release file was not parsed.");
        AssertInvocationFailure(
            delegate
            {
                normalizeHash.Invoke(null, new object[] { "bad-hash" });
            },
            typeof(InvalidOperationException),
            "SHA-256");

        MethodInfo computeHash = serviceType.GetMethod(
            "ComputeSha256",
            BindingFlags.Static | BindingFlags.NonPublic);
        MethodInfo verifyHash = serviceType.GetMethod(
            "VerifySha256",
            BindingFlags.Static | BindingFlags.NonPublic);
        string hashProbe = Path.GetTempFileName();
        try
        {
            File.WriteAllText(hashProbe, "verified update bytes");
            string actualHash = (string)computeHash.Invoke(
                null,
                new object[] { hashProbe });
            verifyHash.Invoke(
                null,
                new object[] { hashProbe, actualHash });
            AssertInvocationFailure(
                delegate
                {
                    verifyHash.Invoke(
                        null,
                        new object[] { hashProbe, new string('0', 64) });
                },
                typeof(InvalidOperationException),
                "校验失败");
        }
        finally
        {
            File.Delete(hashProbe);
        }

        string missingAsset = json.Replace(
            "Sharkey-win-x64.exe.sha256",
            "wrong.sha256");
        AssertInvocationFailure(
            delegate
            {
                parse.Invoke(
                    null,
                    new object[] { missingAsset, "0.2.2" });
            },
            typeof(InvalidOperationException),
            "SHA-256");

        Type bootstrapper = app.GetType(
            "GlobalTranslator.UpdateBootstrapper", true);
        MethodInfo replace = bootstrapper.GetMethod(
            "ReplaceExecutable",
            BindingFlags.Static | BindingFlags.NonPublic);
        string folder = Path.Combine(
            Path.GetTempPath(),
            "SharkeyUpdateProbe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            string source = Path.Combine(folder, "new.exe");
            string target = Path.Combine(folder, "Sharkey.exe");
            string backup = target + ".update-backup";
            File.WriteAllText(source, "new-version");
            File.WriteAllText(target, "old-version");
            replace.Invoke(
                null,
                new object[] { source, target, backup });
            if (File.ReadAllText(target) != "new-version" ||
                File.ReadAllText(backup) != "old-version")
                throw new InvalidOperationException(
                    "Updater did not replace and back up the executable.");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
        Console.WriteLine(
            "UPDATER release/semver/hash/replace=True");
    }

    private static void TestScreenshotRightClickCancellation(Assembly app)
    {
        Type selectorType = app.GetType(
            "GlobalTranslator.ScreenshotSelector", true);
        TestScreenshotRightClickState(selectorType, "idle");
        TestScreenshotRightClickState(selectorType, "dragging");
        TestScreenshotRightClickState(selectorType, "small");
        TestScreenshotEscapeAndSubmit(selectorType);
        Console.WriteLine(
            "SCREENSHOT_CANCEL idle/dragging/small mouse-up-guard=True esc/submit=True");
    }

    private static void TestScreenshotRightClickState(
        Type selectorType,
        string state)
    {
        using (var selector = (System.Windows.Forms.Form)
            Activator.CreateInstance(selectorType, true))
        {
            MethodInfo mouseDown = selectorType.GetMethod(
                "OnMouseDown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseMove = selectorType.GetMethod(
                "OnMouseMove",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseUp = selectorType.GetMethod(
                "OnMouseUp",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (state == "dragging" || state == "small")
            {
                mouseDown.Invoke(
                    selector,
                    new object[]
                    {
                        new System.Windows.Forms.MouseEventArgs(
                            System.Windows.Forms.MouseButtons.Left,
                            1,
                            20,
                            20,
                            0)
                    });
                if (state == "dragging")
                    mouseMove.Invoke(
                        selector,
                        new object[]
                        {
                            new System.Windows.Forms.MouseEventArgs(
                                System.Windows.Forms.MouseButtons.Left,
                                0,
                                120,
                                90,
                                0)
                        });
                else
                    mouseUp.Invoke(
                        selector,
                        new object[]
                        {
                            new System.Windows.Forms.MouseEventArgs(
                                System.Windows.Forms.MouseButtons.Left,
                                1,
                                22,
                                22,
                                0)
                        });
            }

            mouseDown.Invoke(
                selector,
                new object[]
                {
                    new System.Windows.Forms.MouseEventArgs(
                        System.Windows.Forms.MouseButtons.Right,
                        1,
                        40,
                        40,
                        0)
                });
            bool pending = (bool)selectorType.GetField(
                "_rightCancelPending",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(selector);
            if (!pending ||
                selector.DialogResult != System.Windows.Forms.DialogResult.None)
                throw new InvalidOperationException(
                    "Right mouse down closed the screenshot overlay in " +
                    state + " state.");

            mouseUp.Invoke(
                selector,
                new object[]
                {
                    new System.Windows.Forms.MouseEventArgs(
                        System.Windows.Forms.MouseButtons.Left,
                        1,
                        100,
                        100,
                        0)
                });
            if (selector.DialogResult == System.Windows.Forms.DialogResult.OK ||
                selectorType.GetProperty("SelectedBitmap")
                    .GetValue(selector, null) != null)
                throw new InvalidOperationException(
                    "Left mouse up submitted a screenshot while right-click cancellation was pending.");

            mouseUp.Invoke(
                selector,
                new object[]
                {
                    new System.Windows.Forms.MouseEventArgs(
                        System.Windows.Forms.MouseButtons.Right,
                        1,
                        40,
                        40,
                        0)
                });
            if (selector.DialogResult != System.Windows.Forms.DialogResult.Cancel)
                throw new InvalidOperationException(
                    "Right mouse up did not cancel screenshot selection in " +
                    state + " state.");
        }
    }

    private static void TestScreenshotEscapeAndSubmit(Type selectorType)
    {
        using (var selector = (System.Windows.Forms.Form)
            Activator.CreateInstance(selectorType, true))
        {
            selectorType.GetMethod(
                "OnKeyDown",
                BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(
                    selector,
                    new object[]
                    {
                        new System.Windows.Forms.KeyEventArgs(
                            System.Windows.Forms.Keys.Escape)
                    });
            if (selector.DialogResult != System.Windows.Forms.DialogResult.Cancel)
                throw new InvalidOperationException(
                    "Escape no longer cancels screenshot selection.");
        }

        using (var selector = (System.Windows.Forms.Form)
            Activator.CreateInstance(selectorType, true))
        {
            MethodInfo mouseDown = selectorType.GetMethod(
                "OnMouseDown",
                BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo mouseUp = selectorType.GetMethod(
                "OnMouseUp",
                BindingFlags.Instance | BindingFlags.NonPublic);
            mouseDown.Invoke(
                selector,
                new object[]
                {
                    new System.Windows.Forms.MouseEventArgs(
                        System.Windows.Forms.MouseButtons.Left,
                        1,
                        20,
                        20,
                        0)
                });
            mouseUp.Invoke(
                selector,
                new object[]
                {
                    new System.Windows.Forms.MouseEventArgs(
                        System.Windows.Forms.MouseButtons.Left,
                        1,
                        120,
                        90,
                        0)
                });
            Bitmap selected = (Bitmap)selectorType
                .GetProperty("SelectedBitmap")
                .GetValue(selector, null);
            if (selector.DialogResult != System.Windows.Forms.DialogResult.OK ||
                selected == null)
                throw new InvalidOperationException(
                    "Normal left-button screenshot selection no longer submits.");
            selected.Dispose();
        }
    }

    private static void TestMicrosoftFree(Assembly app)
    {
        Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
        object settings = Activator.CreateInstance(settingsType, true);
        settingsType.GetField("Provider").SetValue(settings, "MicrosoftFree");
        settingsType.GetField("TargetLanguage").SetValue(settings, "zh-Hans");

        Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
        object client = Activator.CreateInstance(clientType, true);
        try
        {
            MethodInfo translate = clientType.GetMethod(
                "TranslateAsync",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new[] { typeof(string), settingsType, typeof(CancellationToken) },
                null);
            object task = translate.Invoke(client, new[]
            {
                (object)"Microsoft anonymous translation works.",
                settings,
                CancellationToken.None
            });
            ((Task)task).Wait();
            object result = task.GetType().GetProperty("Result").GetValue(task, null);
            Type resultType = result.GetType();
            Console.WriteLine(
                "MICROSOFT_FREE provider={0} detected={1} text={2}",
                resultType.GetField("Provider").GetValue(result),
                resultType.GetField("DetectedLanguage").GetValue(result),
                resultType.GetField("Text").GetValue(result));
        }
        finally
        {
            ((IDisposable)client).Dispose();
        }
    }

    private static void TestVisionApi(Assembly app)
    {
        const string prefix = "http://127.0.0.1:18932/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        Exception serverError = null;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                HttpListenerContext context = listener.GetContext();
                string body;
                using (var reader = new StreamReader(
                    context.Request.InputStream, context.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                if (context.Request.Url.AbsolutePath != "/v1/chat/completions")
                    throw new InvalidOperationException("Unexpected vision API path.");
                if (context.Request.Headers["Authorization"] != "Bearer vision-secret")
                    throw new InvalidOperationException("Vision API key was not sent.");
                if (!body.Contains("\"model\":\"probe-vision\"") ||
                    !body.Contains("\"image_url\"") ||
                    !body.Contains("data:image/png;base64,") ||
                    !body.Contains("Never use Markdown") ||
                    !body.Contains("Do not insert asterisks"))
                    throw new InvalidOperationException(
                        "Vision request does not contain the plain-text OCR instructions.");
                byte[] response = Encoding.UTF8.GetBytes(
                    "{\"choices\":[{\"message\":{\"role\":\"assistant\"," +
                    "\"content\":\"视觉识别 **成功** 123\"}}]}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = response.Length;
                context.Response.OutputStream.Write(response, 0, response.Length);
                context.Response.Close();
            }
            catch (Exception ex) { serverError = ex; }
        });

        try
        {
            Type settingsType = app.GetType("GlobalTranslator.AppSettings", true);
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("CustomModelBaseUrl").SetValue(settings, prefix + "v1");
            settingsType.GetField("CustomModelApiKey").SetValue(settings, "vision-secret");
            settingsType.GetField("CustomModelName").SetValue(settings, "probe-vision");
            Type clientType = app.GetType("GlobalTranslator.TranslationClient", true);
            object client = Activator.CreateInstance(clientType, true);
            try
            {
                using (var bitmap = new Bitmap(240, 80))
                using (Graphics graphics = Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    graphics.DrawString("OCR 123", SystemFonts.DefaultFont, Brushes.Black, 8, 18);
                    MethodInfo recognize = clientType.GetMethod(
                        "RecognizeImageAsync",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { typeof(Bitmap), settingsType, typeof(CancellationToken) },
                        null);
                    object task = recognize.Invoke(
                        client, new object[] { bitmap, settings, CancellationToken.None });
                    ((Task)task).Wait();
                    string text = (string)task.GetType()
                        .GetProperty("Result").GetValue(task, null);
                    if (text != "视觉识别 成功 123")
                        throw new InvalidOperationException(
                            "Vision response Markdown emphasis was not cleaned.");
                    if (serverError != null) throw serverError;
                    Console.WriteLine("VISION_API text=" + text);
                }
            }
            finally { ((IDisposable)client).Dispose(); }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static void TestAnthropicVisionApi(Assembly app)
    {
        const string prefix = "http://127.0.0.1:18934/";
        var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        Exception serverError = null;
        ThreadPool.QueueUserWorkItem(delegate
        {
            try
            {
                HttpListenerContext context = listener.GetContext();
                string body;
                using (var reader = new StreamReader(
                    context.Request.InputStream, context.Request.ContentEncoding))
                    body = reader.ReadToEnd();
                if (context.Request.Url.AbsolutePath !=
                    "/anthropic/v1/messages")
                    throw new InvalidOperationException(
                        "Unexpected Anthropic vision API path.");
                if (context.Request.Headers["x-api-key"] !=
                    "anthropic-vision-secret")
                    throw new InvalidOperationException(
                        "Anthropic vision API key was not sent.");
                if (!body.Contains("\"model\":\"anthropic-vision\"") ||
                    !body.Contains("\"max_tokens\":") ||
                    !body.Contains("\"system\"") ||
                    !body.Contains("\"type\":\"image\"") ||
                    !body.Contains("\"media_type\":\"image/png\"") ||
                    body.Contains("\"image_url\""))
                    throw new InvalidOperationException(
                        "Anthropic vision request body is incomplete.");
                byte[] response = Encoding.UTF8.GetBytes(
                    "{\"type\":\"message\",\"content\":[" +
                    "{\"type\":\"thinking\",\"thinking\":\"internal\"}," +
                    "{\"type\":\"text\",\"text\":\"Anthropic OCR **成功** 123\"}]}");
                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json; charset=utf-8";
                context.Response.ContentLength64 = response.Length;
                context.Response.OutputStream.Write(
                    response, 0, response.Length);
                context.Response.Close();
            }
            catch (Exception ex) { serverError = ex; }
        });

        try
        {
            Type settingsType = app.GetType(
                "GlobalTranslator.AppSettings", true);
            object settings = Activator.CreateInstance(settingsType, true);
            settingsType.GetField("Provider").SetValue(settings, "ModelApi");
            settingsType.GetField("ModelProtocol").SetValue(settings, "Anthropic");
            settingsType.GetField("CustomModelBaseUrl").SetValue(
                settings, prefix + "anthropic");
            settingsType.GetField("CustomModelApiKey").SetValue(
                settings, "anthropic-vision-secret");
            settingsType.GetField("CustomModelName").SetValue(
                settings, "anthropic-vision");
            settingsType.GetField("CustomModelProtocol").SetValue(
                settings, "Anthropic");

            Type clientType = app.GetType(
                "GlobalTranslator.TranslationClient", true);
            object client = Activator.CreateInstance(clientType, true);
            try
            {
                MethodInfo recognize = clientType.GetMethod(
                    "RecognizeImageAsync",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[]
                    {
                        typeof(Bitmap),
                        settingsType,
                        typeof(CancellationToken)
                    },
                    null);
                using (var bitmap = new Bitmap(120, 60))
                {
                    object task = recognize.Invoke(
                        client,
                        new object[]
                        {
                            bitmap, settings, CancellationToken.None
                        });
                    ((Task)task).Wait();
                    string text = (string)task.GetType()
                        .GetProperty("Result").GetValue(task, null);
                    if (text != "Anthropic OCR 成功 123")
                        throw new InvalidOperationException(
                            "Anthropic vision response was not parsed.");
                    if (serverError != null) throw serverError;
                    Console.WriteLine("ANTHROPIC_VISION text=" + text);
                }
            }
            finally { ((IDisposable)client).Dispose(); }
        }
        finally
        {
            listener.Stop();
            listener.Close();
        }
    }

    private static Exception Unwrap(Exception ex)
    {
        while ((ex is TargetInvocationException || ex is AggregateException) &&
               ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }

    private sealed class NeverEndingStream : Stream
    {
        private readonly TaskCompletionSource<int> _pending =
            new TaskCompletionSource<int>();

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return false; } }
        public override bool CanWrite { get { return false; } }
        public override long Length { get { throw new NotSupportedException(); } }
        public override long Position
        {
            get { throw new NotSupportedException(); }
            set { throw new NotSupportedException(); }
        }

        public override void Flush() { }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override Task<int> ReadAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            return _pending.Task;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }
}
