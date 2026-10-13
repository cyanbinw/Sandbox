using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Domain.Calculations
{
    public class StockMonteCarloSimulator
    {
        public const int MaxHoldingDays = 2520;
        public const int MaxSimulations = 1_000_000;
        public const long MaxPathSteps = 100_000_000;
        public enum SamplingAlgorithm
        {
            IndependentDaily,
            MovingBlockBootstrap
        }
        /// <summary>DropFraction 为当前持仓亏损触发比例（不含交易成本）；每次投入按初始本金的比例计算，两个上限为 null 时均表示无限。</summary>
        public sealed record AveragingDownOptions(
            double DropFraction = 0.05,
            double InvestmentFraction = 1,
            int? MaxAdditions = null,
            double? MaxInvestedMultiple = null);

        public sealed record SimulationResult(
            double ProfitProbability,
            double TargetProbability,
            double StopLossProbability,
            double AverageReturn,
            double MedianReturn,
            double P5Return,
            double P95Return)
        {
            public double AverageAdditions { get; init; }
            public int MaxAdditionsObserved { get; init; }
            public double AverageInvestedMultiple { get; init; } = 1;
        }

        public SimulationResult Simulate(
                double[] closePrices,
                double buyPrice,
                int holdingDays = 30,
                int simulations = 100_000,
                double targetReturn = 0.05,
                double stopLossReturn = -0.05,
                double roundTripCostRate = 0.001,
                bool executeStopLoss = false,
                int seed = 42,
                AveragingDownOptions? averagingDown = null,
                SamplingAlgorithm algorithm = SamplingAlgorithm.IndependentDaily,
                int blockLength = 5)
        {
            ArgumentNullException.ThrowIfNull(closePrices);
            if (!Enum.IsDefined(algorithm))
                throw new ArgumentException("未知的模拟算法。", nameof(algorithm));
            if (algorithm == SamplingAlgorithm.MovingBlockBootstrap &&
                (blockLength <= 0 || blockLength >= closePrices.Length))
                throw new ArgumentException("区块长度必须为正整数且不能超过历史收益率数量（收盘价条数减一）。", nameof(blockLength));
            if (closePrices.Length < 2 ||
                closePrices.Any(p => !double.IsFinite(p) || p <= 0) ||
                !double.IsFinite(buyPrice) || buyPrice <= 0 ||
                holdingDays <= 0 || holdingDays > MaxHoldingDays ||
                simulations <= 0 || simulations > MaxSimulations ||
                (long)holdingDays * simulations > MaxPathSteps ||
                !double.IsFinite(targetReturn) || targetReturn < 0 ||
                !double.IsFinite(stopLossReturn) || Math.Abs(stopLossReturn) >= 1 ||
                !double.IsFinite(roundTripCostRate) || roundTripCostRate < 0 || roundTripCostRate >= 1)
                throw new ArgumentException("模拟参数无效");

            if (averagingDown is { } options &&
                (!double.IsFinite(options.DropFraction) || options.DropFraction <= 0 || options.DropFraction >= 1 ||
                 !double.IsFinite(options.InvestmentFraction) || options.InvestmentFraction <= 0 ||
                 options.MaxAdditions < 0 ||
                 (options.MaxInvestedMultiple is { } cap && (!double.IsFinite(cap) || cap < 1))))
                throw new ArgumentException("补仓参数无效", nameof(averagingDown));

            // 接受正负两种写法，均表示相同的亏损止损幅度。
            stopLossReturn = -Math.Abs(stopLossReturn);

            // 1. 计算历史每日收益率
            var returns = closePrices
                .Skip(1)
                .Select((price, i) =>
                    price / closePrices[i] - 1)
                .ToArray();

            if (returns.Any(r => !double.IsFinite(r) || r <= -1))
                throw new ArgumentException("历史价格产生了无效收益率。", nameof(closePrices));

            var random = new Random(seed);
            var finalReturns = new double[simulations];

            int profitCount = 0;
            int targetCount = 0;
            int stopLossCount = 0;
            long totalAdditions = 0;
            int maxAdditionsObserved = 0;
            double averageInvestedMultiple = 0;

            // 2. 开始蒙特卡罗模拟
            for (int i = 0; i < simulations; i++)
            {
                // 使用净值模拟；买入价只决定价格尺度，不影响收益率分布。
                double equity = 1;
                double exitEquity = 0;
                bool touchedStop = false;
                double invested = 1;
                double positionValue = 1;
                int additions = 0;
                int blockStart = 0;

                for (int day = 0; day < holdingDays; day++)
                {
                    // 止损后仍生成余下的假想路径，让两种模式在相同种子下使用相同样本。
                    // 实际止损收益只使用首次触发时记录的 exitEquity。
                    double dailyReturn;
                    if (algorithm == SamplingAlgorithm.MovingBlockBootstrap)
                    {
                        // 从所有重叠的完整区块中等概率、有放回抽样，不跨历史末尾循环。
                        // 每条路径重新开始区块；最后一个区块只使用剩余所需天数。
                        int offset = day % blockLength;
                        if (offset == 0)
                            blockStart = random.Next(returns.Length - blockLength + 1);
                        dailyReturn = returns[blockStart + offset];
                    }
                    else
                    {
                        // 保留原算法的随机调用顺序和结果。
                        dailyReturn = returns[random.Next(returns.Length)];
                    }

                    equity *= 1 + dailyReturn;
                    if (!double.IsFinite(equity) || equity <= 0)
                        throw new InvalidOperationException("模拟路径发生数值溢出，请检查历史价格或缩短持有期。");

                    if (executeStopLoss && touchedStop)
                        continue;

                    positionValue *= 1 + dailyReturn;
                    if (!double.IsFinite(positionValue) || positionValue <= 0)
                        throw new InvalidOperationException("持仓市值发生数值溢出，请调整模拟参数。");

                    // 用累计投入计算持仓收益；同日先判断止损，再决定是否补仓。
                    double positionReturn = positionValue / invested - 1;
                    if (positionReturn <= stopLossReturn && !touchedStop)
                    {
                        touchedStop = true;
                        // 按触发日模拟收盘价退出，不假设能按止损阈值成交。
                        exitEquity = positionValue / invested;
                    }
                    if (executeStopLoss && touchedStop)
                        continue;

                    // 当前持仓亏损达到阈值时补仓，每个收盘最多一次，无需重新穿越阈值。
                    if (averagingDown is { } strategy &&
                        (!strategy.MaxAdditions.HasValue || additions < strategy.MaxAdditions.Value) &&
                        (!strategy.MaxInvestedMultiple.HasValue || invested < strategy.MaxInvestedMultiple.Value) &&
                        positionReturn <= -strategy.DropFraction)
                    {
                        double amount = strategy.MaxInvestedMultiple is { } limit
                            ? Math.Min(strategy.InvestmentFraction, limit - invested)
                            : strategy.InvestmentFraction;
                        invested += amount;
                        positionValue += amount;
                        if (!double.IsFinite(invested) || !double.IsFinite(positionValue))
                            throw new InvalidOperationException("补仓金额发生数值溢出，请减少单次投入或补仓次数。");
                        additions++;
                    }
                }

                // 3. 计算扣除交易成本后的最终收益
                double netReturn =
                    (executeStopLoss && touchedStop ? exitEquity : positionValue / invested) - 1 - roundTripCostRate;

                totalAdditions += additions;
                maxAdditionsObserved = Math.Max(maxAdditionsObserved, additions);
                averageInvestedMultiple += invested / simulations;

                finalReturns[i] = netReturn;

                if (netReturn > 0)
                    profitCount++;

                if (netReturn >= targetReturn)
                    targetCount++;

                if (touchedStop)
                    stopLossCount++;
            }

            Array.Sort(finalReturns);

            return new SimulationResult(
                ProfitProbability: (double)profitCount / simulations,
                TargetProbability: (double)targetCount / simulations,
                StopLossProbability: (double)stopLossCount / simulations,
                AverageReturn: finalReturns.Sum(value => value / simulations),
                MedianReturn: Percentile(finalReturns, 0.50),
                P5Return: Percentile(finalReturns, 0.05),
                P95Return: Percentile(finalReturns, 0.95)
            )
            {
                AverageAdditions = (double)totalAdditions / simulations,
                MaxAdditionsObserved = maxAdditionsObserved,
                AverageInvestedMultiple = averageInvestedMultiple
            };
        }

        private static double Percentile(double[] sorted, double p)
        {
            var position = p * (sorted.Length - 1);
            var lower = (int)Math.Floor(position);
            var upper = (int)Math.Ceiling(position);
            var weight = position - lower;
            return sorted[lower] * (1 - weight) + sorted[upper] * weight;
        }
    }
}
