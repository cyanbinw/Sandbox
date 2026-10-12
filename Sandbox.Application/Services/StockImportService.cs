using Microsoft.EntityFrameworkCore;
using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;
using Sandbox.Infrastructure.Repositories;

namespace Sandbox.Application.Services
{
    /// <summary>
    /// 股票数据导入：根据用户填写的股票代码，从东方财富拉取历史日 K 线数据并写入数据库。
    /// 按股票代码、复权类型、数据来源及交易日期更新或新增记录。
    /// </summary>
    public class StockImportService : IWorker
    {
        private const string DataSource = "EastMoney";

        private readonly QuantResearchDbContext db;

        public StockImportService(QuantResearchDbContext db)
        {
            this.db = db;
        }

        public void Run() => RunAsync().GetAwaiter().GetResult();

        public async Task RunAsync()
        {
            var stockCode = ReadStockCode();
            if (stockCode == null)
                return;

            var endDate = DateOnly.FromDateTime(DateTime.Today);
            Console.WriteLine($"正在获取 {stockCode} 截至 {endDate:yyyy-MM-dd} 的历史日 K 线数据...");
            var result = await StockHistoryService.GetKlineDataAsync(stockCode, end: endDate);
            var bars = result.Bars;

            if (bars.Count == 0)
            {
                Console.WriteLine("未获取到任何数据，请确认股票代码是否正确。");
                return;
            }

            // 更新股票基础信息（代码 + 名称），供选股展示使用。
            await UpsertStockAsync(stockCode, result.StockName);

            // 同一次拉取刷新整个返回区间，避免前复权历史价格与旧版本拼接。
            var firstDate = bars.Min(b => b.TradeDate);
            var lastDate = bars.Max(b => b.TradeDate);
            var existingRows = await db.StockDailyKlines
                .Where(k => k.StockCode == stockCode && k.AdjustType == 1 &&
                    k.DataSource == DataSource && k.TradeDate >= firstDate && k.TradeDate <= lastDate)
                .ToListAsync();
            var rowsByDate = existingRows.ToDictionary(k => k.TradeDate);
            var updatedAt = DateTime.UtcNow;
            var added = 0;
            foreach (var bar in bars)
            {
                if (!rowsByDate.TryGetValue(bar.TradeDate, out var row))
                {
                    row = new StockDailyKline
                    {
                        StockCode = stockCode, TradeDate = bar.TradeDate,
                        AdjustType = 1, DataSource = DataSource
                    };
                    db.StockDailyKlines.Add(row);
                    rowsByDate.Add(bar.TradeDate, row);
                    added++;
                }
                row.OpenPrice = bar.OpenPrice;
                row.HighPrice = bar.HighPrice;
                row.LowPrice = bar.LowPrice;
                row.ClosePrice = bar.ClosePrice;
                row.Volume = bar.Volume;
                row.UpdatedAt = updatedAt;
            }

            await db.SaveChangesAsync();
            Console.WriteLine($"导入完成：新增 {added} 条，刷新 {bars.Count - added} 条前复权数据。");
        }
        private async Task UpsertStockAsync(string stockCode, string stockName)
        {
            var stock = await db.Stocks.FirstOrDefaultAsync(s => s.StockCode == stockCode);

            if (stock == null)
            {
                db.Stocks.Add(new Stock { StockCode = stockCode, StockName = stockName });
            }
            else if (!string.IsNullOrWhiteSpace(stockName) && stock.StockName != stockName)
            {
                stock.StockName = stockName;
                stock.UpdatedAt = DateTime.UtcNow;
            }
        }

        private static string? ReadStockCode()
        {
            while (true)
            {
                Console.Write("请输入股票代码（输入 q 退出）: ");
                var input = Console.ReadLine()?.Trim();

                if (input == null || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (input.Length != 6 || !input.All(char.IsDigit))
                {
                    Console.WriteLine("股票代码应为 6 位数字，请重新输入。");
                    continue;
                }

                return input;
            }
        }
    }
}
