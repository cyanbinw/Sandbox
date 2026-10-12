using System.Globalization;
using System.Text.Json;

namespace Sandbox.Domain.Util
{
    public static class StockHistoryService
    {
        private static readonly HttpClient Http = new();

        public static async Task<double[]> GetClosePricesAsync(
            string stockCode,
            int count = 250)
        {
            // 创业板、深市股票使用 0；沪市使用 1
            string market = stockCode.StartsWith('6') ? "1" : "0";

            string url =
                "https://push2his.eastmoney.com/api/qt/stock/kline/get" +
                $"?secid={market}.{stockCode}" +
                "&fields1=f1,f2,f3,f4,f5,f6" +
                "&fields2=f51,f52,f53,f54,f55,f56" +
                "&klt=101" +            
                "&fqt=1" +              
                "&beg=20240101" +
                "&end=20261009" +
                $"&lmt={count + 1}";

            using var response = await Http.GetAsync(url);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Object)
            {
                throw new Exception("data 不存在或格式错误");
            }

            if (!data.TryGetProperty("klines", out var klines) ||
                klines.ValueKind != JsonValueKind.Array)
            {
                throw new Exception("klines 不存在或不是数组");
            }

            var prices = klines.EnumerateArray()
                .Select(item =>
                {
                    var fields = item.GetString()!.Split(',');

                    return new
                    {
                        Date = DateOnly.ParseExact(
                            fields[0], "yyyy-MM-dd"),
                        Close = double.Parse(
                            fields[2],
                            CultureInfo.InvariantCulture)
                    };
                })
                .OrderBy(x => x.Date)
                .TakeLast(count)
                .Select(x => x.Close)
                .ToArray();

            if (prices.Length < count)
                throw new Exception(
                    $"历史数据不足：需要 {count} 条，" +
                    $"实际获取 {prices.Length} 条");

            return prices;
        }
    }
}
