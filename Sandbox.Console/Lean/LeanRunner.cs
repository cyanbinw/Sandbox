using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sandbox.Console.Lean;

public sealed record ScenarioResult(decimal Equity, decimal Pnl, decimal Cash, decimal Shares,
    decimal AveragePrice, decimal EndPrice, decimal Fees, int FilledAdds, decimal MaxCloseDrawdown, int Bars);
public sealed record ScenarioPair(DateTime HistoricalStart, DateTime HistoricalEnd, ScenarioResult Hold, ScenarioResult Add);
public sealed record ScenarioSummary(decimal PositiveSamplePercent, decimal MedianPnl, decimal P05Pnl,
    decimal P95Pnl, decimal WorstPnl, decimal WorstCloseDrawdown);

public sealed class LeanRunner(string leanRoot, string launcher, string strategy, string resultsRoot)
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public async Task<string> RunAsync(LeanScenarioOptions options, List<DailyBar> history, CancellationToken token)
    {
        options.Validate();
        foreach (var path in new[] { launcher, strategy, Path.Combine(leanRoot, "Launcher", "config.json") })
            if (!File.Exists(path)) throw new FileNotFoundException("请先按 README 构建 Lean 和 Sandbox 策略。", path);
        var windows = LeanHistory.Windows(history, options.CalendarDays, options.SampleCount);
        var root = Path.GetFullPath(Path.Combine(resultsRoot, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]));
        Directory.CreateDirectory(root);
        await File.WriteAllTextAsync(Path.Combine(root, "request.json"), JsonSerializer.Serialize(new
        {
            Options = options, DataStart = history[0].Date, DataEnd = history[^1].Date,
            Source = "Local EastMoney adjusted daily bars; independently unverified",
            Method = "Complete overlapping historical calendar-day windows, evenly sampled and rebased to StartPrice; not future probabilities",
            Limitations = "Simplified daily limit fills; no liquidation; no full A-share exchange constraints; start price is user input, not live quote"
        }, Json), token);
        // Preserve the exact input used, so changes in the database cannot change this run's evidence.
        LeanHistory.WriteZip(Path.Combine(root, "source"), options.Ticker, history);
        var pairs = new List<ScenarioPair>();
        for (var i = 0; i < windows.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            var bars = windows[i];
            System.Console.WriteLine($"[{i + 1}/{windows.Count}] 历史起点 {bars[0].Date:yyyy-MM-dd}，运行不加仓 / 两档加仓...");
            var caseRoot = Path.Combine(root, $"{i + 1:D4}");
            var dataRoot = Path.Combine(caseRoot, "data");
            CopyMetadata(dataRoot, bars);
            LeanHistory.WriteZip(dataRoot, options.Ticker, LeanHistory.Rebase(bars, options.StartPrice));
            var hold = await ExecuteAsync(false);
            var add = await ExecuteAsync(true);
            pairs.Add(new ScenarioPair(bars[0].Date, bars[^1].Date, hold, add));
            await File.WriteAllTextAsync(Path.Combine(root, "paths.json"), JsonSerializer.Serialize(pairs, Json), token);

            async Task<ScenarioResult> ExecuteAsync(bool add)
            {
                var output = Path.Combine(caseRoot, add ? "add" : "hold");
                Directory.CreateDirectory(output);
                var template = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(leanRoot, "Launcher", "config.json"), token),
                    documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true })!.AsObject();
                template["environment"] = "backtesting";
                template["live-mode"] = false;
                template["algorithm-type-name"] = "ChinaDipBuyAlgorithm";
                template["algorithm-language"] = "CSharp";
                template["algorithm-location"] = Path.GetFullPath(strategy);
                template["data-folder"] = dataRoot;
                template["results-destination-folder"] = output;
                template["object-store-root"] = Path.Combine(output, "storage");
                template["close-automatically"] = true;
                template["algorithm-id"] = "sandbox";
                template["log-file"] = Path.Combine(output, "engine.log");
                template["parameters"] = JsonSerializer.SerializeToNode(new Dictionary<string, string>
                {
                    ["ticker"] = options.Ticker, ["cost"] = F(options.CostPrice), ["price"] = F(options.StartPrice),
                    ["shares"] = F(options.InitialShares), ["capital"] = F(options.TotalBudget),
                    ["add-budget"] = F(options.AddBudget), ["drop1"] = F(options.FirstDrop), ["drop2"] = F(options.SecondDrop),
                    ["start"] = bars[0].Date.ToString("yyyy-MM-dd"), ["end"] = bars[^1].Date.ToString("yyyy-MM-dd"),
                    ["expected-bars"] = F(bars.Count), ["add"] = add ? "true" : "false", ["output"] = Path.Combine(output, "scenario.json")
                });
                var config = Path.Combine(output, "config.json");
                await File.WriteAllTextAsync(config, template.ToJsonString(Json), token);
                var info = new ProcessStartInfo("dotnet")
                {
                    WorkingDirectory = Path.GetDirectoryName(Path.GetFullPath(launcher))!,
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                info.ArgumentList.Add(Path.GetFullPath(launcher));
                info.ArgumentList.Add("--config");
                info.ArgumentList.Add(config);
                using var process = Process.Start(info) ?? throw new InvalidOperationException("无法启动 Lean。");
                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromMinutes(5));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    if (!process.HasExited) process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(CancellationToken.None);
                    throw;
                }
                finally
                {
                    await File.WriteAllTextAsync(Path.Combine(output, "stdout.log"), await stdout, CancellationToken.None);
                    await File.WriteAllTextAsync(Path.Combine(output, "stderr.log"), await stderr, CancellationToken.None);
                }
                var resultPath = Path.Combine(output, "scenario.json");
                if (process.ExitCode != 0 || !File.Exists(resultPath) || !Directory.EnumerateFiles(output, "*-summary.json").Any())
                    throw new InvalidOperationException($"Lean 回测失败（退出码 {process.ExitCode}），请检查 {output}");
                var result = JsonSerializer.Deserialize<ScenarioResult>(await File.ReadAllTextAsync(resultPath, token))!;
                if (result.Bars != bars.Count || result.Cash < 0 || result.Shares % 100 != 0 || result.FilledAdds > 2)
                    throw new InvalidDataException($"回测结果违反数据完整性或资金约束：{output}");
                return result;
            }
        }
        var summary = new { Samples = pairs.Count, Hold = Summarize(pairs.Select(p => p.Hold)), Add = Summarize(pairs.Select(p => p.Add)) };
        await File.WriteAllTextAsync(Path.Combine(root, "summary.json"), JsonSerializer.Serialize(summary, Json), token);
        System.Console.WriteLine($"完成 {summary.Samples} 段历史情景（单位：元）：");
        System.Console.WriteLine("方案       盈利样本占比     盈亏中位数       5%分位        95%分位       最差盈亏");
        Print("不加仓", summary.Hold);
        Print("两档加仓", summary.Add);
        System.Console.WriteLine("以上为历史样本统计，不是未来盈利概率。盈亏含已有成本，期末未卖出。结果目录：" + root);
        return root;
    }

    private void CopyMetadata(string dataRoot, List<DailyBar> bars)
    {
        foreach (var name in new[] { "market-hours", "symbol-properties" })
        {
            var dest = Path.Combine(dataRoot, name);
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.EnumerateFiles(Path.Combine(leanRoot, "Data", name)))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
        }
        var properties = Path.Combine(dataRoot, "symbol-properties", "symbol-properties-database.csv");
        // Task-local market definition: quote currency and round lots, without modifying the engine's files.
        var lines = File.ReadAllLines(properties).Where(l => !l.StartsWith("china,,equity,"));
        File.WriteAllLines(properties, lines.Append("china,,equity,China A-share,CNY,1,0.01,100,,,1,1"));
        var hoursPath = Path.Combine(dataRoot, "market-hours", "market-hours-database.json");
        var hours = JsonNode.Parse(File.ReadAllText(hoursPath))!;
        var china = hours["entries"]?["Equity-china-[*]"] ?? throw new InvalidDataException("Lean 缺少中国市场时段配置。");
        // Treat absent weekdays as closed for this supplied historical path; this is not a future exchange calendar.
        var dates = bars.Select(b => b.Date.Date).ToHashSet();
        var closed = new JsonArray();
        for (var date = bars[0].Date; date <= bars[^1].Date; date = date.AddDays(1))
            if (!dates.Contains(date.Date) && date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
                closed.Add(date.ToString("MM/dd/yyyy", CultureInfo.InvariantCulture));
        china["holidays"] = closed;
        File.WriteAllText(hoursPath, hours.ToJsonString());
    }

    private static void Print(string label, ScenarioSummary summary) => System.Console.WriteLine(
        $"{label,-8} {summary.PositiveSamplePercent,9:F1}% {summary.MedianPnl,13:F2} {summary.P05Pnl,13:F2} {summary.P95Pnl,13:F2} {summary.WorstPnl,13:F2}");

    private static ScenarioSummary Summarize(IEnumerable<ScenarioResult> source)
    {
        var results = source.ToArray();
        var pnl = results.Select(r => r.Pnl).Order().ToArray();
        decimal Q(decimal p)
        {
            var index = (pnl.Length - 1) * p;
            var lo = (int)index;
            return decimal.Round(pnl[lo] + (pnl[Math.Min(lo + 1, pnl.Length - 1)] - pnl[lo]) * (index - lo), 2);
        }
        return new ScenarioSummary(100m * results.Count(r => r.Pnl > 0) / results.Length,
            Q(.5m), Q(.05m), Q(.95m), pnl[0], results.Max(r => r.MaxCloseDrawdown));
    }

    private static string F(decimal value) => value.ToString(CultureInfo.InvariantCulture);
}
