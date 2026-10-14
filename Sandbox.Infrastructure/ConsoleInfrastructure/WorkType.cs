using System;
using System.Collections.Generic;
using System.Text;

namespace Sandbox.Infrastructure.ConsoleInfrastructure
{
    public enum WorkType
    {
        MonteCarloSimulation = 1,
        RetracementCalculator,
        StockImport,
        StockUpdate,
        DefaultMonteCarloSimulation,
        LeanSimulation
    }
}
