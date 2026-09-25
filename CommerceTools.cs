using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GlobalTranslator
{
    internal sealed class CommerceToolRequest
    {
        public string Tool = "";
        public string Operation = "";
        public string Query = "";
        public Dictionary<string, string> Inputs =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal sealed class CommerceToolOutcome
    {
        public string Title = "";
        public string Detail = "";
        public string Sources = "";
    }

    internal static class CommerceCalculator
    {
        private static readonly CultureInfo Number = CultureInfo.InvariantCulture;

        private static decimal Read(CommerceToolRequest request, string key)
        {
            string value;
            decimal parsed;
            if (request.Inputs == null ||
                !request.Inputs.TryGetValue(key, out value) ||
                !decimal.TryParse(value, NumberStyles.Number, Number,
                    out parsed))
                throw new InvalidOperationException("计算缺少有效参数：" + key);
            return parsed;
        }

        private static decimal Positive(CommerceToolRequest request, string key)
        {
            decimal value = Read(request, key);
            if (value <= 0) throw new InvalidOperationException(
                key + " 必须大于 0。");
            return value;
        }

        private static string Text(CommerceToolRequest request, string key)
        {
            string value;
            if (request.Inputs == null ||
                !request.Inputs.TryGetValue(key, out value) ||
                string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("计算缺少参数：" + key);
            return value.Trim().ToLowerInvariant();
        }

        private static string N(decimal value, int digits)
        {
            return decimal.Round(value, digits,
                MidpointRounding.AwayFromZero).ToString(
                "N" + digits, Number);
        }

        internal static CommerceToolOutcome Execute(CommerceToolRequest request)
        {
            if (request == null) throw new ArgumentNullException("request");
            string operation = (request.Operation ?? "").Trim().ToLowerInvariant();
            decimal a, b, result;
            string detail;
            switch (operation)
            {
                case "arithmetic":
                    a = Read(request, "a"); b = Read(request, "b");
                    string operatorName;
                    if (request.Inputs == null ||
                        !request.Inputs.TryGetValue("operator", out operatorName))
                        throw new InvalidOperationException("缺少运算符。");
                    switch (operatorName)
                    {
                        case "+": result = a + b; break;
                        case "-": result = a - b; break;
                        case "*": result = a * b; break;
                        case "/":
                            if (b == 0) throw new DivideByZeroException();
                            result = a / b; break;
                        default: throw new InvalidOperationException("不支持的运算符。");
                    }
                    detail = N(a, 4) + " " + operatorName + " " +
                        N(b, 4) + " = " + N(result, 4);
                    break;
                case "percentage":
                    a = Read(request, "part");
                    b = Positive(request, "total");
                    result = a / b * 100;
                    detail = N(a, 4) + " ÷ " + N(b, 4) +
                        " × 100 = " + N(result, 2) + "%";
                    break;
                case "quote":
                    a = Positive(request, "quantity");
                    b = Read(request, "unit_price");
                    decimal discount = Read(request, "discount_percent");
                    decimal fee = Read(request, "fee");
                    if (b < 0 || fee < 0 || discount < 0 || discount > 100)
                        throw new InvalidOperationException("报价参数超出范围。");
                    result = a * b * (1 - discount / 100) + fee;
                    detail = N(a, 0) + " × " + N(b, 2) +
                        " × (1 - " + N(discount, 2) + "%) + " +
                        N(fee, 2) + " = " + N(result, 2);
                    break;
                case "margin":
                    a = Positive(request, "revenue");
                    b = Read(request, "cost");
                    if (b < 0) throw new InvalidOperationException("成本不能为负数。");
                    result = (a - b) / a * 100;
                    detail = "毛利率 = (收入 " + N(a, 2) + " - 成本 " +
                        N(b, 2) + ") / 收入 × 100 = " + N(result, 2) + "%";
                    break;
                case "markup":
                    a = Positive(request, "cost");
                    b = Read(request, "revenue");
                    result = (b - a) / a * 100;
                    detail = "加价率 = (收入 " + N(b, 2) + " - 成本 " +
                        N(a, 2) + ") / 成本 × 100 = " + N(result, 2) + "%";
                    break;
                case "boxes":
                    a = Positive(request, "quantity");
                    b = Positive(request, "units_per_box");
                    if (a != decimal.Truncate(a) ||
                        b != decimal.Truncate(b))
                        throw new InvalidOperationException(
                            "件数和每箱件数必须是整数。");
                    decimal boxes = decimal.Ceiling(a / b);
                    result = boxes;
                    detail = N(a, 0) + " 件 ÷ " + N(b, 0) +
                        " 件/箱 = " + N(boxes, 0) + " 箱；尾箱 " +
                        N(a - decimal.Floor(a / b) * b, 0) + " 件";
                    break;
                case "weight":
                    a = Positive(request, "quantity");
                    b = Positive(request, "net_per_unit_kg");
                    decimal boxCount = Positive(request, "box_count");
                    decimal tare = Read(request, "tare_per_box_kg");
                    if (tare < 0 || boxCount != decimal.Truncate(boxCount))
                        throw new InvalidOperationException("包装重量或箱数无效。");
                    decimal net = a * b;
                    result = net + boxCount * tare;
                    detail = "净重 " + N(a, 0) + " × " + N(b, 3) +
                        " = " + N(net, 3) + " kg；毛重 = 净重 + " +
                        N(boxCount, 0) + " 箱 × " + N(tare, 3) +
                        " kg = " + N(result, 3) + " kg";
                    break;
                case "convert":
                    a = Read(request, "value");
                    string from = Text(request, "from_unit");
                    string to = Text(request, "to_unit");
                    var unitScale = new Dictionary<string, decimal>
                    {
                        { "mm", 0.001m }, { "cm", 0.01m },
                        { "m", 1m }, { "g", 0.001m },
                        { "kg", 1m }, { "t", 1000m }
                    };
                    decimal fromScale, toScale;
                    if (!unitScale.TryGetValue(from, out fromScale) ||
                        !unitScale.TryGetValue(to, out toScale) ||
                        ((from == "mm" || from == "cm" || from == "m") !=
                         (to == "mm" || to == "cm" || to == "m")))
                        throw new InvalidOperationException("单位不兼容或不受支持。");
                    result = a * fromScale / toScale;
                    detail = N(a, 4) + " " + from + " = " +
                        N(result, 4) + " " + to;
                    break;
                case "volume":
                    a = Positive(request, "length_cm");
                    b = Positive(request, "width_cm");
                    decimal height = Positive(request, "height_cm");
                    decimal count = Positive(request, "box_count");
                    result = a * b * height * count / 1000000;
                    detail = N(a, 2) + " × " + N(b, 2) + " × " +
                        N(height, 2) + " cm × " + N(count, 0) +
                        " 箱 ÷ 1,000,000 = " + N(result, 4) + " m³";
                    break;
                case "freight":
                    a = Positive(request, "actual_kg");
                    b = Positive(request, "length_cm") *
                        Positive(request, "width_cm") *
                        Positive(request, "height_cm") *
                        Positive(request, "box_count") /
                        Positive(request, "divisor");
                    decimal rate = Positive(request, "rate_per_kg");
                    decimal increment = Positive(request,
                        "rounding_increment_kg");
                    fee = Read(request, "fee");
                    if (fee < 0) throw new InvalidOperationException("附加费不能为负数。");
                    decimal chargeable = decimal.Ceiling(
                        Math.Max(a, b) / increment) * increment;
                    result = chargeable * rate + fee;
                    detail = "实重 " + N(a, 2) + " kg；体积重 " +
                        N(b, 2) + " kg；取较高值并按 " +
                        N(increment, 2) + " kg 进位，计费重 " +
                        N(chargeable, 2) + " kg × " + N(rate, 2) +
                        " + 附加费 " + N(fee, 2) + " = " + N(result, 2) +
                        "（不含其他未提供的费用）";
                    break;
                case "fx":
                    a = Read(request, "amount");
                    b = Positive(request, "rate_to_target");
                    string sourceCurrency = Text(request, "source_currency");
                    string targetCurrency = Text(request, "target_currency");
                    if (sourceCurrency == targetCurrency)
                        throw new InvalidOperationException("源币种与目标币种相同。");
                    result = a * b;
                    detail = N(a, 2) + " " +
                        sourceCurrency.ToUpperInvariant() +
                        " × 已确认汇率 " + N(b, 6) +
                        " = " + N(result, 2) + " " +
                        targetCurrency.ToUpperInvariant();
                    break;
                default:
                    throw new InvalidOperationException("不支持的计算类型：" +
                        operation);
            }
            return new CommerceToolOutcome
            {
                Title = "本地计算 · " + operation,
                Detail = detail
            };
        }
    }

    internal sealed class CommerceSearch : IDisposable
    {
        private readonly HttpClient _http;
        internal CommerceSearch(HttpMessageHandler handler = null)
        {
            _http = handler == null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromSeconds(20);
        }

        internal async Task<CommerceToolOutcome> SearchAsync(
            string query, string apiKey, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
                throw new InvalidOperationException("请先配置搜索 API Key。");
            if (string.IsNullOrWhiteSpace(query) || query.Length > 400)
                throw new InvalidOperationException("搜索词应在 1–400 字符之间。");
            var payload = new JavaScriptSerializer().Serialize(new
            {
                query = query,
                search_depth = "basic",
                max_results = 5,
                include_answer = false,
                include_raw_content = false
            });
            using (var request = new HttpRequestMessage(HttpMethod.Post,
                "https://api.tavily.com/search"))
            {
                request.Headers.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer", apiKey.Trim());
                request.Content = new StringContent(payload, Encoding.UTF8,
                    "application/json");
                using (HttpResponseMessage response = await _http.SendAsync(
                    request, token))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException(
                            "联网搜索失败（HTTP " +
                            (int)response.StatusCode + "）。请检查搜索 Key。");
                    string body = await response.Content.ReadAsStringAsync();
                    if (body.Length > 1000000)
                        throw new InvalidOperationException(
                            "搜索结果过大，请缩小搜索范围。");
                    var data = new JavaScriptSerializer().DeserializeObject(body)
                        as Dictionary<string, object>;
                    object raw;
                    object[] hits = data != null &&
                        data.TryGetValue("results", out raw)
                        ? raw as object[] : null;
                    var lines = new List<string>();
                    var sources = new List<string>();
                    foreach (var item in (hits ?? new object[0]).Take(5))
                    {
                        var hit = item as Dictionary<string, object>;
                        if (hit == null) continue;
                        string title = Read(hit, "title");
                        string url = Read(hit, "url");
                        Uri source;
                        if (!Uri.TryCreate(url, UriKind.Absolute,
                            out source) ||
                            (source.Scheme != Uri.UriSchemeHttps &&
                             source.Scheme != Uri.UriSchemeHttp))
                            continue;
                        string summary = Read(hit, "content");
                        if (summary.Length > 1200)
                            summary = summary.Substring(0, 1200);
                        lines.Add(title + "\n" + summary + "\n" + url);
                        string published = Read(hit, "published_date");
                        sources.Add(title + " · " + url +
                            (published.Length == 0 ? "" :
                                " · 发布 " + published));
                    }
                    return new CommerceToolOutcome
                    {
                        Title = "联网资料 · " + query,
                        Detail = lines.Count == 0 ? "没有找到可用来源。" :
                            string.Join("\n\n", lines),
                        Sources = "检索于 " + DateTime.Now.ToString(
                            "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
                            "\n" + string.Join("\n", sources)
                    };
                }
            }
        }

        private static string Read(Dictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string ?? "" : "";
        }

        public void Dispose() { _http.Dispose(); }
    }
}
