using Microsoft.EntityFrameworkCore;
using Sandbox.Infrastructure.Repositories;

namespace Sandbox.Infrastructure.Persistence
{
    public class QuantResearchDbContext : DbContext
    {
        public QuantResearchDbContext(DbContextOptions<QuantResearchDbContext> options) : base(options)
        {
        }

        public DbSet<StockDailyKline> StockDailyKlines { get; set; }

        public DbSet<Stock> Stocks { get; set; }

    }
}
