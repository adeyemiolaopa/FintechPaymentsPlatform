using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationDesignTimeFactory : IDesignTimeDbContextFactory<ReconciliationDbContext>
{
    public ReconciliationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("RECONCILIATION_DATABASE_CONNECTION")
            ?? new ReconciliationOptions().ConnectionString;
        var builder = new DbContextOptionsBuilder<ReconciliationDbContext>();
        builder.UseNpgsql(connectionString);
        return new ReconciliationDbContext(builder.Options);
    }
}
