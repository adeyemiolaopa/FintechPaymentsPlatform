using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Application.Ledger;
using Payments.Ledger.Infrastructure.Messaging;
using Payments.Ledger.Infrastructure.Persistence;
using Payments.Ledger.Infrastructure.Services;

namespace Payments.Ledger.Infrastructure;

public sealed class LedgerDatabaseOptions
{
    public const string SectionName = "LedgerDatabase";
    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_ledger;Username=payments;Password=change-me-local-only";
}

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LedgerDatabaseOptions>().Bind(configuration.GetSection(LedgerDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Ledger database connection string is required").ValidateOnStart();
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName));
        services.AddOptions<LedgerOutboxOptions>().Bind(configuration.GetSection(LedgerOutboxOptions.SectionName));
        services.AddOptions<AccountLifecycleConsumerOptions>().Bind(configuration.GetSection(AccountLifecycleConsumerOptions.SectionName));
        var database = configuration.GetSection(LedgerDatabaseOptions.SectionName).Get<LedgerDatabaseOptions>() ?? new LedgerDatabaseOptions();
        services.AddDbContext<LedgerDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<ILedgerService, LedgerService>();
        services.AddScoped<AccountLifecycleHandler>();
        services.AddHostedService<AccountLifecycleConsumer>();
        services.AddHostedService<LedgerOutboxPublisher>();
        return services;
    }
}
