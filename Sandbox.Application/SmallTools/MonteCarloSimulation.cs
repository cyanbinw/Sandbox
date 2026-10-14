using Sandbox.Domain.Calculations;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.SmallTools
{
    /// <summary>逐项输入自定义模拟参数。</summary>
    public sealed class MonteCarloSimulation : MonteCarloSimulationBase
    {
        public MonteCarloSimulation(QuantResearchDbContext db, StockMonteCarloSimulator simulator)
            : base(db, simulator)
        {
        }

        protected override bool UsePreset => false;
    }
}
