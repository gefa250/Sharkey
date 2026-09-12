using System;
using System.Drawing;
using System.IO;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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
            TestModelApi(app);
            TestAnthropicModelApi(app);
            TestVisionApi(app);
            TestAnthropicVisionApi(app);
            TestMicrosoftFree(app);
            TestEnglishOcrPackManager(app);
            TestOcr(app);
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
        AssertTarget(resolve, "https://example.com/123", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "if (count > 0) return;", "Smart", "en", "zh-Hans");
        AssertTarget(resolve, "任意文本", "Fixed", "ko", "ko");
        MethodInfo preserve = resolver.GetMethod(
            "ShouldPreserveContent",
            BindingFlags.Static | BindingFlags.Public);
        if (!(bool)preserve.Invoke(
                null,
                new object[]
                {
                    "https://example.com/123", "Smart"
                }) ||
            !(bool)preserve.Invoke(
                null,
                new object[]
                {
                    "if (count > 0) return;", "Smart"
                }) ||
            !(bool)preserve.Invoke(
                null,
                new object[] { "2026-07-29", "Smart" }) ||
            (bool)preserve.Invoke(
                null,
                new object[] { "Hello world", "Smart" }))
            throw new InvalidOperationException(
                "Smart target content-preservation rules are incorrect.");

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
            "HOTKEY defaults=F8/F9/F10 combo={0} startupCommandReady=True",
            display);
    }

    private static void TestEnglishOcrPackManager(Assembly app)
    {
        Type manager = app.GetType(
            "GlobalTranslator.OcrLanguagePackManager", true);
        bool installed = (bool)manager.GetMethod(
            "IsEnglishInstalled",
            BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, null);
        string tag = (string)manager.GetMethod(
            "GetInstalledEnglishTag",
            BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, null);
        string command = (string)manager.GetProperty(
            "ManualInstallCommand",
            BindingFlags.Static | BindingFlags.Public)
            .GetValue(null, null);
        if (installed != !string.IsNullOrWhiteSpace(tag))
            throw new InvalidOperationException(
                "English OCR installed state and language tag disagree.");
        if (!command.Contains("Language.Basic") ||
            !command.Contains("Language.OCR") ||
            !command.Contains("en-US") ||
            !command.Contains("Add-WindowsCapability"))
            throw new InvalidOperationException(
                "English OCR manual install command is incomplete.");

        MethodInfo parsePercent = manager.GetMethod(
            "ParseDismPercent",
            BindingFlags.Static | BindingFlags.NonPublic);
        int percent = (int)parsePercent.Invoke(
            null, new object[] { "[================ 42.5% ================]" });
        int missingPercent = (int)parsePercent.Invoke(
            null, new object[] { "Deployment Image Servicing and Management" });
        if (percent != 43 || missingPercent != -1)
            throw new InvalidOperationException(
                "DISM progress parsing is incorrect.");

        Type actionType = app.GetType(
            "GlobalTranslator.OcrComponentAction", true);
        object installAction = Enum.Parse(
            actionType, "InstallRequired");
        MethodInfo buildScript = manager.GetMethod(
            "BuildOperationScript",
            BindingFlags.Static | BindingFlags.NonPublic);
        string script = (string)buildScript.Invoke(
            null,
            new object[]
            {
                installAction,
                @"C:\Temp\sharkey.status",
                @"C:\Temp\sharkey.log"
            });
        if (!script.Contains("dism.exe") ||
            !script.Contains("PROGRESS") ||
            !script.Contains("InstallingBasic") ||
            !script.Contains("InstallingOcr") ||
            !script.Contains("CheckingDependencies"))
            throw new InvalidOperationException(
                "OCR component task script is incomplete.");
        Console.WriteLine(
            "ENGLISH_OCR installed={0} tag={1} commandReady=True progress={2}",
            installed,
            string.IsNullOrEmpty(tag) ? "(none)" : tag,
            percent);
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
                    "\"content\":\"<text>模型接口翻译成功。</text>\"}}]}");
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
            settingsType.GetField("ModelBaseUrl").SetValue(settings, prefix + "v1");
            settingsType.GetField("ModelApiKey").SetValue(settings, "probe-secret");
            settingsType.GetField("ModelName").SetValue(settings, "probe-model");

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
            settingsType.GetField("ModelProtocol").SetValue(settings, "Anthropic");
            settingsType.GetField("ModelBaseUrl").SetValue(
                settings, prefix + "anthropic");
            settingsType.GetField("ModelApiKey").SetValue(
                settings, "anthropic-probe-secret");
            settingsType.GetField("ModelName").SetValue(
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
            settingsType.GetField("ModelBaseUrl").SetValue(settings, prefix + "v1");
            settingsType.GetField("ModelApiKey").SetValue(settings, "vision-secret");
            settingsType.GetField("OcrVisionModel").SetValue(settings, "probe-vision");
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
            settingsType.GetField("ModelBaseUrl").SetValue(
                settings, prefix + "anthropic");
            settingsType.GetField("ModelApiKey").SetValue(
                settings, "anthropic-vision-secret");
            settingsType.GetField("OcrVisionModel").SetValue(
                settings, "anthropic-vision");

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

    private static void TestOcr(Assembly app)
    {
        using (var bitmap = new Bitmap(1000, 180))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (var font = new Font("Microsoft YaHei UI", 42, FontStyle.Bold))
        {
            graphics.Clear(Color.White);
            graphics.DrawString("截图识别测试 123", font, Brushes.Black, 18, 42);

            object result = RecognizeOcr(app, bitmap);
            string text = (string)result.GetType().GetField("Text").GetValue(result);
            double quality = (double)result.GetType()
                .GetField("QualityScore").GetValue(result);
            Console.WriteLine(
                "OCR quality={0:0.00} text={1}",
                quality, text.Replace("\r", " ").Replace("\n", " "));
            if (string.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("OCR returned empty text.");
        }

        using (var dark = new Bitmap(760, 100))
        using (Graphics graphics = Graphics.FromImage(dark))
        using (var font = new Font("Microsoft YaHei UI", 18, FontStyle.Bold))
        {
            graphics.Clear(Color.FromArgb(25, 30, 38));
            graphics.DrawString(
                "深色截图 OCR Enhance 456", font, Brushes.White, 8, 22);
            object result = RecognizeOcr(app, dark);
            string text = (string)result.GetType().GetField("Text").GetValue(result);
            Console.WriteLine(
                "OCR_DARK text=" + text.Replace("\r", " ").Replace("\n", " "));
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Enhanced dark OCR returned empty text.");
            if (!text.Contains("深色截图"))
                throw new InvalidOperationException(
                    "Automatic OCR lost Chinese text in a mixed-language image.");
        }

        Type managerType = app.GetType(
            "GlobalTranslator.OcrLanguagePackManager", true);
        bool englishInstalled = (bool)managerType.GetMethod(
            "IsEnglishInstalled",
            BindingFlags.Static | BindingFlags.Public)
            .Invoke(null, null);
        if (englishInstalled)
        {
            using (var english = new Bitmap(1500, 210))
            using (Graphics graphics = Graphics.FromImage(english))
            using (var font = new Font(
                "Segoe UI", 30, FontStyle.Regular))
            {
                graphics.Clear(Color.White);
                graphics.DrawString(
                    "Verilog introduced several important improvements over its predecessor\n" +
                    "languages, which helped make it a more popular and effective HDL for digital",
                    font, Brushes.Black, 18, 28);
                object result = RecognizeOcr(app, english);
                string text = (string)result.GetType()
                    .GetField("Text").GetValue(result);
                string language = (string)result.GetType()
                    .GetField("Language").GetValue(result);
                Console.WriteLine(
                    "OCR_AUTO_ENGLISH language={0} text={1}",
                    language,
                    text.Replace("\r", " ").Replace("\n", " "));
                if (!language.StartsWith(
                    "en", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        "Automatic OCR did not select the English recognizer.");
                if (text.Contains("itS") || text.Contains("fO r"))
                    throw new InvalidOperationException(
                        "Automatic English OCR retained known casing/spacing errors.");
                if (!text.Contains("\n"))
                    throw new InvalidOperationException(
                        "OCR did not preserve source line breaks.");
            }

            using (var layout = new Bitmap(1000, 230))
            using (Graphics graphics = Graphics.FromImage(layout))
            using (var font = new Font(
                "Segoe UI", 28, FontStyle.Regular))
            {
                graphics.Clear(Color.White);
                graphics.DrawString(
                    "Heading", font, Brushes.Black, 20, 12);
                graphics.DrawString(
                    "Indented item", font, Brushes.Black, 120, 55);
                graphics.DrawString(
                    "Second paragraph", font, Brushes.Black, 20, 145);
                object result = RecognizeOcr(app, layout);
                string text = (string)result.GetType()
                    .GetField("Text").GetValue(result);
                Console.WriteLine(
                    "OCR_LAYOUT text=" +
                    text.Replace("\r", "\\r").Replace("\n", "\\n"));
                string[] lines = text.Split('\n');
                if (lines.Length < 4)
                    throw new InvalidOperationException(
                        "OCR did not preserve paragraph spacing.");
                bool indented = false;
                foreach (string line in lines)
                    if (line.StartsWith(" ") &&
                        line.TrimStart().StartsWith(
                            "Indented", StringComparison.OrdinalIgnoreCase))
                        indented = true;
                if (!indented)
                    throw new InvalidOperationException(
                        "OCR did not preserve source indentation.");
            }
        }

        Type serviceType = app.GetType("GlobalTranslator.OcrService", true);
        MethodInfo cleanup = serviceType.GetMethod(
            "CleanupText", BindingFlags.Static | BindingFlags.NonPublic);
        string cleaned = (string)cleanup.Invoke(
            null, new object[] { "截 图 识 别 Test 123" });
        if (cleaned != "截图识别 Test 123")
            throw new InvalidOperationException(
                "CJK whitespace cleanup failed: " + cleaned);
        Console.WriteLine("OCR_CJK_CLEANUP text=" + cleaned);
    }

    private static object RecognizeOcr(Assembly app, Bitmap bitmap)
    {
        Type ocrType = app.GetType("GlobalTranslator.OcrService", true);
        Type optionsType = app.GetType("GlobalTranslator.OcrOptions", true);
        object ocr = Activator.CreateInstance(ocrType, true);
        object options = Activator.CreateInstance(optionsType, true);
        optionsType.GetField("LanguageTag").SetValue(options, "auto");
        optionsType.GetField("AutoEnhance").SetValue(options, true);
        MethodInfo recognize = ocrType.GetMethod(
            "RecognizeAsync",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            null,
            new[] { typeof(Bitmap), optionsType },
            null);
        object task = recognize.Invoke(ocr, new object[] { bitmap, options });
        ((Task)task).Wait();
        return task.GetType().GetProperty("Result").GetValue(task, null);
    }

    private static Exception Unwrap(Exception ex)
    {
        while ((ex is TargetInvocationException || ex is AggregateException) &&
               ex.InnerException != null)
            ex = ex.InnerException;
        return ex;
    }
}
