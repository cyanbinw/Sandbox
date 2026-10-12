using Microsoft.EntityFrameworkCore;
using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;
using Sandbox.Infrastructure.Repositories;

namespace Sandbox.Application.Services
{
    /// <summary>
    /// 股票数据导入：根据用户填写的股票代码，从东方财富拉取历史日 K 线数据并写入数据库。
    /// 已存在的（按股票代码 + 交易日期）记录会被跳过，避免重复导入。
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

            // 已入库的交易日期，用于跳过重复记录。
            var existingDates = await db.StockDailyKlines
                .AsNoTracking()
                .Where(k => k.StockCode == stockCode)
                .Select(k => k.TradeDate)
                .ToListAsync();

            var existingSet = existingDates.ToHashSet();

            var newRows = bars
                .Where(b => !existingSet.Contains(b.TradeDate))
                .Select(b => new StockDailyKline
                {
                    StockCode = stockCode,
                    TradeDate = b.TradeDate,
                    OpenPrice = b.OpenPrice,
                    HighPrice = b.HighPrice,
                    LowPrice = b.LowPrice,
                    ClosePrice = b.ClosePrice,
                    Volume = b.Volume,
                    AdjustType = 1,
                    DataSource = DataSource
                })
                .ToList();

            if (newRows.Count > 0)
                db.StockDailyKlines.AddRange(newRows);

            await db.SaveChangesAsync();

            Console.WriteLine(newRows.Count == 0
                ? $"股票 {stockCode} 的数据已是最新，无需导入。"
                : $"导入完成：本次新增 {newRows.Count} 条，跳过已存在 {bars.Count - newRows.Count} 条。");
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
