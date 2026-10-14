using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Sandbox.Domain.Calculations;
using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.SmallTools
{
    /// <summary>共用选股、行情读取、模拟执行及结果展示流程。</summary>
    public abstract class MonteCarloSimulationBase : LoopWorkerBase
    {
        private const int DefaultHistoryCount = 500;
        protected abstract bool UsePreset { get; }

        private readonly QuantResearchDbContext db;
        private readonly StockMonteCarloSimulator simulator;

        protected MonteCarloSimulationBase(QuantResearchDbContext db, StockMonteCarloSimulator simulator)
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

            if (UsePreset)
                Console.WriteLine("默认参数：历史 500 条，持有 30 天，模拟 100000 次，目标收益 20%，亏损 10% 按收盘价止损；初始投入 5500，亏损 5% 补仓 5500，最多 6 次，累计投入上限 30000；移动区块 5 天，随机种子 42。");

            int? historyCount;
            do
            {
                historyCount = UsePreset ? 500 : ReadInt("请输入历史样本条数（至少 2 条，0 使用全部可用历史）", DefaultHistoryCount, 0, int.MaxValue);
                if (historyCount == null)
                    return WorkerLoopResult.Exit;
                if (historyCount == 1)
                    Console.WriteLine("至少需要 2 条收盘价才能计算日收益率。");
            } while (historyCount == 1);

            Console.WriteLine("模型假设按输入买入价开始持仓；改变买入价不改变收益率概率。");
            var buyPrice = ReadDouble("请输入买入价", 10.6, value => value > 0);
            if (buyPrice == null)
                return WorkerLoopResult.Exit;

            int? holdingDays = UsePreset ? 30 : ReadInt("请输入持有交易日数（1～2520）", 10, 1, StockMonteCarloSimulator.MaxHoldingDays);
            if (holdingDays == null)
                return WorkerLoopResult.Exit;

            var maxSimulations = (int)Math.Min(StockMonteCarloSimulator.MaxSimulations,
                StockMonteCarloSimulator.MaxPathSteps / holdingDays.Value);
            int? simulations = UsePreset ? 100_000 : ReadInt($"请输入模拟次数（1～{maxSimulations}）", Math.Min(100_000, maxSimulations), 1, maxSimulations);
            if (simulations == null)
                return WorkerLoopResult.Exit;

            double? targetReturn = UsePreset ? 20 : ReadDouble("请输入目标收益率(%，非负）", 20, value => value >= 0);
            if (targetReturn == null)
                return WorkerLoopResult.Exit;

            double? stopLossReturn = UsePreset ? 10 : ReadDouble("请输入止损幅度(%，如 10 或 -10 均表示亏损 10%，绝对值须小于 100）", 10, value => Math.Abs(value) < 100);
            if (stopLossReturn == null)
                return WorkerLoopResult.Exit;
            stopLossReturn = -Math.Abs(stopLossReturn.Value);
            Console.WriteLine($"止损收益率：{stopLossReturn.Value:0.####}%，每日收盘收益率达到或低于该值时触发。");

            int? mode = UsePreset ? 2 : ReadInt("模式：1 持有到期，2 按触发日收盘价止损", 1, 1, 2);
            if (mode == null)
                return WorkerLoopResult.Exit;

            int? enableAveraging = UsePreset ? 1 : ReadInt("是否启用补仓：0 否，1 是", 0, 0, 1);
            if (enableAveraging == null)
                return WorkerLoopResult.Exit;
            StockMonteCarloSimulator.AveragingDownOptions? averagingDown = null;
            double initialInvestment = 1;
            if (enableAveraging == 1)
            {
                double? initial = UsePreset ? 5500 : ReadDouble("请输入初始投入金额", 10000, value => value > 0);
                if (initial == null)
                    return WorkerLoopResult.Exit;
                initialInvestment = initial.Value;
                double? drop = UsePreset ? 5 : ReadDouble("当前持仓亏损达到多少百分比时补仓（如 5 或 10，大于 0，小于 100）", 5, value => value > 0 && value < 100);
                if (drop == null)
                    return WorkerLoopResult.Exit;
                double? amount = UsePreset ? 5500 : ReadDouble("每次补仓金额", initialInvestment,
                    value => value > 0 && double.IsFinite(value / initialInvestment) && value / initialInvestment > 0);
                if (amount == null)
                    return WorkerLoopResult.Exit;
                int? maxAdditions = UsePreset ? 6 : ReadInt("补仓次数上限（0 不补仓，-1 无限）", 3, -1, int.MaxValue);
                if (maxAdditions == null)
                    return WorkerLoopResult.Exit;
                double? maxInvestment = UsePreset ? 30000 : ReadDouble("累计投入金额上限（含初始投入，-1 无限）", -1,
                    value => value == -1 || (value >= initialInvestment && double.IsFinite(value / initialInvestment)));
                if (maxInvestment == null)
                    return WorkerLoopResult.Exit;
                averagingDown = new(
                    DropFraction: drop.Value / 100,
                    InvestmentFraction: amount.Value / initialInvestment,
                    MaxAdditions: maxAdditions == -1 ? null : maxAdditions.Value,
                    MaxInvestedMultiple: maxInvestment == -1 ? null : maxInvestment.Value / initialInvestment);
                Console.WriteLine($"补仓：当前持仓亏损达到 {drop.Value:0.####}% 时投入 {amount.Value:0.##}；次数上限：{(maxAdditions == -1 ? "无限" : maxAdditions.Value.ToString())}；累计投入上限：{(maxInvestment == -1 ? "无限" : maxInvestment.Value.ToString("0.##"))}。");
                Console.WriteLine("触发盈亏 =（持仓市值 - 累计投入）/ 累计投入，不含交易成本；补仓后更新成本，后续交易日仍满足亏损条件则继续补仓。");
                Console.WriteLine("每日收盘最多补仓一次，剩余额度不足时按余额补入；执行止损优先于补仓。允许零股，不计最小交易单位。");
            }
            int? seed = UsePreset ? 42 : ReadInt("请输入随机种子（相同参数可复现）", 42, int.MinValue, int.MaxValue);
            if (seed == null)
                return WorkerLoopResult.Exit;

            Console.WriteLine("止损按补仓前的每日收盘持仓收益率判断，执行模式按触发日收盘价成交；收益率以累计投入为分母，往返成本为累计投入的 0.1%。");
            double[] closePrices = await GetClosePricesAsync(stockCode, historyCount.Value);

            int? algorithmChoice = UsePreset ? 1 : ReadInt("路径算法：1 Moving Block Bootstrap（移动区块重采样），2 原有独立日收益率重采样", 1, 1, 2);
            if (algorithmChoice == null)
                return WorkerLoopResult.Exit;
            var algorithm = algorithmChoice == 1
                ? StockMonteCarloSimulator.SamplingAlgorithm.MovingBlockBootstrap
                : StockMonteCarloSimulator.SamplingAlgorithm.IndependentDaily;
            int blockLength = 5;
            if (!UsePreset && algorithm == StockMonteCarloSimulator.SamplingAlgorithm.MovingBlockBootstrap)
            {
                if (closePrices.Length <= 5)
                    Console.WriteLine("历史收益率不足默认的 5 个交易日，请明确输入不超过现有收益率数量的区块长度。");
                while (true)
                {
                    var length = ReadInt($"请输入区块交易日数（1～{closePrices.Length - 1}）", 5, 1, closePrices.Length - 1);
                    if (length == null)
                        return WorkerLoopResult.Exit;
                    if (length < closePrices.Length)
                    {
                        blockLength = length.Value;
                        break;
                    }
                    Console.WriteLine("默认区块长度超过可用历史，请输入有效长度。");
                }
            }
            Console.WriteLine(algorithmChoice == 1
                ? $"路径算法：Moving Block Bootstrap，区块长度：{blockLength} 个交易日。"
                : "路径算法：原有独立日收益率重采样。");

            var result = simulator.Simulate(
                closePrices: closePrices,
                buyPrice: buyPrice.Value,
                holdingDays: holdingDays.Value,
                simulations: simulations.Value,
                targetReturn: targetReturn.Value / 100,
                stopLossReturn: stopLossReturn.Value / 100,
                executeStopLoss: mode == 2,
                seed: seed.Value,
                averagingDown: averagingDown,
                algorithm: algorithm,
                blockLength: blockLength
            );

            Console.WriteLine($"盈利概率：{result.ProfitProbability:P2}");
            Console.WriteLine($"达到目标收益概率：{result.TargetProbability:P2}");
            Console.WriteLine($"每日收盘触及止损线概率：{result.StopLossProbability:P2}");
            Console.WriteLine($"统计模式：{(mode == 2 ? "执行止损" : "持有到期")}，随机种子：{seed.Value}");
            Console.WriteLine($"平均收益率：{result.AverageReturn:P2}");
            Console.WriteLine($"收益率中位数：{result.MedianReturn:P2}");
            Console.WriteLine($"收益率5%分位：{result.P5Return:P2}");
            Console.WriteLine($"收益率95%分位：{result.P95Return:P2}");
            if (averagingDown != null)
            {
                Console.WriteLine($"平均补仓次数：{result.AverageAdditions:0.##}，单路径最多补仓次数：{result.MaxAdditionsObserved}");
                Console.WriteLine($"平均累计投入：{result.AverageInvestedMultiple * initialInvestment:0.##}（初始本金的 {result.AverageInvestedMultiple:0.####} 倍）");
            }
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
            var query = db.StockDailyKlines
                .AsNoTracking()
                .Where(k => k.StockCode == stockCode && k.AdjustType == 1 &&
                    k.DataSource == "EastMoney" && k.TradeDate < today)
                .OrderByDescending(k => k.TradeDate)
                .AsQueryable();
            if (count > 0)
                query = query.Take(count);

            var rows = await query.Select(k => new { k.TradeDate, k.ClosePrice, k.UpdatedAt })
                .ToListAsync();

            // 要求整个窗口由同一批次在今天刷新，保证复权口径一致。
            if (rows.Count >= 2 && (count == 0 || rows.Count == count) &&
                rows.Select(k => k.TradeDate).Distinct().Count() == rows.Count)
            {
                rows.Reverse();
                Console.WriteLine($"数据库样本：{rows[0].TradeDate:yyyy-MM-dd} ～ {rows[^1].TradeDate:yyyy-MM-dd}，{rows.Count} 条，东方财富前复权{(count == 0 ? "（全部已入库历史）" : "")}。");
                return rows.Select(k => (double)k.ClosePrice).ToArray();
            }

            Console.WriteLine("历史窗口不足、未在今天完整刷新或复权批次不一致，重新从接口获取完整样本...");
            var result = await StockHistoryService.GetKlineDataAsync(stockCode, end: today.AddDays(-1));
            IEnumerable<StockKlineBar> selectedBars = result.Bars.Where(b => b.TradeDate < today).OrderBy(b => b.TradeDate);
            if (count > 0)
                selectedBars = selectedBars.TakeLast(count);
            var bars = selectedBars.ToArray();
            if (bars.Length < Math.Max(2, count) || bars.Select(b => b.TradeDate).Distinct().Count() != bars.Length)
                throw new InvalidOperationException($"历史数据不足或日期重复：至少需要 {Math.Max(2, count)} 条，实际获取 {bars.Length} 条");

            Console.WriteLine($"接口样本：{bars[0].TradeDate:yyyy-MM-dd} ～ {bars[^1].TradeDate:yyyy-MM-dd}，{bars.Length} 条，东方财富前复权（本次未写入数据库）。");
            if (count == 0)
                Console.WriteLine("全部历史使用当前接口返回的可用数据：默认从 2020-01-01 开始，单次请求上限 10000 条。");
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
