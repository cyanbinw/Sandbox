using System.Text.Json;
using Sandbox.Console.Lean;

void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
var options = new LeanScenarioOptions { SampleCount = 1, CalendarDays = 4 };
options.Validate();
try { (options with { TotalBudget = 10000 }).Validate(); throw new Exception("Accepted insufficient cash"); }
catch (ArgumentException) { }
var start = new DateTime(2025, 1, 6);
List<DailyBar> bars =
[
    new(start, 11.9m, 11.9m, 11.9m, 11.9m, 100000),
    new(start.AddDays(1), 11.8m, 11.9m, 11.5m, 11.6m, 100000),
    new(start.AddDays(2), 11.2m, 11.4m, 11.0m, 11.1m, 100000),
    new(start.AddDays(3), 10.5m, 10.6m, 10.3m, 10.4m, 100000),
    new(start.AddDays(4), 11m, 11.2m, 10.9m, 11m, 100000)
];
Check(LeanHistory.Windows(bars, 4, 20).Count == 1, "Incomplete windows were accepted");
Check(LeanHistory.Rebase(bars, 23.8m).Last().Close == 22m, "Rebase failed");
if (args.Length == 0) { Console.WriteLine("Lean input checks passed. Pass --engine for real engine smoke test."); return; }
var lean = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../../Lean"));
var sandbox = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../.."));
if (args.Contains("--history"))
{
    options = options with { CalendarDays = 30, SampleCount = 2 };
    bars = LeanHistory.ReadZip(Path.Combine(lean, "Data/equity/china/daily/300083.zip"));
}
var results = await new LeanRunner(lean, Path.Combine(lean, "Launcher/bin/Debug/QuantConnect.Lean.Launcher.dll"),
    Path.Combine(sandbox, "Sandbox.Console/Lean/Strategy/bin/Debug/net10.0/Sandbox.LeanStrategy.dll"),
    Path.Combine(sandbox, "LeanResults/checks")).RunAsync(options, bars, CancellationToken.None);
var pairs = JsonSerializer.Deserialize<List<ScenarioPair>>(File.ReadAllText(Path.Combine(results, "paths.json")))!;
if (args.Contains("--history"))
{
    Check(pairs.Count == 2 && pairs.All(p => p.Hold.Shares == 800 && p.Add.Cash >= 0), "Historical smoke failed");
    Console.WriteLine("Historical data / real Lean checks passed.");
    return;
}
var pair = pairs.Single();
Check(pair.Hold.Shares == 800 && pair.Hold.Pnl == -720m, "Hold accounting incorrect");
Check(pair.Add.FilledAdds == 2 && pair.Add.Shares == 2500, "Expected exactly two filled adds (800 + 900 shares)");
Check(pair.Add.Cash >= 0 && pair.Add.Fees == 10m, "Cash or fee accounting incorrect");
Check(pair.Add.Pnl == -440m, $"Expected gap fills 800 @ 11.20 and 900 @ 10.50; got {pair.Add.Pnl}");
Console.WriteLine("Real Lean integration checks passed.");

// Exercise the actual menu/DI/default paths, using local data without a database connection.
var info = new System.Diagnostics.ProcessStartInfo("dotnet")
{
    WorkingDirectory = sandbox, UseShellExecute = false, CreateNoWindow = true,
    RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
};
info.ArgumentList.Add(Path.Combine(sandbox, "Sandbox.Console/bin/Debug/net10.0/Sandbox.Console.dll"));
using var console = System.Diagnostics.Process.Start(info)!;
var outputTask = console.StandardOutput.ReadToEndAsync();
var errorTask = console.StandardError.ReadToEndAsync();
foreach (var line in new[] { "6", "300083", "11.9", "11.9", "800", "30000", "10000", "5", "10", "30", "1", "2", "q" })
    await console.StandardInput.WriteLineAsync(line);
console.StandardInput.Close();
using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
try { await console.WaitForExitAsync(timeout.Token); }
catch { if (!console.HasExited) console.Kill(true); throw; }
var output = await outputTask;
var error = await errorTask;
Check(console.ExitCode == 0 && output.Contains("完成 1 段历史情景") && !output.Contains("模拟未完成"),
    "Console menu smoke failed: " + output + error);
Console.WriteLine("Sandbox.Console menu / default paths / local data checks passed.");
