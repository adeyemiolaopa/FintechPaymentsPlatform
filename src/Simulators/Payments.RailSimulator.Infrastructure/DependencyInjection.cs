using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Domain;
using Payments.RailSimulator.Infrastructure.Persistence;
using Payments.RailSimulator.Infrastructure.Services;

namespace Payments.RailSimulator.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddRailSimulatorInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RailSimulatorDatabaseOptions>().Bind(configuration.GetSection(RailSimulatorDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Rail simulator database connection string is required").ValidateOnStart();
        services.AddOptions<RailSimulatorOptions>().Bind(configuration.GetSection(RailSimulatorOptions.SectionName));

        var database = configuration.GetSection(RailSimulatorDatabaseOptions.SectionName).Get<RailSimulatorDatabaseOptions>() ?? new RailSimulatorDatabaseOptions();
        services.AddDbContext<RailSimulatorDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddSingleton<IClock, RailSystemClock>();
        services.AddScoped<IRailSimulatorService, RailSimulatorService>();
        services.AddScoped<IRailRequestAuthenticator, RailRequestAuthenticator>();
        services.AddSingleton<HttpClient>();
        services.AddHostedService<RailDelayedProcessingWorker>();
        services.AddHostedService<RailCallbackWorker>();
        return services;
    }

    public static async Task InitializeRailSimulatorAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RailSimulatorDbContext>();
        await dbContext.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

        var options = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<RailSimulatorOptions>>().Value;
        if (!await dbContext.ProviderScenarios.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            dbContext.ProviderScenarios.Add(ProviderScenario.Default(DateTimeOffset.UtcNow));
        }

        if (!await dbContext.CallbackConfigurations.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            dbContext.CallbackConfigurations.Add(CallbackConfiguration.Default(DateTimeOffset.UtcNow));
        }

        if (!await dbContext.ProviderClients.AnyAsync(client => client.ClientId == options.DefaultClientId, cancellationToken).ConfigureAwait(false))
        {
            dbContext.ProviderClients.Add(ProviderClient.Create(options.DefaultClientId, options.DefaultApiKey, options.DefaultClientSecret, DateTimeOffset.UtcNow));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}

internal sealed class RailSystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
