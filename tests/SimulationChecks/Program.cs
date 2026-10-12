using Sandbox.Domain.Calculations;

var simulator = new StockMonteCarloSimulator();
var checks = 0;
void Near(double actual, double expected)
{
    if (Math.Abs(actual - expected) > 1e-10)
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
Console.WriteLine($"Passed {checks} simulation checks.");
