using Sandbox.Domain.Calculations;

var simulator = new StockMonteCarloSimulator();
var checks = 0;
void Near(double actual, double expected)
{
    if (!double.IsFinite(actual) || Math.Abs(actual - expected) > 1e-10)
        throw new Exception($"Expected {expected}, got {actual}");
    checks++;
}
void Reject(Action action)
{
    try { action(); }
    catch (ArgumentException) { checks++; return; }
    throw new Exception("Invalid input was accepted.");
}

// Single historical return makes these independent, analytically known paths.
var hold = simulator.Simulate([100, 80], 100, 2, 20, stopLossReturn: -0.1);
var stop = simulator.Simulate([100, 80], 100, 2, 20, stopLossReturn: -0.1, executeStopLoss: true);
Near(hold.AverageReturn, -0.361);
Near(stop.AverageReturn, -0.201); // Exit at actual -20% close, not -10% threshold.
Near(stop.StopLossProbability, 1);
Near(stop.ProfitProbability, 0);
Near(stop.P5Return, -0.201);
Near(stop.P95Return, -0.201);
var gain = simulator.Simulate([100, 110], 100, 2, 20, targetReturn: 0.2);
Near(gain.AverageReturn, 0.209);
Near(gain.TargetProbability, 1);
Near(gain.StopLossProbability, 0);
var flat = simulator.Simulate([100, 100], 100, 2, 20);
Near(flat.AverageReturn, -0.001);

double[] history = [100, 90, 110, 95, 120];
var first = simulator.Simulate(history, 10, 10, 1000, seed: 7);
if (first != simulator.Simulate(history, 10, 10, 1000, seed: 7))
    throw new Exception("Seed is not reproducible.");
checks++;
if (first != simulator.Simulate(history, 20, 10, 1000, seed: 7))
    throw new Exception("Price scale changed return distribution.");
checks++;
var stopped = simulator.Simulate(history, 10, 10, 1000, seed: 7, executeStopLoss: true);
Near(first.StopLossProbability, stopped.StopLossProbability);

Reject(() => simulator.Simulate(null!, 10));
Reject(() => simulator.Simulate([1], 10));
Reject(() => simulator.Simulate([1, double.NaN], 10));
Reject(() => simulator.Simulate([1, double.PositiveInfinity], 10));
Reject(() => simulator.Simulate([1, 2], double.NaN));
Reject(() => simulator.Simulate([1, 2], 10, targetReturn: double.NaN));
var zeroStop = simulator.Simulate([100, 100], 100, 2, 20, stopLossReturn: 0, executeStopLoss: true);
Near(zeroStop.StopLossProbability, 1);
Near(zeroStop.AverageReturn, -0.001);
var positiveStop = simulator.Simulate([100, 102], 100, 2, 20, stopLossReturn: 0.05, executeStopLoss: true);
Near(positiveStop.StopLossProbability, 0);
Near(positiveStop.AverageReturn, 0.0394);
var aboveStop = simulator.Simulate([100, 110], 100, 2, 20, stopLossReturn: 0.05, executeStopLoss: true);
Near(aboveStop.StopLossProbability, 0);
Near(aboveStop.AverageReturn, 0.209);
var unsignedStop = simulator.Simulate([100, 80], 100, 2, 20, stopLossReturn: 0.1, executeStopLoss: true);
if (unsignedStop != stop)
    throw new Exception("Positive and negative stop magnitudes must produce identical results.");
checks++;
Reject(() => simulator.Simulate([1, 2], 10, stopLossReturn: 1));
Reject(() => simulator.Simulate([1, 2], 10, stopLossReturn: double.NaN));
Reject(() => simulator.Simulate([1, 2], 10, stopLossReturn: double.PositiveInfinity));
Reject(() => simulator.Simulate([1, 2], 10, stopLossReturn: -1));
Reject(() => simulator.Simulate([1, 2], 10, roundTripCostRate: 1));
Reject(() => simulator.Simulate([1, 2], 10, holdingDays: 0));
Reject(() => simulator.Simulate([1, 2], 10, simulations: 0));
Reject(() => simulator.Simulate([1, 2], 10, holdingDays: 2520, simulations: 1_000_000));
Reject(() => simulator.Simulate([double.Epsilon, double.MaxValue], 10));
var unlimited = new StockMonteCarloSimulator.AveragingDownOptions(0.1, 1);
var added = simulator.Simulate([100, 80], 100, 3, 20, averagingDown: unlimited);
Near(added.AverageReturn, 2.952 / 4 - 1 - 0.001);
Near(added.AverageAdditions, 3);
Near(added.MaxAdditionsObserved, 3);
Near(added.AverageInvestedMultiple, 4);
var countLimited = simulator.Simulate([100, 80], 100, 3, 20,
    averagingDown: unlimited with { MaxAdditions = 1 });
Near(countLimited.AverageReturn, 1.152 / 2 - 1 - 0.001);
Near(countLimited.AverageAdditions, 1);
var amountLimited = simulator.Simulate([100, 80], 100, 3, 20,
    averagingDown: unlimited with { MaxInvestedMultiple = 2.5 });
Near(amountLimited.AverageReturn, 1.552 / 2.5 - 1 - 0.001);
Near(amountLimited.AverageAdditions, 2);
Near(amountLimited.AverageInvestedMultiple, 2.5);
var bothLimited = simulator.Simulate([100, 80], 100, 3, 20,
    averagingDown: unlimited with { MaxAdditions = 1, MaxInvestedMultiple = 2.5 });
Near(bothLimited.AverageInvestedMultiple, 2);
var noBudget = simulator.Simulate([100, 80], 100, 2, 20,
    averagingDown: unlimited with { MaxInvestedMultiple = 1 });
Near(noBudget.AverageReturn, hold.AverageReturn);
Near(noBudget.AverageAdditions, 0);
var zeroAdds = simulator.Simulate([100, 80], 100, 2, 20,
    averagingDown: unlimited with { MaxAdditions = 0 });
Near(zeroAdds.AverageReturn, hold.AverageReturn);
var stopBeforeAdd = simulator.Simulate([100, 80], 100, 3, 20, executeStopLoss: true,
    averagingDown: unlimited);
Near(stopBeforeAdd.AverageReturn, -0.201);
Near(stopBeforeAdd.AverageAdditions, 0);
var delayedStop = simulator.Simulate([100, 80], 100, 3, 20, stopLossReturn: -0.25,
    executeStopLoss: true, averagingDown: unlimited);
Near(delayedStop.AverageReturn, -0.281);
Near(delayedStop.AverageAdditions, 1);
Near(simulator.Simulate([100, 110], 100, 3, 20, averagingDown: unlimited).AverageAdditions, 0);
// Loss against total invested capital triggers again even though the decline since
// the last purchase is only 20%, below the 30% threshold.
var currentLoss = simulator.Simulate([100, 80], 100, 3, 20,
    averagingDown: unlimited with { DropFraction = 0.3 });
Near(currentLoss.AverageAdditions, 2);
Near(currentLoss.AverageInvestedMultiple, 3);
Near(currentLoss.AverageReturn, 2.312 / 3 - 1 - 0.001);
// A large addition dilutes the current loss: another 20% price decline alone
// does not trigger the 25% loss rule on the next day.
var dilutedLoss = simulator.Simulate([100, 80], 100, 3, 20,
    averagingDown: unlimited with { DropFraction = 0.25, InvestmentFraction = 10 });
Near(dilutedLoss.AverageAdditions, 1);
Near(dilutedLoss.AverageReturn, 8.512 / 11 - 1 - 0.001);
var withAdds = simulator.Simulate(history, 10, 10, 1000, seed: 7, averagingDown: unlimited);
if (withAdds != simulator.Simulate(history, 20, 10, 1000, seed: 7, averagingDown: unlimited))
    throw new Exception("Averaging strategy is not reproducible across price scales.");
checks++;
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { DropFraction = 0 }));
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { DropFraction = 1 }));
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { InvestmentFraction = double.NaN }));
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { MaxAdditions = -1 }));
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { MaxInvestedMultiple = 0.5 }));
Reject(() => simulator.Simulate([100, 80], 100, averagingDown: unlimited with { MaxInvestedMultiple = double.PositiveInfinity }));
var block = StockMonteCarloSimulator.SamplingAlgorithm.MovingBlockBootstrap;
// Length one must exactly reproduce the original independent sampler, including strategy results.
foreach (var execute in new[] { false, true })
{
    var original = simulator.Simulate(history, 10, 17, 1000, seed: 7,
        executeStopLoss: execute, averagingDown: unlimited);
    var unitBlock = simulator.Simulate(history, 10, 17, 1000, seed: 7,
        executeStopLoss: execute, averagingDown: unlimited, algorithm: block, blockLength: 1);
    if (original != unitBlock) throw new Exception("Unit blocks changed original strategy results.");
    checks++;
}
// One available block gives an exact ordered path; repeat it and truncate the final block.
double[] ordered = [100, 80, 120, 90, 110, 130];
var exactBlock = simulator.Simulate(ordered, 100, 7, 20, algorithm: block);
Near(exactBlock.AverageReturn, 1.3 * 1.2 - 1 - 0.001);
var blockStop = simulator.Simulate(ordered, 100, 7, 20, algorithm: block, executeStopLoss: true,
    averagingDown: unlimited);
Near(blockStop.AverageReturn, -0.201);
Near(blockStop.AverageAdditions, 0);
var blockAdd = simulator.Simulate(ordered, 100, 2, 20, algorithm: block, averagingDown: unlimited);
Near(blockAdd.AverageReturn, 1.8 * 1.5 / 2 - 1 - 0.001);
Near(blockAdd.AverageAdditions, 1);
// Independent reference using an explicit list of all overlapping blocks.
double[][] blocks = [[0.8, 1.5], [1.5, 0.75], [0.75, 110.0 / 90], [110.0 / 90, 130.0 / 110]];
var rng = new Random(19);
double expectedAverage = 0;
for (int path = 0; path < 100; path++)
{
    double value = 1;
    var days = 0;
    while (days < 7)
    {
        foreach (var factor in blocks[rng.Next(blocks.Length)])
        {
            value *= factor;
            if (++days == 7) break;
        }
    }
    expectedAverage += (value - 1 - 0.001) / 100;
}
var sampled = simulator.Simulate(ordered, 100, 7, 100, seed: 19, algorithm: block, blockLength: 2);
Near(sampled.AverageReturn, expectedAverage);
if (sampled != simulator.Simulate(ordered, 100, 7, 100, seed: 19, algorithm: block, blockLength: 2))
    throw new Exception("Block seed is not reproducible.");
checks++;
Near(sampled.StopLossProbability, simulator.Simulate(ordered, 100, 7, 100, seed: 19,
    algorithm: block, blockLength: 2, executeStopLoss: true).StopLossProbability);
Reject(() => simulator.Simulate(history, 100, algorithm: block, blockLength: 0));
Reject(() => simulator.Simulate(history, 100, algorithm: block, blockLength: -1));
Reject(() => simulator.Simulate(history, 100, algorithm: block));
Reject(() => simulator.Simulate(history, 100, algorithm: (StockMonteCarloSimulator.SamplingAlgorithm)99));
Console.WriteLine($"Passed {checks} simulation checks.");
