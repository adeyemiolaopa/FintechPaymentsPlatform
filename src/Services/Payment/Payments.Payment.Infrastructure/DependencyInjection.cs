using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Payment.Application.Payments;
using Payments.Payment.Infrastructure.Messaging;
using Payments.Payment.Infrastructure.Persistence;
using Payments.Payment.Infrastructure.Services;

namespace Payments.Payment.Infrastructure;

public sealed class PaymentDatabaseOptions
{
    public const string SectionName = "PaymentDatabase";
    public string ConnectionString { get; init; } = "Host=127.0.0.1;Port=15432;Database=payments_payment;Username=payments;Password=change-me-local-only";
}


public sealed class PaymentIdempotencyOptions
{
    public const string SectionName = "PaymentIdempotency";
    public int RetentionDays { get; init; } = 7;
    public bool CleanupEnabled { get; init; } = true;
    public int CleanupIntervalMinutes { get; init; } = 60;
    public int CleanupBatchSize { get; init; } = 100;
}
public sealed class DownstreamServiceOptions
{
    public const string SectionName = "DownstreamServices";
    public string AccountBaseUrl { get; init; } = "http://account-api:8080";
    public string LedgerBaseUrl { get; init; } = "http://ledger-api:8080";
    public int TimeoutSeconds { get; init; } = 5;
}

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<PaymentDatabaseOptions>().Bind(configuration.GetSection(PaymentDatabaseOptions.SectionName)).Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "Payment database connection string is required").ValidateOnStart();
        services.AddOptions<DownstreamServiceOptions>().Bind(configuration.GetSection(DownstreamServiceOptions.SectionName));
        services.AddOptions<KafkaOptions>().Bind(configuration.GetSection(KafkaOptions.SectionName));
        services.AddOptions<PaymentOutboxOptions>().Bind(configuration.GetSection(PaymentOutboxOptions.SectionName));
        services.AddOptions<PaymentRecoveryOptions>().Bind(configuration.GetSection(PaymentRecoveryOptions.SectionName));
        services.AddOptions<PaymentIdempotencyOptions>().Bind(configuration.GetSection(PaymentIdempotencyOptions.SectionName));
        var database = configuration.GetSection(PaymentDatabaseOptions.SectionName).Get<PaymentDatabaseOptions>() ?? new PaymentDatabaseOptions();
        var downstream = configuration.GetSection(DownstreamServiceOptions.SectionName).Get<DownstreamServiceOptions>() ?? new DownstreamServiceOptions();
        services.AddDbContext<PaymentDbContext>(options => options.UseNpgsql(database.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddScoped<IPaymentService, PaymentService>();
        services.AddHttpClient<IAccountServiceClient, HttpAccountServiceClient>(client => { client.BaseAddress = new Uri(downstream.AccountBaseUrl); client.Timeout = TimeSpan.FromSeconds(Math.Clamp(downstream.TimeoutSeconds, 1, 30)); });
        services.AddHttpClient<ILedgerServiceClient, HttpLedgerServiceClient>(client => { client.BaseAddress = new Uri(downstream.LedgerBaseUrl); client.Timeout = TimeSpan.FromSeconds(Math.Clamp(downstream.TimeoutSeconds, 1, 30)); });
        services.AddScoped<PaymentReferenceHandler>();
        services.AddHostedService<PaymentOutboxPublisher>();
        services.AddHostedService<PaymentRecoveryWorker>();
        services.AddHostedService<PaymentIdempotencyCleanupWorker>();
        return services;
    }
}
