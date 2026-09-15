using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Account.Application.Accounts;
using Payments.Account.Infrastructure.Messaging;
using Payments.Account.Infrastructure.Persistence;
using Payments.Account.Infrastructure.Services;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Account.Infrastructure;

public sealed class AccountDatabaseOptions
{
    public const string SectionName = "AccountDatabase";
    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_account;Username=payments;Password=change-me-local-only";
}

public static class DependencyInjection
{
    public static IServiceCollection AddAccountInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<AccountDatabaseOptions>().Bind(configuration.GetSection(AccountDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Account database connection string is required").ValidateOnStart();
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName));
        services.AddOptions<AccountConsumerOptions>().Bind(configuration.GetSection(AccountConsumerOptions.SectionName));
        services.AddOptions<AccountOutboxOptions>().Bind(configuration.GetSection(AccountOutboxOptions.SectionName));
        services.AddOptions<AccountPolicyOptions>().Bind(configuration.GetSection(AccountPolicyOptions.SectionName));
        var database = configuration.GetSection(AccountDatabaseOptions.SectionName).Get<AccountDatabaseOptions>() ?? new AccountDatabaseOptions();
        services.AddDbContext<AccountDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IBeneficiaryService, BeneficiaryService>();
        services.AddScoped<CustomerLifecycleHandler>();
        services.AddHostedService<CustomerLifecycleConsumer>();
        services.AddHostedService<AccountOutboxPublisher>();
        return services;
    }
}
