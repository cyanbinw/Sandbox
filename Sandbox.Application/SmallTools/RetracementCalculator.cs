using System.Globalization;
using Sandbox.Infrastructure.ConsoleInfrastructure;

namespace Sandbox.Application.SmallTools
{
    public class RetracementCalculator : IWorker
    {
        public void Run()
        {
            Console.WriteLine("上涨行情回撤计算：P = H - (H - L) × R");
            Console.WriteLine("比例按百分数输入，例如 38.2 或 38.2%；超过 100% 表示跌破起点。输入 q 退出。");

            var low = ReadNumber("请输入上涨起点（最低价 L）: ");
            if (!low.HasValue)
                return;

            decimal? high;
            while (true)
            {
                high = ReadNumber("请输入这一波上涨的最高价 H: ");
                if (!high.HasValue)
                    return;
                if (high.Value > low.Value)
                    break;
                Console.WriteLine("最高价必须大于起点价格，请重新输入。");
            }

            decimal[] ratios = [0.236m, 0.382m, 0.5m, 0.618m];

            var rows = new List<(string Ratio, string Price, string Position)>();

            foreach (var ratio in ratios)
            {
                try
                {
                    var price = Calculate(high.Value, low.Value, ratio);
                    var position = ratio > 1 ? "跌破起点" : ratio == 1 ? "回到起点" : "回踩价位";
                    rows.Add(($"{ratio * 100m:0.####}%", $"{price:G29}", position));
                }
                catch (OverflowException)
                {
                    Console.WriteLine("输入数值过大，无法计算，请使用较小的价格或比例。");
                    return;
                }
            }

            // 中文表头每个字占两个显示列，数字列按实际内容宽度补齐空格。
            var ratioWidth = Math.Max(8, rows.Max(row => row.Ratio.Length)) + 4;
            var priceWidth = Math.Max(8, rows.Max(row => row.Price.Length)) + 4;
            Console.WriteLine($"\n回撤比例{new string(' ', ratioWidth - 8)}预计价格{new string(' ', priceWidth - 8)}位置");
            foreach (var row in rows)
                Console.WriteLine($"{row.Ratio.PadRight(ratioWidth)}{row.Price.PadRight(priceWidth)}{row.Position}");
        }

        public Task RunAsync()
        {
            Run();
            return Task.CompletedTask;
        }

        /// <summary>
        /// 计算上涨行情的回撤价格：P = H - (H - L) × R。
        /// 比例使用小数，例如 0.382 表示 38.2%，大于 1 表示跌破起点。
        /// </summary>
        private static decimal Calculate(decimal high, decimal low, decimal ratio)
        {
            if (high <= low)
                throw new ArgumentOutOfRangeException(nameof(high), "最高价必须大于起点价格。");
            if (ratio < 0)
                throw new ArgumentOutOfRangeException(nameof(ratio), "回撤比例不能为负数。");

            return high - (high - low) * ratio;
        }

        private static decimal? ReadNumber(string prompt)
        {
            while (true)
            {
                Console.Write(prompt);
                var input = Console.ReadLine()?.Trim();
                if (input == null || string.Equals(input, "q", StringComparison.OrdinalIgnoreCase))
                    return null;
                if (TryParseNumber(input, out var value))
                    return value;
                Console.WriteLine("请输入有效数字，使用小数点分隔小数。");
            }
        }

        private static bool TryParseNumber(string? input, out decimal value) =>
            decimal.TryParse(input, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out value);
    }
}
