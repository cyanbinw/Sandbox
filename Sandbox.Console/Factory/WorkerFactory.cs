using Sandbox.Application.Services;
using Sandbox.Application.SmallTools;
using Sandbox.Infrastructure.ConsoleInfrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Sandbox.Console.Factory
{
    public sealed record WorkerRegistration(string Group, string Name, string Description, Func<IWorker> CreateWorker);

    public class WorkerFactory
    {
        private readonly Dictionary<WorkType, WorkerRegistration> _workers = new Dictionary<WorkType, WorkerRegistration>();
        private readonly IServiceProvider _services;

        public IReadOnlyDictionary<WorkType, WorkerRegistration> Workers => _workers;

        public WorkerFactory(IServiceProvider services)
        {
            _services = services;
            RegisterSmallTools<MonteCarloSimulation>(WorkType.MonteCarloSimulation, "小工具", "蒙特卡罗模拟", "通过大量随机采样重复模拟可能结果，用于估计概率分布、风险和不确定性。");
            RegisterSmallTools<RetracementCalculator>(WorkType.RetracementCalculator, "小工具", "回撤计算", "根据上涨最高价、起点和回撤比例计算回踩或破位价格。");
            RegisterSmallTools<StockImportService>(WorkType.StockImport, "数据", "股票数据导入", "根据填写的股票代码，从东方财富导入历史日 K 线数据到数据库。");
        }

        public void RegisterSmallTools<TWorker>(WorkType workType, string Group, string Name, string Description)
            where TWorker : class, IWorker =>
            AddWorker(workType, Group, Name, Description, () => _services.GetRequiredService<TWorker>());

        public void AddWorker(WorkType key, string group, string name, string description, Func<IWorker> value)
        {
            ArgumentNullException.ThrowIfNull(value);
            ArgumentException.ThrowIfNullOrWhiteSpace(group);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            ArgumentException.ThrowIfNullOrWhiteSpace(description);
            _workers[key] = new WorkerRegistration(group, name, description, value);
        }
    }
}
