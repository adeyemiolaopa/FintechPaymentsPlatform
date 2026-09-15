using System.Text.Json;
using Confluent.Kafka;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.UnitTests;

public sealed class MessagingContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public void Producer_config_enables_idempotent_all_ack_delivery()
    {
        var config = KafkaProducerConfigFactory.Create(new KafkaOptions
        {
            BootstrapServers = "localhost:9092",
            MessageTimeoutMs = 45000,
            RequestTimeoutMs = 12000,
            LingerMs = 10,
            CompressionType = "gzip",
        });

        Assert.True(config.EnableIdempotence);
        Assert.Equal(Acks.All, config.Acks);
        Assert.Equal(45000, config.MessageTimeoutMs);
        Assert.Equal(12000, config.RequestTimeoutMs);
        Assert.Equal(10, config.LingerMs);
        Assert.Equal(CompressionType.Gzip, config.CompressionType);
    }

    [Fact]
    public void Envelope_headers_include_delivery_contract_metadata()
    {
        var envelope = new IntegrationEventEnvelope<PaymentLifecycleIntegrationEvent>(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            PaymentLifecycleIntegrationEvent.EventType,
            PaymentLifecycleIntegrationEvent.EventVersion,
            DateTimeOffset.Parse("2026-09-15T20:00:00Z"),
            "corr-123",
            "cause-456",
            "payment-service",
            new PaymentLifecycleIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "PAY-001", "Transfer", "Completed", "NGN", null),
            "payment-123",
            "payment-123");

        var headers = KafkaMessageHeaders.FromEnvelope(envelope).Select(header => header.Key).ToArray();

        Assert.Contains("event-id", headers);
        Assert.Contains("event-type", headers);
        Assert.Contains("event-version", headers);
        Assert.Contains("correlation-id", headers);
        Assert.Contains("causation-id", headers);
        Assert.Contains("producer", headers);
        Assert.Contains("occurred-at", headers);
        Assert.Contains("aggregate-id", headers);
        Assert.Contains("partition-key", headers);
        Assert.Contains("content-type", headers);
    }

    [Fact]
    public void Topic_catalog_covers_every_declared_integration_event_type()
    {
        var eventTypes = new[]
        {
            IdentityUserRegisteredIntegrationEvent.EventType,
            CustomerLifecycleIntegrationEvent.EventType,
            AccountLifecycleIntegrationEvent.EventType,
            FundsReservationIntegrationEvent.EventType,
            AccountRestrictionIntegrationEvent.EventType,
            BeneficiaryLifecycleIntegrationEvent.EventType,
            LedgerAccountCreatedIntegrationEvent.EventType,
            LedgerTransactionPostedIntegrationEvent.EventType,
            LedgerTransactionReversedIntegrationEvent.EventType,
            PaymentLifecycleIntegrationEvent.EventType,
        };

        foreach (var eventType in eventTypes)
        {
            var topic = MessagingTopicCatalog.GetByEventType(eventType);
            Assert.Equal(eventType, topic.EventType);
            Assert.EndsWith(".v1", topic.Name, StringComparison.Ordinal);
            Assert.Equal(1, topic.EventVersion);
            Assert.True(topic.Partitions >= 3);
            Assert.False(string.IsNullOrWhiteSpace(topic.PartitionKeyStrategy));
        }
    }

    [Fact]
    public void Envelope_deserialization_accepts_older_payloads_without_optional_routing_fields()
    {
        var json = """
        {
          "eventId": "22222222-2222-2222-2222-222222222222",
          "eventType": "identity.user.registered",
          "eventVersion": 1,
          "occurredAtUtc": "2026-09-15T20:00:00Z",
          "correlationId": "corr-123",
          "causationId": null,
          "producer": "identity-service",
          "payload": {
            "userId": "33333333-3333-3333-3333-333333333333",
            "customerId": "44444444-4444-4444-4444-444444444444",
            "email": "user@example.com",
            "phoneNumber": "+2348012345678"
          }
        }
        """;

        var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent>>(json, SerializerOptions);

        Assert.NotNull(envelope);
        Assert.Null(envelope.AggregateId);
        Assert.Null(envelope.PartitionKey);
        Assert.Equal(IdentityUserRegisteredIntegrationEvent.EventType, envelope.EventType);
        Assert.Equal(Guid.Parse("33333333-3333-3333-3333-333333333333"), envelope.Payload.UserId);
    }
}
