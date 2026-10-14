namespace Sandbox.Console.Lean;

public sealed record LeanScenarioOptions
{
    public string Ticker { get; init; } = "300083";
    public decimal CostPrice { get; init; } = 11.90m;
    public decimal StartPrice { get; init; } = 11.90m;
    public int InitialShares { get; init; } = 800;
    public decimal TotalBudget { get; init; } = 30000m;
    public decimal AddBudget { get; init; } = 10000m;
    public decimal FirstDrop { get; init; } = .05m;
    public decimal SecondDrop { get; init; } = .10m;
    public int CalendarDays { get; init; } = 30;
    public int SampleCount { get; init; } = 20;
    public decimal Cash => TotalBudget - CostPrice * InitialShares;

    public void Validate()
    {
        if (Ticker.Length != 6 || !Ticker.All(char.IsAsciiDigit))
            throw new ArgumentException("股票代码必须为六位数字。");
        if (CostPrice <= 0 || StartPrice <= 0 || InitialShares <= 0 || InitialShares % 100 != 0)
            throw new ArgumentException("价格必须大于零，初始持股数须为正整数手（100 股）。");
        if (Cash < 0 || AddBudget <= 5 || 2 * AddBudget > Cash)
            throw new ArgumentException("剩余现金不足以覆盖两档加仓预算，或单档预算不大于最低佣金。");
        if (FirstDrop <= 0 || SecondDrop <= FirstDrop || SecondDrop >= 1)
            throw new ArgumentException("要求 0 < 第一档跌幅 < 第二档跌幅 < 100%。");
        if (decimal.Floor(CostPrice * (1 - SecondDrop) * 100) / 100 < .01m)
            throw new ArgumentException("加仓限价低于一分钱。");
        if (CalendarDays is < 1 or > 365 || SampleCount is < 1 or > 2000)
            throw new ArgumentException("模拟期限为 1～365 个自然日，样本数为 1～2000。");
    }
}
