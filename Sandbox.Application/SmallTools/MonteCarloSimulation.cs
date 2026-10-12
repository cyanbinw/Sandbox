using Sandbox.Domain.Util;
using Sandbox.Infrastructure.ConsoleInfrastructure;

namespace Sandbox.Application.SmallTools
{
    public class MonteCarloSimulation : IWorker
    {
        public void Run()
        {
            // 演示数据：实际使用时换成真实的历史复权收盘价
            double[] closePrices = StockHistoryService.GetClosePricesAsync("300083", 250).Result;

            var result = Simulate(
                closePrices: closePrices,
                buyPrice: 10.6,
                holdingDays: 10,
                simulations: 100_000,
                targetReturn: 0.2,
                stopLossReturn: -0.1
            );

            Console.WriteLine($"盈利概率：{result.ProfitProbability:P2}");
            Console.WriteLine($"收益超5%概率：{result.TargetProbability:P2}");
            Console.WriteLine($"触及止损概率：{result.StopLossProbability:P2}");
            Console.WriteLine($"平均收益率：{result.AverageReturn:P2}");
            Console.WriteLine($"收益率中位数：{result.MedianReturn:P2}");
            Console.WriteLine($"收益率5%分位：{result.P5Return:P2}");
        }

        public async Task RunAsync()
        {
            Run();
            await Task.CompletedTask;
        }


        private sealed record SimulationResult(
            double ProfitProbability,
            double TargetProbability,
            double StopLossProbability,
            double AverageReturn,
            double MedianReturn,
            double P5Return,
            double P95Return);

        private static SimulationResult Simulate(
                double[] closePrices,
                double buyPrice,
                int holdingDays = 30,
                int simulations = 100_000,
                double targetReturn = 0.05,
                double stopLossReturn = -0.05,
                double roundTripCostRate = 0.001)
        {
            if (closePrices.Length < 2 ||
                closePrices.Any(p => p <= 0) ||
                buyPrice <= 0 ||
                holdingDays <= 0 ||
                simulations <= 0 ||
                roundTripCostRate < 0)
                throw new ArgumentException("模拟参数无效");

            // 1. 计算历史每日收益率
            var returns = closePrices
                .Skip(1)
                .Select((price, i) =>
                    price / closePrices[i] - 1)
                .ToArray();

            var random = new Random(42);
            var finalReturns = new double[simulations];

            int profitCount = 0;
            int targetCount = 0;
            int stopLossCount = 0;

            // 2. 开始蒙特卡罗模拟
            for (int i = 0; i < simulations; i++)
            {
                double price = buyPrice;
                bool touchedStop = false;

                for (int day = 0; day < holdingDays; day++)
                {
                    // 从历史收益率中随机抽一天
                    double dailyReturn =
                        returns[random.Next(returns.Length)];

                    price *= 1 + dailyReturn;

                    // 检查是否触及止损价格
                    if (price / buyPrice - 1 <= stopLossReturn)
                        touchedStop = true;
                }

                // 3. 计算扣除交易成本后的最终收益
                double netReturn =
                    price / buyPrice - 1 - roundTripCostRate;

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
                AverageReturn: finalReturns.Average(),
                MedianReturn: Percentile(finalReturns, 0.50),
                P5Return: Percentile(finalReturns, 0.05),
                P95Return: Percentile(finalReturns, 0.95)
            );
        }

        private static double Percentile(double[] sorted, double p)
        {
            int index = (int)Math.Floor(
                p * (sorted.Length - 1));

            return sorted[index];
        }
    }
}
