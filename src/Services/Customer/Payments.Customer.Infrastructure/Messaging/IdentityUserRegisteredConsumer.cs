using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Customer.Infrastructure.Persistence;

namespace Payments.Customer.Infrastructure.Messaging;

public sealed class CustomerConsumerOptions
{
    public const string SectionName = "CustomerConsumer";

    public bool Enabled { get; init; } = true;
    public string ConsumerName { get; init; } = "customer.identity-user-registered-v1";
    public string GroupId { get; init; } = "customer-service-v1";
    public string Topic { get; init; } = "identity.lifecycle.v1";
    public string[] RetryTopics { get; init; } = ["identity.lifecycle.v1.retry.1m"];
    public string DeadLetterTopic { get; init; } = "customer.identity.lifecycle.v1.dlq";
    public int ImmediateRetryCount { get; init; } = 3;
    public int ProcessingTimeoutSeconds { get; init; } = 300;
    public int MaxPollIntervalMs { get; init; } = 300000;
    public int MaxPollRecords { get; init; } = 10;
}

public sealed class IdentityUserRegisteredConsumer : KafkaInboxConsumer<IdentityUserRegisteredIntegrationEvent, CustomerDbContext, IdentityUserRegisteredHandler>
{
    public IdentityUserRegisteredConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<CustomerConsumerOptions> options, ILogger<IdentityUserRegisteredConsumer> logger)
        : base(scopeFactory, kafkaOptions, ToInboxOptions(options.Value), logger)
    {
    }

    protected override string ExpectedEventType => IdentityUserRegisteredIntegrationEvent.EventType;
    protected override int ExpectedEventVersion => IdentityUserRegisteredIntegrationEvent.EventVersion;

    private static InboxConsumerOptions ToInboxOptions(CustomerConsumerOptions options)
        => new()
        {
            Enabled = options.Enabled,
            ConsumerName = options.ConsumerName,
            GroupId = options.GroupId,
            Topic = options.Topic,
            RetryTopics = options.RetryTopics,
            DeadLetterTopic = options.DeadLetterTopic,
            ImmediateRetryCount = options.ImmediateRetryCount,
            ProcessingTimeoutSeconds = options.ProcessingTimeoutSeconds,
            MaxPollIntervalMs = options.MaxPollIntervalMs,
            MaxPollRecords = options.MaxPollRecords,
        };
}
