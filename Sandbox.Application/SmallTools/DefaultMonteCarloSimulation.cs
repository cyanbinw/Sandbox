using Sandbox.Domain.Calculations;
using Sandbox.Infrastructure.Persistence;

namespace Sandbox.Application.SmallTools
{
    /// <summary>使用固定交易参数，只询问股票和买入价。</summary>
    public sealed class DefaultMonteCarloSimulation : MonteCarloSimulationBase
    {
        public DefaultMonteCarloSimulation(QuantResearchDbContext db, StockMonteCarloSimulator simulator)
            : base(db, simulator)
        {
        }

        protected override bool UsePreset => true;
    }
}
