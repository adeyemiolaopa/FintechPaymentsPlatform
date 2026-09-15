using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Account.Infrastructure.Persistence;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Account.Infrastructure.Messaging;

public sealed class AccountConsumerOptions
{
    public const string SectionName = "AccountConsumer";
    public bool Enabled { get; init; } = true;
    public string ConsumerName { get; init; } = "account.customer-lifecycle-v1";
    public string GroupId { get; init; } = "account-service-v1";
    public string CustomerLifecycleTopic { get; init; } = "customer.lifecycle.v1";
    public string[] RetryTopics { get; init; } = ["customer.lifecycle.v1.retry.1m"];
    public string DeadLetterTopic { get; init; } = "account.customer.lifecycle.v1.dlq";
    public int ImmediateRetryCount { get; init; } = 3;
    public int ProcessingTimeoutSeconds { get; init; } = 300;
    public int MaxPollIntervalMs { get; init; } = 300000;
    public int MaxPollRecords { get; init; } = 10;
}

public sealed class CustomerLifecycleConsumer : KafkaInboxConsumer<CustomerLifecycleIntegrationEvent, AccountDbContext, CustomerLifecycleHandler>
{
    public CustomerLifecycleConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<AccountConsumerOptions> options, ILogger<CustomerLifecycleConsumer> logger)
        : base(scopeFactory, kafkaOptions, ToInboxOptions(options.Value), logger)
    {
    }

    protected override string ExpectedEventType => CustomerLifecycleIntegrationEvent.EventType;
    protected override int ExpectedEventVersion => CustomerLifecycleIntegrationEvent.EventVersion;

    private static InboxConsumerOptions ToInboxOptions(AccountConsumerOptions options)
        => new()
        {
            Enabled = options.Enabled,
            ConsumerName = options.ConsumerName,
            GroupId = options.GroupId,
            Topic = options.CustomerLifecycleTopic,
            RetryTopics = options.RetryTopics,
            DeadLetterTopic = options.DeadLetterTopic,
            ImmediateRetryCount = options.ImmediateRetryCount,
            ProcessingTimeoutSeconds = options.ProcessingTimeoutSeconds,
            MaxPollIntervalMs = options.MaxPollIntervalMs,
            MaxPollRecords = options.MaxPollRecords,
        };
}
