using Microsoft.Extensions.DependencyInjection;
using Sandbox.Console.Factory;
using Sandbox.Application.Services;
using Sandbox.Application.SmallTools;
using Microsoft.EntityFrameworkCore;
using Sandbox.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Sandbox.Domain.Calculations;

namespace Sandbox.Console
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddSandbox(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddScoped<StockMonteCarloSimulator>();
            services.AddScoped<WorkerFactory>();
            services.AddScoped<Lean.LeanSimulationWorker>();
            services.AddScoped<MonteCarloSimulation>();
            services.AddScoped<DefaultMonteCarloSimulation>();
            services.AddScoped<RetracementCalculator>();
            services.AddScoped<StockImportService>();
            services.AddScoped<StockUpdateService>();

            services.AddDbContext<QuantResearchDbContext>(options =>
            {
                var connectionString = configuration.GetConnectionString("QuantResearch");
                if (string.IsNullOrWhiteSpace(connectionString))
                    throw new InvalidOperationException("请在 appsettings.json 或 appsettings.Local.json 中配置 ConnectionStrings:QuantResearch，或设置环境变量 ConnectionStrings__QuantResearch。");

                options.UseSqlServer(connectionString);
            });

            return services;
        }
    }
}
