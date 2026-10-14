using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Console.Lean;

public sealed class LeanSimulationWorker(IConfiguration configuration, IServiceProvider services) : IWorker
{
    public void Run() => RunAsync().GetAwaiter().GetResult();

    public async Task RunAsync()
    {
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        System.Console.CancelKeyPress += handler;
        try
        {
            System.Console.WriteLine("Lean 历史情景模拟：30 天指自然日；q 返回；Ctrl+C 取消任务。股价走势是假设，非预测。");
            var options = new LeanScenarioOptions
            {
                Ticker = Read("股票代码", "300083"),
                CostPrice = Number("持仓成本价（已含初始费用）", 11.90m),
                StartPrice = Number("模拟起始股价（与成本分开，不会自动获取实时报价）", 11.90m),
                InitialShares = Integer("实际持股数", 800),
                TotalBudget = Number("总预算（已投入成本 + 剩余现金）", 30000),
                AddBudget = Number("每档加仓预算（含费用）", 10000),
                FirstDrop = Number("第一档相对成本跌幅 %", 5) / 100,
                SecondDrop = Number("第二档相对成本跌幅 %", 10) / 100,
                CalendarDays = Integer("未来模拟自然日数", 30),
                SampleCount = Integer("历史窗口数（每个窗口运行两次 Lean）", 20)
            };
            options.Validate();
            var root = Path.GetFullPath(configuration["Lean:Root"] ?? Path.Combine(AppContext.BaseDirectory, "../../../../../../Lean"));
            var launcher = Path.GetFullPath(configuration["Lean:Launcher"] ?? Path.Combine(root, "Launcher/bin/Debug/QuantConnect.Lean.Launcher.dll"));
            var strategy = Path.GetFullPath(configuration["Lean:Strategy"] ?? Path.Combine(AppContext.BaseDirectory, "../../../Lean/Strategy/bin/Debug/net10.0/Sandbox.LeanStrategy.dll"));
            var results = Path.GetFullPath(configuration["Lean:Results"] ?? Path.Combine(AppContext.BaseDirectory, "LeanResults"));
            var source = Read("数据来源：1 数据库；2 已有 Lean 日线", "1");
            var history = source switch
            {
                "1" => await LeanHistory.FromDatabaseAsync(services.GetRequiredService<QuantResearchDbContext>(), options.Ticker, cancellation.Token),
                "2" => LeanHistory.ReadZip(Path.Combine(root, "Data/equity/china/daily", options.Ticker + ".zip")),
                _ => throw new ArgumentException("数据来源应为 1 或 2。")
            };
            await new LeanRunner(root, launcher, strategy, results).RunAsync(options, history, cancellation.Token);
        }
        catch (OperationCanceledException) { System.Console.WriteLine("已取消；已完成的任务数据保留。"); }
        catch (Exception ex) { System.Console.WriteLine("Lean 模拟未完成：" + ex.Message); }
        finally { System.Console.CancelKeyPress -= handler; }
    }

    private static string Read(string label, string value)
    {
        System.Console.Write($"{label} [{value}]: ");
        var line = System.Console.ReadLine()?.Trim();
        if (line == null || line.Equals("q", StringComparison.OrdinalIgnoreCase)) throw new OperationCanceledException();
        return line.Length == 0 ? value : line;
    }
    private static decimal Number(string label, decimal value) =>
        decimal.Parse(Read(label, value.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
    private static int Integer(string label, int value) =>
        int.Parse(Read(label, value.ToString(CultureInfo.InvariantCulture)), CultureInfo.InvariantCulture);
}
