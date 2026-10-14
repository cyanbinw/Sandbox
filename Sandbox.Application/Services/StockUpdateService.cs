using Microsoft.EntityFrameworkCore;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.Services
{
    /// <summary>批量更新已入库股票的行情，每只间隔 5 秒，复用导入的每日去重及保存逻辑。</summary>
    public class StockUpdateService : IWorker
    {
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromSeconds(5);
        private readonly QuantResearchDbContext db;
        private readonly StockImportService importer;

        public StockUpdateService(QuantResearchDbContext db, StockImportService importer)
        {
            this.db = db;
            this.importer = importer;
        }

        public void Run() => RunAsync().GetAwaiter().GetResult();

        public async Task RunAsync()
        {
            // 兼容只有行情记录、没有基础信息的股票。
            var codes = await db.Stocks.Select(s => s.StockCode)
                .Union(db.StockDailyKlines
                    .Where(k => k.AdjustType == 1 && k.DataSource == "EastMoney")
                    .Select(k => k.StockCode))
                .OrderBy(code => code).ToListAsync();
            if (codes.Count == 0)
            {
                Console.WriteLine("没有可更新的股票，请先使用主菜单的「股票数据导入」。");
                return;
            }

            Console.WriteLine($"开始批量更新 {codes.Count} 只股票，每只间隔 5 秒（今天已更新则跳过）。");
            for (int i = 0; i < codes.Count; i++)
            {
                if (i > 0)
                    await Task.Delay(UpdateInterval);

                Console.WriteLine($"[{i + 1}/{codes.Count}] 更新 {codes[i]}");
                await importer.ImportAsync(codes[i]);
            }
            Console.WriteLine("批量更新完成。");
        }
    }
}
