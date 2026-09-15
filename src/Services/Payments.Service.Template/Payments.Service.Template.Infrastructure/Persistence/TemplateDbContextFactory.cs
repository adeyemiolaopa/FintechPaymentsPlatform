using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Payments.Service.Template.Infrastructure.Persistence;

public sealed class TemplateDbContextFactory : IDesignTimeDbContextFactory<TemplateDbContext>
{
    public TemplateDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<TemplateDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=15432;Database=payments_template;Username=payments;Password=change-me-local-only")
            .Options;

        return new TemplateDbContext(options);
    }
}


