using System.Globalization;
using System.Text.Json;

namespace Sandbox.Domain.Util
{
    public static class StockHistoryService
    {
        private static readonly HttpClient Http = new();

        /// <summary>
        /// 获取股票历史日 K 线数据（OHLCV）及股票名称，K 线按交易日期升序返回。
        /// </summary>
        /// <param name="stockCode">股票代码，6 位数字。</param>
        /// <param name="begin">起始日期，默认 2020-01-01。</param>
        /// <param name="end">结束日期，默认今天。</param>
        public static async Task<StockKlineResult> GetKlineDataAsync(
            string stockCode,
            DateOnly? begin = null,
            DateOnly? end = null)
        {
            if (string.IsNullOrWhiteSpace(stockCode))
                throw new ArgumentException("股票代码不能为空", nameof(stockCode));

            // 创业板、深市股票使用 0；沪市使用 1
            string market = stockCode.StartsWith('6') ? "1" : "0";

            string beg = (begin ?? new DateOnly(2020, 1, 1)).ToString("yyyyMMdd");
            string endDate = (end ?? DateOnly.FromDateTime(DateTime.Now)).ToString("yyyyMMdd");

            string url =
                "https://push2his.eastmoney.com/api/qt/stock/kline/get" +
                $"?secid={market}.{stockCode}" +
                "&fields1=f1,f2,f3,f4,f5,f6" +
                "&fields2=f51,f52,f53,f54,f55,f56" +
                "&klt=101" +
                "&fqt=1" +
                $"&beg={beg}" +
                $"&end={endDate}" +
                "&lmt=10000";

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

            // 股票名称，部分接口返回为空，保持空字符串即可。
            string stockName = string.Empty;
            if (data.TryGetProperty("name", out var nameElement) &&
                nameElement.ValueKind == JsonValueKind.String)
            {
                stockName = nameElement.GetString() ?? string.Empty;
            }

            var result = new List<StockKlineBar>();

            foreach (var item in klines.EnumerateArray())
            {
                var fields = item.GetString()!.Split(',');

                result.Add(new StockKlineBar(
                    TradeDate: DateOnly.ParseExact(fields[0], "yyyy-MM-dd"),
                    OpenPrice: decimal.Parse(fields[1], CultureInfo.InvariantCulture),
                    ClosePrice: decimal.Parse(fields[2], CultureInfo.InvariantCulture),
                    HighPrice: decimal.Parse(fields[3], CultureInfo.InvariantCulture),
                    LowPrice: decimal.Parse(fields[4], CultureInfo.InvariantCulture),
                    Volume: long.Parse(fields[5], CultureInfo.InvariantCulture)));
            }

            return new StockKlineResult(
                stockName,
                result.OrderBy(x => x.TradeDate).ToList());
        }
    }

    /// <summary>
    /// 股票历史 K 线获取结果，包含股票名称和日 K 线数据。
    /// </summary>
    public sealed record StockKlineResult(
        string StockName,
        List<StockKlineBar> Bars);

    /// <summary>
    /// 股票日 K 线一条数据（OHLCV），供导入等场景使用。
    /// </summary>
    public sealed record StockKlineBar(
        DateOnly TradeDate,
        decimal OpenPrice,
        decimal HighPrice,
        decimal LowPrice,
        decimal ClosePrice,
        long Volume);
}
