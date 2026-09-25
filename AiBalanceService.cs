using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace GlobalTranslator
{
    internal sealed class AiBalanceService : IDisposable
    {
        private readonly HttpClient _client;

        public AiBalanceService() : this(new HttpClientHandler { AllowAutoRedirect = false }) { }

        internal AiBalanceService(HttpMessageHandler handler)
        {
            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromSeconds(10);
            _client.MaxResponseContentBufferSize = 65536;
        }

        public static string UnavailableReason(string vendor, ModelConnectionSettings connection)
        {
            if (!string.Equals(vendor, "DeepSeek", StringComparison.OrdinalIgnoreCase))
                return "暂不支持查询";
            Uri address;
            if (connection == null || !Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out address) ||
                address.Scheme != "https" || address.Host != "api.deepseek.com" || address.Port != 443 ||
                address.UserInfo.Length != 0 || address.Query.Length != 0 || address.Fragment.Length != 0)
                return "仅支持 DeepSeek 官方接口";
            string path = address.AbsolutePath.TrimEnd('/');
            if (path != "" && path != "/v1" && path != "/anthropic" && path != "/anthropic/v1")
                return "仅支持 DeepSeek 官方接口";
            if (string.IsNullOrWhiteSpace(connection.ApiKey)) return "请先配置密钥";
            return "";
        }

        public async Task<string> QueryAsync(string vendor, ModelConnectionSettings connection,
            CancellationToken token)
        {
            string unavailable = UnavailableReason(vendor, connection);
            if (unavailable.Length != 0) throw new InvalidOperationException(unavailable);
            using (var request = new HttpRequestMessage(HttpMethod.Get,
                "https://api.deepseek.com/user/balance"))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", connection.ApiKey.Trim());
                using (var response = await _client.SendAsync(request, token))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException("余额查询失败（HTTP " + (int)response.StatusCode + "）");
                    return Parse(await response.Content.ReadAsStringAsync());
                }
            }
        }

        internal static string Parse(string json)
        {
            var data = new JavaScriptSerializer { MaxJsonLength = 65536 }.Deserialize<BalanceResponse>(json);
            if (data == null || data.balance_infos == null || data.balance_infos.Length == 0 ||
                data.balance_infos.Length > 5 || !data.is_available.HasValue)
                throw new FormatException("余额数据格式无效");
            var amounts = new List<string>();
            foreach (BalanceInfo info in data.balance_infos)
            {
                decimal amount;
                if (info == null || (info.currency != "CNY" && info.currency != "USD") ||
                    !decimal.TryParse(info.total_balance, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out amount))
                    throw new FormatException("余额数据格式无效");
                amounts.Add(info.currency + " " + amount.ToString("0.00####", CultureInfo.InvariantCulture));
            }
            return string.Join(" / ", amounts) + (data.is_available.Value ? "" : " · 账户不可用");
        }

        public void Dispose() { _client.Dispose(); }

        public sealed class BalanceResponse
        {
            public bool? is_available { get; set; }
            public BalanceInfo[] balance_infos { get; set; }
        }
        public sealed class BalanceInfo
        {
            public string currency { get; set; }
            public string total_balance { get; set; }
        }
    }
}
