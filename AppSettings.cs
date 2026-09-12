using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GlobalTranslator
{
    internal static class ModelApiProtocols
    {
        public const string OpenAI = "OpenAI";
        public const string Anthropic = "Anthropic";

        public static string Normalize(string value)
        {
            return string.Equals(
                       value,
                       Anthropic,
                       StringComparison.OrdinalIgnoreCase)
                ? Anthropic
                : OpenAI;
        }

        public static bool IsAnthropic(string value)
        {
            return string.Equals(
                Normalize(value), Anthropic,
                StringComparison.OrdinalIgnoreCase);
        }

        public static string EndpointSuffix(string value)
        {
            return IsAnthropic(value)
                ? "/v1/messages"
                : "/chat/completions";
        }

        public static string DisplayName(string value)
        {
            return IsAnthropic(value)
                ? "Anthropic Messages"
                : "OpenAI Chat Completions";
        }

        public static string BuildEndpoint(
            string baseUrl, string protocol)
        {
            string value = (baseUrl ?? "").Trim().TrimEnd('/');
            if (value.Length == 0) return "";
            if (IsAnthropic(protocol))
            {
                // Accept either a provider base URL (…/anthropic) or a
                // complete Messages endpoint (…/v1/messages).
                if (value.EndsWith(
                        "/messages",
                        StringComparison.OrdinalIgnoreCase))
                    return value;
                if (value.EndsWith(
                        "/v1",
                        StringComparison.OrdinalIgnoreCase))
                    return value + "/messages";
            }
            else if (value.EndsWith(
                         "/chat/completions",
                         StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
            return value + EndpointSuffix(protocol);
        }
    }

    internal static class OcrLayoutModes
    {
        public const string Auto = "Auto";
        public const string Vertical = "Vertical";
        public const string Horizontal = "Horizontal";

        public static string Normalize(string value)
        {
            if (string.Equals(value, Vertical, StringComparison.OrdinalIgnoreCase))
                return Vertical;
            if (string.Equals(value, Horizontal, StringComparison.OrdinalIgnoreCase))
                return Horizontal;
            return Auto;
        }

        public static string DisplayName(string value)
        {
            switch (Normalize(value))
            {
                case Vertical: return "上下";
                case Horizontal: return "左右";
                default: return "自动";
            }
        }
    }

    internal sealed class AppSettings
    {
        public string Provider = "GoogleFree";
        public string TargetLanguageMode = "Smart";
        public string TargetLanguage = "zh-Hans";
        public string GoogleApiKey = "";
        public string MicrosoftApiKey = "";
        public string MicrosoftRegion = "";
        public string ModelVendor = "Custom";
        public string ModelBaseUrl = "https://api.openai.com/v1";
        public string ModelApiKey = "";
        public string ModelName = "gpt-5.6-sol";
        public string ModelProtocol = ModelApiProtocols.OpenAI;
        public string CustomModelBaseUrl = "https://api.openai.com/v1";
        public string CustomModelApiKey = "";
        public string CustomModelName = "gpt-5.6-sol";
        public string CustomModelProtocol = ModelApiProtocols.OpenAI;
        public string DeepSeekModelBaseUrl = "https://api.deepseek.com";
        public string DeepSeekModelApiKey = "";
        public string DeepSeekModelName = "deepseek-v4-flash";
        public string DeepSeekModelProtocol = ModelApiProtocols.OpenAI;
        public string MiMoModelBaseUrl = "https://api.xiaomimimo.com/v1";
        public string MiMoModelApiKey = "";
        public string MiMoModelName = "mimo-v2.5";
        public string MiMoModelProtocol = ModelApiProtocols.OpenAI;
        public string QwenModelBaseUrl =
            "https://dashscope.aliyuncs.com/compatible-mode/v1";
        public string QwenModelApiKey = "";
        public string QwenModelName = "qwen-plus";
        public string QwenModelProtocol = ModelApiProtocols.OpenAI;
        public string OcrLanguage = "auto";
        public bool OcrAutoEnhance = true;
        // Kept under the old field name for settings-file compatibility. It now
        // means that AI Vision is the preferred OCR engine, with Windows OCR as
        // an optional offline fallback.
        public bool OcrAiFallback = true;
        public string OcrVisionModel = "deepseek-v4-flash-vision-exp";
        public bool OcrLocalFallback = true;
        public bool OcrAiConsentGranted = false;
        public bool AutoTranslate = false;
        public string TranslateHotkey = "F8";
        public string OcrHotkey = "F9";
        public string SettingsHotkey = "F10";
        public bool StartWithWindows = StartupManager.IsEnabled();
        public string PopupFontSize = "Standard";
        public string OcrLayoutMode = OcrLayoutModes.Auto;

        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GlobalTranslator.Settings.v1");
        private static readonly string Folder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlobalTranslator");
        private static readonly string FilePath = Path.Combine(Folder, "settings.dat");

        public static bool HasSavedSettings
        {
            get { return File.Exists(FilePath); }
        }

        public static AppSettings Load()
        {
            var settings = new AppSettings();
            if (!File.Exists(FilePath)) return settings;

            try
            {
                bool hasVendorProfiles = false;
                bool hasOcrVisionModel = false;
                bool hasOcrAiSetting = false;
                byte[] encrypted = File.ReadAllBytes(FilePath);
                byte[] clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
                string[] lines = Encoding.UTF8.GetString(clear).Split(new[] { '\n' }, StringSplitOptions.None);
                foreach (string line in lines)
                {
                    int separator = line.IndexOf('=');
                    if (separator <= 0) continue;
                    string name = line.Substring(0, separator);
                    string value = Decode(line.Substring(separator + 1));
                    switch (name)
                    {
                        case "Provider": settings.Provider = value; break;
                        case "TargetLanguageMode": settings.TargetLanguageMode = value; break;
                        case "TargetLanguage": settings.TargetLanguage = value; break;
                        case "GoogleApiKey": settings.GoogleApiKey = value; break;
                        case "MicrosoftApiKey": settings.MicrosoftApiKey = value; break;
                        case "MicrosoftRegion": settings.MicrosoftRegion = value; break;
                        case "ModelVendor": settings.ModelVendor = value; break;
                        case "ModelBaseUrl": settings.ModelBaseUrl = value; break;
                        case "ModelApiKey": settings.ModelApiKey = value; break;
                        case "ModelName": settings.ModelName = value; break;
                        case "ModelProtocol": settings.ModelProtocol = ModelApiProtocols.Normalize(value); break;
                        case "CustomModelBaseUrl": settings.CustomModelBaseUrl = value; hasVendorProfiles = true; break;
                        case "CustomModelApiKey": settings.CustomModelApiKey = value; hasVendorProfiles = true; break;
                        case "CustomModelName": settings.CustomModelName = value; hasVendorProfiles = true; break;
                        case "CustomModelProtocol": settings.CustomModelProtocol = ModelApiProtocols.Normalize(value); hasVendorProfiles = true; break;
                        case "DeepSeekModelBaseUrl": settings.DeepSeekModelBaseUrl = value; hasVendorProfiles = true; break;
                        case "DeepSeekModelApiKey": settings.DeepSeekModelApiKey = value; hasVendorProfiles = true; break;
                        case "DeepSeekModelName": settings.DeepSeekModelName = value; hasVendorProfiles = true; break;
                        case "DeepSeekModelProtocol": settings.DeepSeekModelProtocol = ModelApiProtocols.Normalize(value); hasVendorProfiles = true; break;
                        case "MiMoModelBaseUrl": settings.MiMoModelBaseUrl = value; hasVendorProfiles = true; break;
                        case "MiMoModelApiKey": settings.MiMoModelApiKey = value; hasVendorProfiles = true; break;
                        case "MiMoModelName": settings.MiMoModelName = value; hasVendorProfiles = true; break;
                        case "MiMoModelProtocol": settings.MiMoModelProtocol = ModelApiProtocols.Normalize(value); hasVendorProfiles = true; break;
                        case "QwenModelBaseUrl": settings.QwenModelBaseUrl = value; hasVendorProfiles = true; break;
                        case "QwenModelApiKey": settings.QwenModelApiKey = value; hasVendorProfiles = true; break;
                        case "QwenModelName": settings.QwenModelName = value; hasVendorProfiles = true; break;
                        case "QwenModelProtocol": settings.QwenModelProtocol = ModelApiProtocols.Normalize(value); hasVendorProfiles = true; break;
                        case "OcrLanguage": settings.OcrLanguage = value; break;
                        case "OcrAutoEnhance": settings.OcrAutoEnhance = value != "false"; break;
                        case "OcrAiFallback": settings.OcrAiFallback = value == "true"; hasOcrAiSetting = true; break;
                        case "OcrVisionModel": settings.OcrVisionModel = value; hasOcrVisionModel = true; break;
                        case "OcrLocalFallback": settings.OcrLocalFallback = value != "false"; break;
                        case "OcrAiConsentGranted": settings.OcrAiConsentGranted = value == "true"; break;
                        case "AutoTranslate": settings.AutoTranslate = value == "true"; break;
                        case "TranslateHotkey": settings.TranslateHotkey = value; break;
                        case "OcrHotkey": settings.OcrHotkey = value; break;
                        case "SettingsHotkey": settings.SettingsHotkey = value; break;
                        case "StartWithWindows": settings.StartWithWindows = value == "true"; break;
                        case "PopupFontSize": settings.PopupFontSize = value == "Small" || value == "Large" ? value : "Standard"; break;
                        case "OcrLayoutMode": settings.OcrLayoutMode = OcrLayoutModes.Normalize(value); break;
                    }
                }
                if (!hasVendorProfiles)
                {
                    settings.ModelVendor = InferModelVendor(
                        settings.ModelVendor,
                        settings.ModelBaseUrl);
                    settings.SetModelConnection(
                        settings.ModelVendor,
                        new ModelConnectionSettings
                        {
                            BaseUrl = settings.ModelBaseUrl,
                            ApiKey = settings.ModelApiKey,
                            Model = settings.ModelName,
                            Protocol = settings.ModelProtocol
                        });
                }
                if (!hasOcrVisionModel ||
                    string.IsNullOrWhiteSpace(settings.OcrVisionModel))
                    settings.OcrVisionModel =
                        "deepseek-v4-flash-vision-exp";
                if (!hasOcrAiSetting || !hasOcrVisionModel)
                    settings.OcrAiFallback = true;
            }
            catch { return new AppSettings(); }
            return settings;
        }

        public void Save()
        {
            Directory.CreateDirectory(Folder);
            string data =
                "Provider=" + Encode(Provider) + "\n" +
                "TargetLanguageMode=" + Encode(TargetLanguageMode) + "\n" +
                "TargetLanguage=" + Encode(TargetLanguage) + "\n" +
                "GoogleApiKey=" + Encode(GoogleApiKey) + "\n" +
                "MicrosoftApiKey=" + Encode(MicrosoftApiKey) + "\n" +
                "MicrosoftRegion=" + Encode(MicrosoftRegion) + "\n" +
                "ModelVendor=" + Encode(ModelVendor) + "\n" +
                "ModelBaseUrl=" + Encode(ModelBaseUrl) + "\n" +
                "ModelApiKey=" + Encode(ModelApiKey) + "\n" +
                "ModelName=" + Encode(ModelName) + "\n" +
                "ModelProtocol=" + Encode(ModelApiProtocols.Normalize(ModelProtocol)) + "\n" +
                "CustomModelBaseUrl=" + Encode(CustomModelBaseUrl) + "\n" +
                "CustomModelApiKey=" + Encode(CustomModelApiKey) + "\n" +
                "CustomModelName=" + Encode(CustomModelName) + "\n" +
                "CustomModelProtocol=" + Encode(ModelApiProtocols.Normalize(CustomModelProtocol)) + "\n" +
                "DeepSeekModelBaseUrl=" + Encode(DeepSeekModelBaseUrl) + "\n" +
                "DeepSeekModelApiKey=" + Encode(DeepSeekModelApiKey) + "\n" +
                "DeepSeekModelName=" + Encode(DeepSeekModelName) + "\n" +
                "DeepSeekModelProtocol=" + Encode(ModelApiProtocols.Normalize(DeepSeekModelProtocol)) + "\n" +
                "MiMoModelBaseUrl=" + Encode(MiMoModelBaseUrl) + "\n" +
                "MiMoModelApiKey=" + Encode(MiMoModelApiKey) + "\n" +
                "MiMoModelName=" + Encode(MiMoModelName) + "\n" +
                "MiMoModelProtocol=" + Encode(ModelApiProtocols.Normalize(MiMoModelProtocol)) + "\n" +
                "QwenModelBaseUrl=" + Encode(QwenModelBaseUrl) + "\n" +
                "QwenModelApiKey=" + Encode(QwenModelApiKey) + "\n" +
                "QwenModelName=" + Encode(QwenModelName) + "\n" +
                "QwenModelProtocol=" + Encode(ModelApiProtocols.Normalize(QwenModelProtocol)) + "\n" +
                "OcrLanguage=" + Encode(OcrLanguage) + "\n" +
                "OcrAutoEnhance=" + Encode(OcrAutoEnhance ? "true" : "false") + "\n" +
                "OcrAiFallback=" + Encode(OcrAiFallback ? "true" : "false") + "\n" +
                "OcrVisionModel=" + Encode(OcrVisionModel) + "\n" +
                "OcrLocalFallback=" + Encode(OcrLocalFallback ? "true" : "false") + "\n" +
                "OcrAiConsentGranted=" + Encode(OcrAiConsentGranted ? "true" : "false") + "\n" +
                "AutoTranslate=" + Encode(AutoTranslate ? "true" : "false") + "\n" +
                "TranslateHotkey=" + Encode(TranslateHotkey) + "\n" +
                "OcrHotkey=" + Encode(OcrHotkey) + "\n" +
                "SettingsHotkey=" + Encode(SettingsHotkey) + "\n" +
                "PopupFontSize=" + Encode(PopupFontSize) + "\n" +
                "OcrLayoutMode=" + Encode(OcrLayoutModes.Normalize(OcrLayoutMode)) + "\n" +
                "StartWithWindows=" + Encode(StartWithWindows ? "true" : "false");
            byte[] clear = Encoding.UTF8.GetBytes(data);
            byte[] encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(FilePath, encrypted);
        }

        public ModelConnectionSettings GetModelConnection(
            string vendor)
        {
            switch ((vendor ?? "").ToLowerInvariant())
            {
                case "deepseek":
                    return new ModelConnectionSettings
                    {
                        BaseUrl = DeepSeekModelBaseUrl,
                        ApiKey = DeepSeekModelApiKey,
                        Model = DeepSeekModelName,
                        Protocol = DeepSeekModelProtocol
                    };
                case "mimo":
                    return new ModelConnectionSettings
                    {
                        BaseUrl = MiMoModelBaseUrl,
                        ApiKey = MiMoModelApiKey,
                        Model = MiMoModelName,
                        Protocol = MiMoModelProtocol
                    };
                case "qwen":
                    return new ModelConnectionSettings
                    {
                        BaseUrl = QwenModelBaseUrl,
                        ApiKey = QwenModelApiKey,
                        Model = QwenModelName,
                        Protocol = QwenModelProtocol
                    };
                default:
                    return new ModelConnectionSettings
                    {
                        BaseUrl = CustomModelBaseUrl,
                        ApiKey = CustomModelApiKey,
                        Model = CustomModelName,
                        Protocol = CustomModelProtocol
                    };
            }
        }

        public void SetModelConnection(
            string vendor,
            ModelConnectionSettings connection)
        {
            connection = connection ??
                new ModelConnectionSettings();
            switch ((vendor ?? "").ToLowerInvariant())
            {
                case "deepseek":
                    DeepSeekModelBaseUrl = connection.BaseUrl;
                    DeepSeekModelApiKey = connection.ApiKey;
                    DeepSeekModelName = connection.Model;
                    DeepSeekModelProtocol = ModelApiProtocols.Normalize(connection.Protocol);
                    break;
                case "mimo":
                    MiMoModelBaseUrl = connection.BaseUrl;
                    MiMoModelApiKey = connection.ApiKey;
                    MiMoModelName = connection.Model;
                    MiMoModelProtocol = ModelApiProtocols.Normalize(connection.Protocol);
                    break;
                case "qwen":
                    QwenModelBaseUrl = connection.BaseUrl;
                    QwenModelApiKey = connection.ApiKey;
                    QwenModelName = connection.Model;
                    QwenModelProtocol = ModelApiProtocols.Normalize(connection.Protocol);
                    break;
                default:
                    CustomModelBaseUrl = connection.BaseUrl;
                    CustomModelApiKey = connection.ApiKey;
                    CustomModelName = connection.Model;
                    CustomModelProtocol = ModelApiProtocols.Normalize(connection.Protocol);
                    break;
            }
        }

        private static string Encode(string value)
        {
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? ""));
        }

        private static string Decode(string value)
        {
            try { return Encoding.UTF8.GetString(Convert.FromBase64String(value)); }
            catch { return ""; }
        }

        private static string InferModelVendor(
            string vendor, string baseUrl)
        {
            if (!string.IsNullOrWhiteSpace(vendor) &&
                !string.Equals(
                    vendor,
                    "Custom",
                    StringComparison.OrdinalIgnoreCase))
                return vendor;
            string normalized =
                (baseUrl ?? "").TrimEnd('/').ToLowerInvariant();
            if (normalized == "https://api.deepseek.com" ||
                normalized == "https://api.deepseek.com/anthropic")
                return "DeepSeek";
            if (normalized == "https://api.xiaomimimo.com/v1" ||
                normalized == "https://api.xiaomimimo.com/anthropic")
                return "MiMo";
            if (normalized ==
                "https://dashscope.aliyuncs.com/compatible-mode/v1")
                return "Qwen";
            return "Custom";
        }
    }

    internal sealed class ModelConnectionSettings
    {
        public string BaseUrl = "";
        public string ApiKey = "";
        public string Model = "";
        public string Protocol = ModelApiProtocols.OpenAI;

        public ModelConnectionSettings Copy()
        {
            return new ModelConnectionSettings
            {
                BaseUrl = BaseUrl,
                ApiKey = ApiKey,
                Model = Model,
                Protocol = ModelApiProtocols.Normalize(Protocol)
            };
        }

        public bool IsUsable(string vendor)
        {
            if (string.IsNullOrWhiteSpace(BaseUrl) ||
                string.IsNullOrWhiteSpace(Model))
                return false;
            if (!string.IsNullOrWhiteSpace(ApiKey))
                return true;
            return string.Equals(
                       vendor,
                       "Custom",
                       StringComparison.OrdinalIgnoreCase) &&
                   IsLocalEndpoint(BaseUrl);
        }

        public static bool IsLocalEndpoint(string baseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(
                baseUrl, UriKind.Absolute, out uri))
                return false;
            string host = uri.Host;
            if (string.Equals(
                    host, "localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".local",
                    StringComparison.OrdinalIgnoreCase) ||
                host.IndexOf('.') < 0)
                return true;
            IPAddress address;
            if (!IPAddress.TryParse(host, out address))
                return false;
            if (IPAddress.IsLoopback(address)) return true;
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
                return bytes[0] == 10 ||
                       (bytes[0] == 172 &&
                        bytes[1] >= 16 && bytes[1] <= 31) ||
                       (bytes[0] == 192 && bytes[1] == 168) ||
                       (bytes[0] == 169 && bytes[1] == 254);
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal;
        }
    }
}
