using System.Globalization;
using System.IO.Compression;
using Microsoft.EntityFrameworkCore;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Console.Lean;

public sealed record DailyBar(DateTime Date, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

public static class LeanHistory
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static async Task<List<DailyBar>> FromDatabaseAsync(QuantResearchDbContext db, string ticker, CancellationToken token)
    {
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Shanghai").Date;
        var rows = await db.StockDailyKlines.AsNoTracking()
            .Where(k => k.StockCode == ticker && k.AdjustType == 1 && k.DataSource == "EastMoney")
            .OrderBy(k => k.TradeDate).ToListAsync(token);
        return Validate(rows.Select(k => new DailyBar(k.TradeDate.ToDateTime(TimeOnly.MinValue), k.OpenPrice, k.HighPrice, k.LowPrice,
            k.ClosePrice, checked((long)(k.Volume * 100)))).Where(b => b.Date < today).ToList());
    }

    public static List<DailyBar> ReadZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        if (zip.Entries.Count != 1) throw new InvalidDataException("日线 zip 必须包含一个 CSV。");
        using var reader = new StreamReader(zip.Entries[0].Open());
        var bars = new List<DailyBar>();
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var p = line.Split(',');
            if (p.Length != 6) throw new InvalidDataException("日线 CSV 列数错误。");
            bars.Add(new DailyBar(DateTime.ParseExact(p[0], "yyyyMMdd HH:mm", Invariant),
                decimal.Parse(p[1], Invariant) / 10000, decimal.Parse(p[2], Invariant) / 10000,
                decimal.Parse(p[3], Invariant) / 10000, decimal.Parse(p[4], Invariant) / 10000,
                long.Parse(p[5], Invariant)));
        }
        var today = TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Asia/Shanghai").Date;
        return Validate(bars.Where(b => b.Date < today).ToList());
    }

    public static List<DailyBar> Validate(List<DailyBar> bars)
    {
        if (bars.Count < 2) throw new InvalidDataException("至少需要两条已完成日线。");
        for (var i = 0; i < bars.Count; i++)
        {
            var b = bars[i];
            if ((i > 0 && bars[i - 1].Date >= b.Date) || b.Low <= 0 || b.High < b.Low ||
                b.Open < b.Low || b.Open > b.High || b.Close < b.Low || b.Close > b.High || b.Volume < 0)
                throw new InvalidDataException($"行情日期重复、乱序或 OHLC 无效：{b.Date:yyyy-MM-dd}");
        }
        return bars;
    }

    public static List<List<DailyBar>> Windows(List<DailyBar> bars, int days, int count)
    {
        var candidates = bars.Select((b, i) => i)
            .Where(i => bars[i].Date.AddDays(days) <= bars[^1].Date && i + 1 < bars.Count &&
                        bars[i + 1].Date <= bars[i].Date.AddDays(days)).ToList();
        if (candidates.Count == 0) throw new InvalidDataException("历史不足以构成完整模拟窗口。");
        var n = Math.Min(count, candidates.Count);
        return Enumerable.Range(0, n).Select(j => candidates[n == 1 ? candidates.Count - 1 :
            (int)((long)j * (candidates.Count - 1) / (n - 1))])
            .Select(i => bars.Skip(i).TakeWhile(b => b.Date <= bars[i].Date.AddDays(days)).ToList()).ToList();
    }

    public static void WriteZip(string dataFolder, string ticker, IEnumerable<DailyBar> bars)
    {
        var folder = Path.Combine(dataFolder, "equity", "china", "daily");
        Directory.CreateDirectory(folder);
        using var zip = ZipFile.Open(Path.Combine(folder, ticker + ".zip"), ZipArchiveMode.Create);
        using var writer = new StreamWriter(zip.CreateEntry(ticker + ".csv").Open());
        foreach (var b in bars)
            writer.WriteLine(string.Join(",", b.Date.ToString("yyyyMMdd HH:mm", Invariant),
                Price(b.Open), Price(b.High), Price(b.Low), Price(b.Close), b.Volume.ToString(Invariant)));
    }

    public static IEnumerable<DailyBar> Rebase(List<DailyBar> bars, decimal price)
    {
        // Initial day is only a seed. Its intraday range must not trigger an add.
        yield return new DailyBar(bars[0].Date, price, price, price, price, 0);
        var scale = price / bars[0].Close;
        foreach (var b in bars.Skip(1))
            yield return b with { Open = b.Open * scale, High = b.High * scale, Low = b.Low * scale, Close = b.Close * scale };
    }

    private static string Price(decimal price) => decimal.Round(price * 10000, 0).ToString("0", Invariant);
}
