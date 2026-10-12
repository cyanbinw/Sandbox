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
        public sealed record SimulationResult(
            double ProfitProbability,
            double TargetProbability,
            double StopLossProbability,
            double AverageReturn,
            double MedianReturn,
            double P5Return,
            double P95Return);

        public SimulationResult Simulate(
                double[] closePrices,
                double buyPrice,
                int holdingDays = 30,
                int simulations = 100_000,
                double targetReturn = 0.05,
                double stopLossReturn = -0.05,
                double roundTripCostRate = 0.001,
                bool executeStopLoss = false,
                int seed = 42)
        {
            ArgumentNullException.ThrowIfNull(closePrices);
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

            // 2. 开始蒙特卡罗模拟
            for (int i = 0; i < simulations; i++)
            {
                // 使用净值模拟；买入价只决定价格尺度，不影响收益率分布。
                double equity = 1;
                double exitEquity = 0;
                bool touchedStop = false;

                for (int day = 0; day < holdingDays; day++)
                {
                    // 止损后仍生成余下的假想路径，让两种模式在相同种子下使用相同样本。
                    // 实际止损收益只使用首次触发时记录的 exitEquity。
                    // 从历史收益率中随机抽一天
                    double dailyReturn =
                        returns[random.Next(returns.Length)];

                    equity *= 1 + dailyReturn;
                    if (!double.IsFinite(equity) || equity <= 0)
                        throw new InvalidOperationException("模拟路径发生数值溢出，请检查历史价格或缩短持有期。");

                    // 检查是否触及止损价格
                    if (equity - 1 <= stopLossReturn && !touchedStop)
                    {
                        touchedStop = true;
                        // 按触发日模拟收盘价退出，不假设能按止损阈值成交。
                        exitEquity = equity;
                    }
                }

                // 3. 计算扣除交易成本后的最终收益
                double netReturn =
                    (executeStopLoss && touchedStop ? exitEquity : equity) - 1 - roundTripCostRate;

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
            );
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
