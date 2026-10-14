using System.Globalization;
using System.Text.Json;
using QuantConnect;
using QuantConnect.Algorithm;
using QuantConnect.Data;
using QuantConnect.Data.Market;
using QuantConnect.Orders;
using QuantConnect.Orders.Fees;
using QuantConnect.Orders.Fills;
using QuantConnect.Securities;

namespace Sandbox.LeanStrategy;

/// <summary>Executes one supplied historical scenario. Scenario generation belongs to Sandbox.Console.</summary>
public sealed class ChinaDipBuyAlgorithm : QCAlgorithm
{
    private Symbol _symbol = null!;
    private bool _submitted;
    private int _bars;
    private int _fills;
    private decimal _peak;
    private decimal _drawdown;
    private decimal _capital;

    public override void Initialize()
    {
        var start = DateTime.ParseExact(GetParameter("start"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        var end = DateTime.ParseExact(GetParameter("end"), "yyyy-MM-dd", CultureInfo.InvariantCulture);
        SetStartDate(start);
        SetEndDate(end);
        SetTimeZone(TimeZones.Shanghai);
        SetAccountCurrency("CNY");
        _capital = GetParameter("capital", 30000m);
        var cost = GetParameter("cost", 11.90m);
        var price = GetParameter("price", 11.90m);
        var quantity = GetParameter("shares", 800);
        SetCash(_capital - cost * quantity);
        var security = AddEquity(GetParameter("ticker", "300083"), Resolution.Daily, "china",
            fillForward: false, leverage: 1, dataNormalizationMode: DataNormalizationMode.Raw);
        _symbol = security.Symbol;
        security.SetFeeModel(new PurchaseFeeModel());
        security.SetFillModel(new ScenarioFillModel());
        security.SetMarketPrice(new TradeBar(start, _symbol, price, price, price, price, 0));
        security.Holdings.SetHoldings(cost, quantity);
        _peak = Portfolio.TotalPortfolioValue;
        SetBenchmark(_ => price);
    }

    public override void OnData(Slice data)
    {
        if (!data.Bars.ContainsKey(_symbol)) return;
        _bars++;
        if (!_submitted)
        {
            _submitted = true;
            if (GetParameter("add", "false") == "true")
            {
                var cost = GetParameter("cost", 11.90m);
                var budget = GetParameter("add-budget", 10000m);
                foreach (var drop in new[] { GetParameter("drop1", .05m), GetParameter("drop2", .10m) })
                {
                    var limit = Math.Floor(cost * (1 - drop) * 100) / 100;
                    var quantity = Math.Floor((budget - 5) / limit / 100) * 100;
                    while (quantity > 0 && quantity * limit + Math.Max(5, quantity * limit * .00025m) > budget)
                        quantity -= 100;
                    if (quantity > 0) LimitOrder(_symbol, quantity, limit, tag: $"加仓 {drop:P0}");
                }
            }
        }
        _peak = Math.Max(_peak, Portfolio.TotalPortfolioValue);
        _drawdown = Math.Max(_drawdown, (_peak - Portfolio.TotalPortfolioValue) / _peak);
    }

    public override void OnOrderEvent(OrderEvent orderEvent)
    {
        if (orderEvent.Status == OrderStatus.Invalid)
            throw new InvalidOperationException($"订单被拒绝：{orderEvent}");
        if (orderEvent.Status == OrderStatus.Filled) _fills++;
    }

    public override void OnEndOfAlgorithm()
    {
        if (_bars != GetParameter("expected-bars", 0))
            throw new InvalidOperationException($"行情未完整读取：{_bars} 条。");
        var result = new
        {
            Equity = Portfolio.TotalPortfolioValue,
            Pnl = Portfolio.TotalPortfolioValue - _capital,
            Cash = Portfolio.Cash,
            Shares = Portfolio[_symbol].Quantity,
            AveragePrice = Portfolio[_symbol].AveragePrice,
            EndPrice = Securities[_symbol].Price,
            Fees = Portfolio.TotalFees,
            FilledAdds = _fills,
            MaxCloseDrawdown = _drawdown,
            Bars = _bars
        };
        File.WriteAllText(GetParameter("output"), JsonSerializer.Serialize(result));
    }

    private sealed class PurchaseFeeModel : FeeModel
    {
        public override OrderFee GetOrderFee(OrderFeeParameters parameters) => new(new CashAmount(
            Math.Max(5m, Math.Abs(parameters.Order.Quantity) * parameters.Security.Price * .00025m), "CNY"));
    }

    private sealed class ScenarioFillModel : EquityFillModel
    {
        public override OrderEvent LimitFill(Security asset, LimitOrder order)
        {
            var bar = asset.GetLastData() as TradeBar;
            if (bar == null || bar.Volume <= 0 || bar.High == bar.Low)
                return new OrderEvent(order, asset.LocalTime.ConvertToUtc(asset.Exchange.TimeZone), OrderFee.Zero);
            return base.LimitFill(asset, order);
        }
    }
}
