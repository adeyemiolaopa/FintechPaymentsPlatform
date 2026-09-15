using System.Globalization;
using System.Text;
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
    TPayload Payload,
    string? AggregateId = null,
    string? PartitionKey = null)
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

public sealed record PaymentLifecycleIntegrationEvent(
    Guid PaymentId,
    Guid CustomerId,
    string PaymentReference,
    string PaymentType,
    string Status,
    string Currency,
    string? ReasonCode) : IIntegrationEvent
{
    public static string EventType => "payment.lifecycle.changed";
    public static int EventVersion => 1;
}

public sealed record KafkaTopicDefinition(
    string Name,
    string Owner,
    string EventType,
    int EventVersion,
    string PartitionKeyStrategy,
    int Partitions,
    int RetentionDays,
    bool Compacted = false);

public static class MessagingTopicCatalog
{
    public const int DefaultPartitions = 6;
    public const int DefaultRetentionDays = 14;

    public static readonly KafkaTopicDefinition IdentityLifecycle = new("identity.lifecycle.v1", "identity", IdentityUserRegisteredIntegrationEvent.EventType, IdentityUserRegisteredIntegrationEvent.EventVersion, "user-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition CustomerLifecycle = new("customer.lifecycle.v1", "customer", CustomerLifecycleIntegrationEvent.EventType, CustomerLifecycleIntegrationEvent.EventVersion, "customer-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition AccountLifecycle = new("account.lifecycle.v1", "account", AccountLifecycleIntegrationEvent.EventType, AccountLifecycleIntegrationEvent.EventVersion, "account-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition AccountFundsReservation = new("account.funds.reservation.v1", "account", FundsReservationIntegrationEvent.EventType, FundsReservationIntegrationEvent.EventVersion, "account-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition AccountRestriction = new("account.restriction.v1", "account", AccountRestrictionIntegrationEvent.EventType, AccountRestrictionIntegrationEvent.EventVersion, "account-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition BeneficiaryLifecycle = new("beneficiary.lifecycle.v1", "account", BeneficiaryLifecycleIntegrationEvent.EventType, BeneficiaryLifecycleIntegrationEvent.EventVersion, "customer-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition LedgerAccounts = new("ledger.accounts.v1", "ledger", LedgerAccountCreatedIntegrationEvent.EventType, LedgerAccountCreatedIntegrationEvent.EventVersion, "ledger-account-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition LedgerTransactionsPosted = new("ledger.transactions.v1", "ledger", LedgerTransactionPostedIntegrationEvent.EventType, LedgerTransactionPostedIntegrationEvent.EventVersion, "ledger-transaction-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition LedgerTransactionsReversed = new("ledger.transactions.v1", "ledger", LedgerTransactionReversedIntegrationEvent.EventType, LedgerTransactionReversedIntegrationEvent.EventVersion, "ledger-transaction-id", DefaultPartitions, DefaultRetentionDays);
    public static readonly KafkaTopicDefinition PaymentsLifecycle = new("payments.lifecycle.v1", "payment", PaymentLifecycleIntegrationEvent.EventType, PaymentLifecycleIntegrationEvent.EventVersion, "payment-id", DefaultPartitions, DefaultRetentionDays);

    public static IReadOnlyCollection<KafkaTopicDefinition> All { get; } =
    [
        IdentityLifecycle,
        CustomerLifecycle,
        AccountLifecycle,
        AccountFundsReservation,
        AccountRestriction,
        BeneficiaryLifecycle,
        LedgerAccounts,
        LedgerTransactionsPosted,
        LedgerTransactionsReversed,
        PaymentsLifecycle,
    ];

    public static IReadOnlyCollection<KafkaTopicDefinition> Topics { get; } = All
        .GroupBy(topic => topic.Name, StringComparer.Ordinal)
        .Select(group => group.First())
        .OrderBy(topic => topic.Name, StringComparer.Ordinal)
        .ToArray();

    public static KafkaTopicDefinition GetByEventType(string eventType)
        => All.Single(topic => string.Equals(topic.EventType, eventType, StringComparison.Ordinal));
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
    public int MessageTimeoutMs { get; init; } = 30000;
    public int RequestTimeoutMs { get; init; } = 10000;
    public int LingerMs { get; init; } = 5;
    public string CompressionType { get; init; } = "snappy";
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
        _producer = new ProducerBuilder<string, string>(KafkaProducerConfigFactory.Create(_options)).Build();
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
            Key = envelope.PartitionKey ?? envelope.EventId.ToString("D"),
            Value = JsonSerializer.Serialize(envelope, SerializerOptions),
            Headers = KafkaMessageHeaders.FromEnvelope(envelope),
        };

        var result = await _producer.ProduceAsync(topic, message, cancellationToken).ConfigureAwait(false);
        _logger.LogInformation("Published integration event {EventType} to {TopicPartitionOffset}", envelope.EventType, result.TopicPartitionOffset);
    }

    public void Dispose() => _producer.Dispose();
}

public static class KafkaProducerConfigFactory
{
    public static ProducerConfig Create(KafkaOptions options)
        => new()
        {
            BootstrapServers = options.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
            MessageTimeoutMs = options.MessageTimeoutMs,
            RequestTimeoutMs = options.RequestTimeoutMs,
            LingerMs = options.LingerMs,
            CompressionType = Enum.TryParse<CompressionType>(options.CompressionType, true, out var compression) ? compression : Confluent.Kafka.CompressionType.Snappy,
        };
}

public static class KafkaMessageHeaders
{
    public static Headers FromEnvelope<TPayload>(IntegrationEventEnvelope<TPayload> envelope)
        where TPayload : IIntegrationEvent
    {
        var headers = new Headers
        {
            { "event-id", Encoding.UTF8.GetBytes(envelope.EventId.ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes(envelope.EventType) },
            { "event-version", Encoding.UTF8.GetBytes(envelope.EventVersion.ToString(CultureInfo.InvariantCulture)) },
            { "correlation-id", Encoding.UTF8.GetBytes(envelope.CorrelationId) },
            { "producer", Encoding.UTF8.GetBytes(envelope.Producer) },
            { "occurred-at", Encoding.UTF8.GetBytes(envelope.OccurredAtUtc.ToString("O", CultureInfo.InvariantCulture)) },
            { "content-type", Encoding.UTF8.GetBytes("application/json") },
        };

        if (!string.IsNullOrWhiteSpace(envelope.CausationId)) headers.Add("causation-id", Encoding.UTF8.GetBytes(envelope.CausationId));
        if (!string.IsNullOrWhiteSpace(envelope.AggregateId)) headers.Add("aggregate-id", Encoding.UTF8.GetBytes(envelope.AggregateId));
        if (!string.IsNullOrWhiteSpace(envelope.PartitionKey)) headers.Add("partition-key", Encoding.UTF8.GetBytes(envelope.PartitionKey));
        return headers;
    }
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
