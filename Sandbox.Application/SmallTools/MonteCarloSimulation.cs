using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sandbox.Domain.Calculations;
using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.SmallTools
{
    public class MonteCarloSimulation : IWorker
    {
        private const int RequiredHistoryCount = 250;

        private readonly QuantResearchDbContext db;
        private readonly StockMonteCarloSimulator simulator;

        public MonteCarloSimulation(QuantResearchDbContext db, StockMonteCarloSimulator simulator)
        {
            this.db = db;
            this.simulator = simulator;
        }

        public void Run() => RunAsync().GetAwaiter().GetResult();

        public async Task RunAsync()
        {
            var stockCode = await SelectStockAsync();
            if (stockCode == null)
                return;

            var buyPrice = ReadDouble("请输入买入价", 10.6);
            if (buyPrice == null)
                return;

            var holdingDays = ReadInt("请输入持有天数", 10);
            if (holdingDays == null)
                return;

            var simulations = ReadInt("请输入模拟次数", 100_000);
            if (simulations == null)
                return;

            var targetReturn = ReadDouble("请输入目标收益率(%)", 20);
            if (targetReturn == null)
                return;

            var stopLossReturn = ReadDouble("请输入止损收益率(%)", -10);
            if (stopLossReturn == null)
                return;

            double[] closePrices = await GetClosePricesAsync(stockCode, RequiredHistoryCount);

            var result = simulator.Simulate(
                closePrices: closePrices,
                buyPrice: buyPrice.Value,
                holdingDays: holdingDays.Value,
                simulations: simulations.Value,
                targetReturn: targetReturn.Value / 100,
                stopLossReturn: stopLossReturn.Value / 100
            );

            Console.WriteLine($"盈利概率：{result.ProfitProbability:P2}");
            Console.WriteLine($"达到目标收益概率：{result.TargetProbability:P2}");
            Console.WriteLine($"触及止损概率：{result.StopLossProbability:P2}");
            Console.WriteLine($"平均收益率：{result.AverageReturn:P2}");
            Console.WriteLine($"收益率中位数：{result.MedianReturn:P2}");
            Console.WriteLine($"收益率5%分位：{result.P5Return:P2}");
            Console.WriteLine($"收益率95%分位：{result.P95Return:P2}");
        }

        /// <summary>
        /// 获取历史复权收盘价：优先从数据库读取，数据不足时回退到东方财富接口。
        /// </summary>
        private async Task<double[]> GetClosePricesAsync(string stockCode, int count)
        {
            var dbPrices = await db.StockDailyKlines
                .AsNoTracking()
                .Where(k => k.StockCode == stockCode)
                .OrderBy(k => k.TradeDate)
                .Select(k => k.ClosePrice)
                .ToListAsync();

            if (dbPrices.Count >= count)
            {
                Console.WriteLine($"从数据库读取到 {dbPrices.Count} 条历史数据。");
                return dbPrices.TakeLast(count).Select(p => (double)p).ToArray();
            }

            Console.WriteLine($"数据库仅有 {dbPrices.Count} 条历史数据（需要 {count} 条），改为调用接口获取...");
            var result = await StockHistoryService.GetKlineDataAsync(stockCode);
            if (result.Bars.Count < count)
                throw new InvalidOperationException($"历史数据不足：需要 {count} 条，实际获取 {result.Bars.Count} 条");

            return result.Bars.TakeLast(count).Select(bar => (double)bar.ClosePrice).ToArray();
        }

        /// <summary>
        /// 从数据库读取已导入的股票列表供选择，列表为空时回退到手动输入代码。
        /// </summary>
        private async Task<string?> SelectStockAsync()
        {
            var stocks = await db.Stocks
                .AsNoTracking()
                .OrderBy(s => s.StockCode)
                .ToListAsync();

            if (stocks.Count == 0)
            {
                Console.WriteLine("数据库中没有已导入的股票，请先使用「股票数据导入」导入，或手动输入代码。");
                return ReadStockCode();
            }

            while (true)
            {
                Console.WriteLine("\n已导入的股票：");
                for (int i = 0; i < stocks.Count; i++)
                {
                    var name = string.IsNullOrWhiteSpace(stocks[i].StockName)
                        ? string.Empty
                        : $"  {stocks[i].StockName}";
                    Console.WriteLine($"  {i + 1,2}. {stocks[i].StockCode}{name}");
                }
                Console.WriteLine("   0. 手动输入股票代码");

                Console.Write("请选择股票编号（q 退出）: ");
                var input = Console.ReadLine()?.Trim();

                if (input == null || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (input == "0")
                    return ReadStockCode();

                if (int.TryParse(input, out var number) && number >= 1 && number <= stocks.Count)
                    return stocks[number - 1].StockCode;

                Console.WriteLine("编号无效，请重新输入。");
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

        private static double? ReadDouble(string prompt, double defaultValue)
        {
            while (true)
            {
                Console.Write($"{prompt} [{defaultValue:0.####}]: ");
                var input = Console.ReadLine()?.Trim();

                if (input == null || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (input.Length == 0)
                    return defaultValue;

                if (double.TryParse(input, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                        CultureInfo.InvariantCulture, out var value))
                    return value;

                Console.WriteLine("请输入有效数字，使用小数点分隔小数。");
            }
        }

        private static int? ReadInt(string prompt, int defaultValue)
        {
            while (true)
            {
                Console.Write($"{prompt} [{defaultValue}]: ");
                var input = Console.ReadLine()?.Trim();

                if (input == null || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;

                if (input.Length == 0)
                    return defaultValue;

                if (int.TryParse(input, NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture, out var value))
                    return value;

                Console.WriteLine("请输入有效整数。");
            }
        }
    }
}
