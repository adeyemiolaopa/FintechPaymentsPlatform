using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Service.Template.Application.Examples;
using Payments.Service.Template.Infrastructure.Persistence;

namespace Payments.Service.Template.Infrastructure;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_template;Username=payments;Password=change-me-local-only";
}

public static class DependencyInjection
{
    public static IServiceCollection AddTemplateInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Database connection string is required")
            .ValidateOnStart();

        var databaseOptions = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        services.AddDbContext<TemplateDbContext>(options =>
        {
            options.UseNpgsql(databaseOptions.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3));
        });

        services.AddScoped<IExampleRepository, ExampleRepository>();
        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<TemplateDbContext>());
        return services;
    }
}


