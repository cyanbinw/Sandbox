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
            RegisterSmallTools<Lean.LeanSimulationWorker>(WorkType.LeanSimulation, "Lean", "股票持仓情景模拟", "设置持仓和加仓规则，由 Lean 执行历史情景并对比不加仓。");
            RegisterSmallTools<DefaultMonteCarloSimulation>(WorkType.DefaultMonteCarloSimulation, "小工具", "蒙特卡罗模拟（默认参数）", "只需选择股票和输入买入价，使用预设的 30 天止损补仓策略。");
            RegisterSmallTools<MonteCarloSimulation>(WorkType.MonteCarloSimulation, "小工具", "蒙特卡罗模拟", "通过大量随机采样重复模拟可能结果，用于估计概率分布、风险和不确定性。");
            RegisterSmallTools<RetracementCalculator>(WorkType.RetracementCalculator, "小工具", "回撤计算", "根据上涨最高价、起点和回撤比例计算回踩或破位价格。");
            RegisterSmallTools<StockImportService>(WorkType.StockImport, "数据", "股票数据导入", "输入股票代码导入历史行情，今天已更新则跳过。");
            RegisterSmallTools<StockUpdateService>(WorkType.StockUpdate, "数据", "更新已有股票数据", "批量更新库内所有股票，每只间隔 5 秒，今天已更新则跳过。");
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
