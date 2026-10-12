using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sandbox.Domain.Calculations;
using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.SmallTools
{
    public class MonteCarloSimulation : LoopWorkerBase
    {
        private const int RequiredHistoryCount = 250;

        private readonly QuantResearchDbContext db;
        private readonly StockMonteCarloSimulator simulator;

        public MonteCarloSimulation(QuantResearchDbContext db, StockMonteCarloSimulator simulator)
        {
            this.db = db;
            this.simulator = simulator;
        }

        public override async Task RunAsync()
        {
            Console.WriteLine("蒙特卡罗模拟：完成后可继续选股，任意输入步骤输入 q 返回工具菜单。");
            await base.RunAsync();
        }

        protected override async Task<WorkerLoopResult> RunOnceAsync()
        {
            var stockCode = await SelectStockAsync();
            if (stockCode == null)
                return WorkerLoopResult.Exit;

            Console.WriteLine("模型假设按输入买入价开始持仓；改变买入价不改变收益率概率。");
            var buyPrice = ReadDouble("请输入买入价", 10.6, value => value > 0);
            if (buyPrice == null)
                return WorkerLoopResult.Exit;

            var holdingDays = ReadInt("请输入持有交易日数（1～2520）", 10, 1, StockMonteCarloSimulator.MaxHoldingDays);
            if (holdingDays == null)
                return WorkerLoopResult.Exit;

            var maxSimulations = (int)Math.Min(StockMonteCarloSimulator.MaxSimulations,
                StockMonteCarloSimulator.MaxPathSteps / holdingDays.Value);
            var simulations = ReadInt($"请输入模拟次数（1～{maxSimulations}）", Math.Min(100_000, maxSimulations), 1, maxSimulations);
            if (simulations == null)
                return WorkerLoopResult.Exit;

            var targetReturn = ReadDouble("请输入目标收益率(%，非负）", 20, value => value >= 0);
            if (targetReturn == null)
                return WorkerLoopResult.Exit;

            var stopLossReturn = ReadDouble("请输入止损幅度(%，如 10 或 -10 均表示亏损 10%，绝对值须小于 100）", 10, value => Math.Abs(value) < 100);
            if (stopLossReturn == null)
                return WorkerLoopResult.Exit;
            stopLossReturn = -Math.Abs(stopLossReturn.Value);
            Console.WriteLine($"止损收益率：{stopLossReturn.Value:0.####}%，每日收盘收益率达到或低于该值时触发。");

            var mode = ReadInt("模式：1 持有到期，2 按触发日收盘价止损", 1, 1, 2);
            if (mode == null)
                return WorkerLoopResult.Exit;
            var seed = ReadInt("请输入随机种子（相同参数可复现）", 42, int.MinValue, int.MaxValue);
            if (seed == null)
                return WorkerLoopResult.Exit;

            Console.WriteLine("止损按每日收盘判断，执行模式假设按触发日收盘价成交；往返成本为本金的 0.1%。");
            double[] closePrices = await GetClosePricesAsync(stockCode, RequiredHistoryCount);

            var result = simulator.Simulate(
                closePrices: closePrices,
                buyPrice: buyPrice.Value,
                holdingDays: holdingDays.Value,
                simulations: simulations.Value,
                targetReturn: targetReturn.Value / 100,
                stopLossReturn: stopLossReturn.Value / 100,
                executeStopLoss: mode == 2,
                seed: seed.Value
            );

            Console.WriteLine($"盈利概率：{result.ProfitProbability:P2}");
            Console.WriteLine($"达到目标收益概率：{result.TargetProbability:P2}");
            Console.WriteLine($"每日收盘触及止损线概率：{result.StopLossProbability:P2}");
            Console.WriteLine($"统计模式：{(mode == 2 ? "执行止损" : "持有到期")}，随机种子：{seed.Value}");
            Console.WriteLine($"平均收益率：{result.AverageReturn:P2}");
            Console.WriteLine($"收益率中位数：{result.MedianReturn:P2}");
            Console.WriteLine($"收益率5%分位：{result.P5Return:P2}");
            Console.WriteLine($"收益率95%分位：{result.P95Return:P2}");
            Console.WriteLine("\n本次模拟完成，请继续选择股票；输入 q 返回工具菜单。");
            return WorkerLoopResult.Continue;
        }

        /// <summary>
        /// 获取完整历史前复权窗口：缓存不足或未在当天统一刷新时重新拉取，排除当天日线。
        /// </summary>
        private async Task<double[]> GetClosePricesAsync(string stockCode, int count)
        {
            // 排除当天尚未完成的日线；不将周末、节假日误判为缺失交易日。
            var today = DateOnly.FromDateTime(DateTime.Today);
            var refreshSince = DateTime.Today.ToUniversalTime();
            var rows = await db.StockDailyKlines
                .AsNoTracking()
                .Where(k => k.StockCode == stockCode && k.AdjustType == 1 &&
                    k.DataSource == "EastMoney" && k.TradeDate < today)
                .OrderByDescending(k => k.TradeDate)
                .Take(count)
                .Select(k => new { k.TradeDate, k.ClosePrice, k.UpdatedAt })
                .ToListAsync();

            // 要求整个窗口由同一批次在今天刷新，保证复权口径一致。
            if (rows.Count == count && rows.All(k => k.UpdatedAt >= refreshSince) &&
                rows.Select(k => k.UpdatedAt).Distinct().Count() == 1 &&
                rows.Select(k => k.TradeDate).Distinct().Count() == count)
            {
                rows.Reverse();
                Console.WriteLine($"数据库样本：{rows[0].TradeDate:yyyy-MM-dd} ～ {rows[^1].TradeDate:yyyy-MM-dd}，{count} 条，东方财富前复权。");
                return rows.Select(k => (double)k.ClosePrice).ToArray();
            }

            Console.WriteLine("历史窗口不足、未在今天完整刷新或复权批次不一致，重新从接口获取完整样本...");
            var result = await StockHistoryService.GetKlineDataAsync(stockCode, end: today.AddDays(-1));
            var bars = result.Bars.Where(b => b.TradeDate < today).OrderBy(b => b.TradeDate).TakeLast(count).ToArray();
            if (bars.Length < count || bars.Select(b => b.TradeDate).Distinct().Count() != count)
                throw new InvalidOperationException($"历史数据不足或日期重复：需要 {count} 条，实际获取 {bars.Length} 条");

            Console.WriteLine($"接口样本：{bars[0].TradeDate:yyyy-MM-dd} ～ {bars[^1].TradeDate:yyyy-MM-dd}，{count} 条，东方财富前复权（本次未写入数据库）。");
            return bars.Select(bar => (double)bar.ClosePrice).ToArray();
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

        private static double? ReadDouble(string prompt, double defaultValue, Func<double, bool> isValid)
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
                        CultureInfo.InvariantCulture, out var value) && double.IsFinite(value) && isValid(value))
                    return value;

                Console.WriteLine("请输入范围内的有限数字，使用小数点分隔小数。");
            }
        }

        private static int? ReadInt(string prompt, int defaultValue, int minValue, int maxValue)
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
                        CultureInfo.InvariantCulture, out var value) && value >= minValue && value <= maxValue)
                    return value;

                Console.WriteLine("请输入范围内的有效整数。");
            }
        }
    }
}
