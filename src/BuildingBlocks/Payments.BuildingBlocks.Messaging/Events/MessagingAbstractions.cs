using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;

namespace Payments.BuildingBlocks.Messaging.Events;

public interface IIntegrationEvent
{
    static abstract string EventType { get; }
    static abstract int EventVersion { get; }
}

public sealed record IntegrationEventEnvelope<TPayload>(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAtUtc,
    string CorrelationId,
    string? CausationId,
    string Producer,
    TPayload Payload)
    where TPayload : IIntegrationEvent;

public sealed record IdentityUserRegisteredIntegrationEvent(
    Guid UserId,
    Guid CustomerId,
    string Email,
    string PhoneNumber) : IIntegrationEvent
{
    public static string EventType => "identity.user.registered";
    public static int EventVersion => 1;
}


public sealed record CustomerLifecycleIntegrationEvent(
    Guid CustomerId,
    string Status,
    string KycStatus) : IIntegrationEvent
{
    public static string EventType => "customer.lifecycle.changed";
    public static int EventVersion => 1;
}

public sealed record AccountLifecycleIntegrationEvent(
    Guid AccountId,
    Guid CustomerId,
    string Currency,
    string AccountType,
    string Status,
    string? Reason) : IIntegrationEvent
{
    public static string EventType => "account.lifecycle.changed";
    public static int EventVersion => 1;
}

public sealed record FundsReservationIntegrationEvent(
    Guid AccountId,
    Guid CustomerId,
    Guid ReservationId,
    string ReferenceId,
    decimal Amount,
    string Currency,
    string Status) : IIntegrationEvent
{
    public static string EventType => "account.funds.reservation.changed";
    public static int EventVersion => 1;
}

public sealed record AccountRestrictionIntegrationEvent(
    Guid AccountId,
    Guid CustomerId,
    Guid RestrictionId,
    string RestrictionType,
    string Status,
    string? Reason) : IIntegrationEvent
{
    public static string EventType => "account.restriction.changed";
    public static int EventVersion => 1;
}

public sealed record BeneficiaryLifecycleIntegrationEvent(
    Guid BeneficiaryId,
    Guid CustomerId,
    string Type,
    string Status,
    string Currency,
    string CountryCode) : IIntegrationEvent
{
    public static string EventType => "beneficiary.lifecycle.changed";
    public static int EventVersion => 1;
}

public sealed record LedgerAccountCreatedIntegrationEvent(
    Guid LedgerAccountId,
    string ExternalReference,
    string AccountCode,
    string AccountType,
    string Currency) : IIntegrationEvent
{
    public static string EventType => "ledger.account.created";
    public static int EventVersion => 1;
}

public sealed record LedgerTransactionPostedIntegrationEvent(
    Guid TransactionId,
    string ExternalReference,
    string TransactionType,
    string Currency,
    DateTimeOffset OccurredAtUtc) : IIntegrationEvent
{
    public static string EventType => "ledger.transaction.posted";
    public static int EventVersion => 1;
}

public sealed record LedgerTransactionReversedIntegrationEvent(
    Guid OriginalTransactionId,
    Guid ReversalTransactionId,
    string ExternalReference,
    string Currency,
    string Reason) : IIntegrationEvent
{
    public static string EventType => "ledger.transaction.reversed";
    public static int EventVersion => 1;
}
public interface IEventPublisher
{
    Task PublishAsync<TPayload>(string topic, TPayload payload, CancellationToken cancellationToken = default)
        where TPayload : IIntegrationEvent;
}

public sealed class KafkaOptions
{
    public const string SectionName = "Kafka";

    public string BootstrapServers { get; init; } = "localhost:9092";

    public string ProducerName { get; init; } = "template-service";

    public string SchemaRegistryUrl { get; init; } = "http://localhost:8081";
}

public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IProducer<string, string> _producer;
    private readonly IClock _clock;
    private readonly IRequestContext _requestContext;
    private readonly KafkaOptions _options;
    private readonly ILogger<KafkaEventPublisher> _logger;

    public KafkaEventPublisher(IOptions<KafkaOptions> options, IClock clock, IRequestContext requestContext, ILogger<KafkaEventPublisher> logger)
    {
        _options = options.Value;
        _clock = clock;
        _requestContext = requestContext;
        _logger = logger;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();
    }

    public async Task PublishAsync<TPayload>(string topic, TPayload payload, CancellationToken cancellationToken = default)
        where TPayload : IIntegrationEvent
    {
        var envelope = new IntegrationEventEnvelope<TPayload>(
            Guid.NewGuid(),
            TPayload.EventType,
            TPayload.EventVersion,
            _clock.UtcNow,
            _requestContext.CorrelationId,
            _requestContext.CausationId,
            _options.ProducerName,
            payload);

        var message = new Message<string, string>
        {
            Key = envelope.EventId.ToString("D"),
            Value = JsonSerializer.Serialize(envelope, SerializerOptions),
            Headers = new Headers
            {
                { "x-correlation-id", System.Text.Encoding.UTF8.GetBytes(envelope.CorrelationId) },
                { "event-type", System.Text.Encoding.UTF8.GetBytes(envelope.EventType) },
                { "event-version", System.Text.Encoding.UTF8.GetBytes(envelope.EventVersion.ToString(System.Globalization.CultureInfo.InvariantCulture)) },
            },
        };

        var result = await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Published integration event {EventType} to {TopicPartitionOffset}", envelope.EventType, result.TopicPartitionOffset);
    }

    public void Dispose() => _producer.Dispose();
}

public static class KafkaServiceCollectionExtensions
{
    public static IServiceCollection AddKafkaMessaging(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<KafkaOptions>()
            .Bind(configuration.GetSection(KafkaOptions.SectionName))
            .Validate(options => !string.IsNullOrWhiteSpace(options.BootstrapServers), "Kafka bootstrap servers are required")
            .ValidateOnStart();
        services.AddScoped<IEventPublisher, KafkaEventPublisher>();
        return services;
    }
}
