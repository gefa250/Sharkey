using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GlobalTranslator
{
    internal sealed class CommerceToolActivity
    {
        public string Tool = "";
        public string Label = "";
        public string Query = "";
        public string Status = "running";
        public string Summary = "";
    }

    // Local clock/timezone tool: no network, deterministic, works offline.
    internal static class AgentTimeTool
    {
        private static readonly Dictionary<string, string> Zones =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "纽约", "Eastern Standard Time" }, { "纽约市", "Eastern Standard Time" },
                { "new york", "Eastern Standard Time" }, { "nyc", "Eastern Standard Time" },
                { "华盛顿", "Eastern Standard Time" }, { "washington", "Eastern Standard Time" },
                { "波士顿", "Eastern Standard Time" }, { "boston", "Eastern Standard Time" },
                { "迈阿密", "Eastern Standard Time" }, { "miami", "Eastern Standard Time" },
                { "亚特兰大", "Eastern Standard Time" }, { "atlanta", "Eastern Standard Time" },
                { "多伦多", "Eastern Standard Time" }, { "toronto", "Eastern Standard Time" },
                { "芝加哥", "Central Standard Time" }, { "chicago", "Central Standard Time" },
                { "休斯顿", "Central Standard Time" }, { "houston", "Central Standard Time" },
                { "达拉斯", "Central Standard Time" }, { "dallas", "Central Standard Time" },
                { "丹佛", "Mountain Standard Time" }, { "denver", "Mountain Standard Time" },
                { "凤凰城", "US Mountain Standard Time" }, { "phoenix", "US Mountain Standard Time" },
                { "洛杉矶", "Pacific Standard Time" }, { "los angeles", "Pacific Standard Time" },
                { "旧金山", "Pacific Standard Time" }, { "san francisco", "Pacific Standard Time" },
                { "西雅图", "Pacific Standard Time" }, { "seattle", "Pacific Standard Time" },
                { "拉斯维加斯", "Pacific Standard Time" }, { "las vegas", "Pacific Standard Time" },
                { "温哥华", "Pacific Standard Time" }, { "vancouver", "Pacific Standard Time" },
                { "阿拉斯加", "Alaskan Standard Time" }, { "alaska", "Alaskan Standard Time" },
                { "夏威夷", "Hawaiian Standard Time" }, { "hawaii", "Hawaiian Standard Time" },
                { "北京", "China Standard Time" }, { "上海", "China Standard Time" },
                { "深圳", "China Standard Time" }, { "广州", "China Standard Time" },
                { "beijing", "China Standard Time" }, { "shanghai", "China Standard Time" },
                { "香港", "China Standard Time" }, { "hong kong", "China Standard Time" },
                { "台北", "Taipei Standard Time" }, { "taipei", "Taipei Standard Time" },
                { "新加坡", "Singapore Standard Time" }, { "singapore", "Singapore Standard Time" },
                { "东京", "Tokyo Standard Time" }, { "tokyo", "Tokyo Standard Time" },
                { "大阪", "Tokyo Standard Time" }, { "osaka", "Tokyo Standard Time" },
                { "首尔", "Korea Standard Time" }, { "seoul", "Korea Standard Time" },
                { "伦敦", "GMT Standard Time" }, { "london", "GMT Standard Time" },
                { "巴黎", "Romance Standard Time" }, { "paris", "Romance Standard Time" },
                { "柏林", "W. Europe Standard Time" }, { "berlin", "W. Europe Standard Time" },
                { "罗马", "W. Europe Standard Time" }, { "rome", "W. Europe Standard Time" },
                { "马德里", "Romance Standard Time" }, { "madrid", "Romance Standard Time" },
                { "莫斯科", "Russian Standard Time" }, { "moscow", "Russian Standard Time" },
                { "迪拜", "Arabian Standard Time" }, { "dubai", "Arabian Standard Time" },
                { "悉尼", "AUS Eastern Standard Time" }, { "sydney", "AUS Eastern Standard Time" },
                { "墨尔本", "AUS Eastern Standard Time" }, { "melbourne", "AUS Eastern Standard Time" },
                { "奥克兰", "New Zealand Standard Time" }, { "auckland", "New Zealand Standard Time" },
                { "圣保罗", "E. South America Standard Time" }, { "sao paulo", "E. South America Standard Time" },
                { "墨西哥城", "Central Standard Time (Mexico)" }, { "mexico city", "Central Standard Time (Mexico)" },
                { "孟买", "India Standard Time" }, { "mumbai", "India Standard Time" },
                { "新德里", "India Standard Time" }, { "delhi", "India Standard Time" }
            };

        private static readonly string[] Defaults = { "纽约", "洛杉矶", "芝加哥", "北京" };

        internal static CommerceToolOutcome Execute(CommerceToolRequest request)
        {
            string raw = "";
            if (request != null && request.Inputs != null)
            {
                if (!request.Inputs.TryGetValue("cities", out raw) || string.IsNullOrWhiteSpace(raw))
                    if (!request.Inputs.TryGetValue("timezone", out raw)) raw = "";
            }
            if (string.IsNullOrWhiteSpace(raw) && request != null) raw = request.Query ?? "";
            string[] names = string.IsNullOrWhiteSpace(raw)
                ? Defaults
                : Regex.Split(raw, @"[,，;；、\s]+").Where(item => item.Trim().Length > 0).Select(item => item.Trim()).ToArray();
            if (names.Length == 0) names = Defaults;
            if (names.Length > 8) names = names.Take(8).ToArray();

            DateTime now = DateTime.Now;
            var lines = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string name in names)
            {
                TimeZoneInfo zone;
                string label;
                if (!TryResolve(name, out zone, out label))
                {
                    lines.Add(name + "：无法识别该城市，请给出时区 ID（例如 America/New_York）。");
                    continue;
                }
                string key = zone.Id + "|" + label;
                if (!seen.Add(key)) continue;
                DateTime local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local, zone);
                TimeSpan offset = zone.GetUtcOffset(local);
                bool dst = zone.IsDaylightSavingTime(local);
                lines.Add(label + "：" + local.ToString("yyyy-MM-dd HH:mm") +
                    "（UTC" + Format(offset) + (dst ? "，夏令时" : "") + "）");
            }
            return new CommerceToolOutcome
            {
                Title = "当前时间",
                Detail = "本机时间：" + now.ToString("yyyy-MM-dd HH:mm") + "（UTC" + Format(TimeZoneInfo.Local.GetUtcOffset(now)) + "）\n" +
                    string.Join("\n", lines.ToArray())
            };
        }

        internal static bool TryResolve(string name, out TimeZoneInfo zone, out string label)
        {
            zone = null;
            label = (name ?? "").Trim();
            if (label.Length == 0) return false;
            string id;
            if (!Zones.TryGetValue(label, out id))
                id = label; // allow raw timezone ids such as America/New_York
            try
            {
                zone = TimeZoneInfo.FindSystemTimeZoneById(id);
                return true;
            }
            catch (TimeZoneNotFoundException) { return false; }
            catch (InvalidTimeZoneException) { return false; }
        }

        private static string Format(TimeSpan value)
        {
            string sign = value < TimeSpan.Zero ? "-" : "+";
            value = value.Duration();
            return sign + value.Hours.ToString("00") + ":" + value.Minutes.ToString("00");
        }
    }

    // Minimal, SSRF-guarded page reader: http(s) only, public hosts, small pages.
    internal static class AgentWebFetchTool
    {
        private const int MaxBytes = 2 * 1024 * 1024;
        private const int MaxChars = 20000;
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        internal static async Task<CommerceToolOutcome> FetchAsync(string rawUrl, CancellationToken token)
        {
            Uri uri;
            if (!TryValidate(rawUrl, out uri))
                throw new InvalidOperationException("只能抓取公开的 http/https 页面。");
            using (var handler = new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
            using (var http = new HttpClient(handler) { Timeout = Timeout })
            {
                using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    timeout.CancelAfter(Timeout);
                    int redirects = 0;
                    while (true)
                    {
                        ValidateHost(uri);
                        using (var request = new HttpRequestMessage(HttpMethod.Get, uri))
                        {
                            request.Headers.TryAddWithoutValidation("User-Agent",
                                "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SharkeyFetch/1.0");
                            using (var response = await http.SendAsync(request,
                                HttpCompletionOption.ResponseHeadersRead, timeout.Token))
                            {
                                if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400 &&
                                    response.Headers.Location != null)
                                {
                                    if (++redirects > 3) throw new InvalidOperationException("页面重定向次数过多。");
                                    uri = response.Headers.Location.IsAbsoluteUri
                                        ? response.Headers.Location
                                        : new Uri(uri, response.Headers.Location);
                                    if (!TryValidate(uri.ToString(), out uri))
                                        throw new InvalidOperationException("只能抓取公开的 http/https 页面。");
                                    continue;
                                }
                                if (!response.IsSuccessStatusCode)
                                    throw new InvalidOperationException("页面返回 HTTP " + (int)response.StatusCode + "。");
                                if (response.Content.Headers.ContentLength > MaxBytes)
                                    throw new InvalidOperationException("页面过大，未抓取。");
                                string html;
                                using (var stream = await response.Content.ReadAsStreamAsync())
                                using (var buffer = new System.IO.MemoryStream())
                                {
                                    byte[] chunk = new byte[16384];
                                    int read, total = 0;
                                    while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, timeout.Token)) > 0)
                                    {
                                        total += read;
                                        if (total > MaxBytes) throw new InvalidOperationException("页面过大，未抓取。");
                                        buffer.Write(chunk, 0, read);
                                    }
                                    html = Encoding.UTF8.GetString(buffer.ToArray());
                                }
                                string title = ExtractTitle(html);
                                string text = ExtractText(html);
                                if (text.Length == 0) throw new InvalidOperationException("页面没有可读正文。");
                                return new CommerceToolOutcome
                                {
                                    Title = title.Length == 0 ? uri.Host : title,
                                    Detail = text,
                                    Sources = uri.ToString()
                                };
                            }
                        }
                    }
                }
            }
        }

        private static bool TryValidate(string rawUrl, out Uri uri)
        {
            uri = null;
            if (string.IsNullOrWhiteSpace(rawUrl)) return false;
            Uri parsed;
            if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out parsed)) return false;
            if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) return false;
            if (parsed.Port != 80 && parsed.Port != 443) return false;
            uri = parsed;
            return true;
        }

        private static void ValidateHost(Uri uri)
        {
            string host = uri.Host;
            if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
                host == "0.0.0.0")
                throw new InvalidOperationException("不允许抓取本机或局域网地址。");
            IPAddress[] addresses;
            IPAddress single;
            if (IPAddress.TryParse(host, out single)) addresses = new[] { single };
            else
            {
                try { addresses = Dns.GetHostAddresses(host); }
                catch (Exception) { throw new InvalidOperationException("无法解析该域名。"); }
            }
            foreach (IPAddress address in addresses)
                if (IsBlocked(address)) throw new InvalidOperationException("不允许抓取本机或局域网地址。");
        }

        internal static bool IsBlocked(IPAddress address)
        {
            if (address == null) return true;
            if (IPAddress.IsLoopback(address)) return true;
            byte[] bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                if (bytes[0] == 10) return true;
                if (bytes[0] == 127) return true;
                if (bytes[0] == 0) return true;
                if (bytes[0] == 169 && bytes[1] == 254) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
                if (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127) return true;
                return false;
            }
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (address.IsIPv6LinkLocal || address.IsIPv6SiteLocal) return true;
                if (bytes.Length == 16 && (bytes[0] & 0xFE) == 0xFC) return true; // fc00::/7
                return false;
            }
            return true;
        }

        internal static string ExtractTitle(string html)
        {
            Match match = Regex.Match(html ?? "", @"<title[^>]*>(.*?)</title>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);
            return match.Success ? WebUtility.HtmlDecode(Clean(match.Groups[1].Value)) : "";
        }

        internal static string ExtractText(string html)
        {
            string value = html ?? "";
            value = Regex.Replace(value, @"<script[\s\S]*?</script>", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"<style[\s\S]*?</style>", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"<head[\s\S]*?</head>", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"<noscript[\s\S]*?</noscript>", " ", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"<(br|/p|/div|/li|/h[1-6]|/tr)[^>]*>", "\n", RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"<[^>]+>", " ");
            value = WebUtility.HtmlDecode(value);
            value = Regex.Replace(value, @"[ \t\f\v]+", " ");
            value = Regex.Replace(value, @"\n\s*\n+", "\n\n");
            value = value.Trim();
            if (value.Length > MaxChars) value = value.Substring(0, MaxChars) + "\n…（已截断）";
            return value;
        }

        private static string Clean(string value)
        {
            return Regex.Replace(value, @"\s+", " ").Trim();
        }
    }
}
