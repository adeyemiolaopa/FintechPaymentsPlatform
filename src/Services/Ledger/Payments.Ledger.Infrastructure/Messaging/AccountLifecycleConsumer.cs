using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Infrastructure.Persistence;

namespace Payments.Ledger.Infrastructure.Messaging;

public sealed class AccountLifecycleConsumerOptions
{
    public const string SectionName = "LedgerConsumer";
    public bool Enabled { get; init; } = true;
    public string ConsumerName { get; init; } = "ledger.account-lifecycle-v1";
    public string GroupId { get; init; } = "ledger-service-v1";
    public string AccountLifecycleTopic { get; init; } = "account.lifecycle.v1";
    public string[] RetryTopics { get; init; } = ["account.lifecycle.v1.retry.1m"];
    public string DeadLetterTopic { get; init; } = "ledger.account.lifecycle.v1.dlq";
    public int ImmediateRetryCount { get; init; } = 3;
    public int ProcessingTimeoutSeconds { get; init; } = 300;
    public int MaxPollIntervalMs { get; init; } = 300000;
    public int MaxPollRecords { get; init; } = 10;
}

public sealed class AccountLifecycleConsumer : KafkaInboxConsumer<AccountLifecycleIntegrationEvent, LedgerDbContext, AccountLifecycleHandler>
{
    public AccountLifecycleConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<AccountLifecycleConsumerOptions> options, ILogger<AccountLifecycleConsumer> logger)
        : base(scopeFactory, kafkaOptions, ToInboxOptions(options.Value), logger)
    {
    }

    protected override string ExpectedEventType => AccountLifecycleIntegrationEvent.EventType;
    protected override int ExpectedEventVersion => AccountLifecycleIntegrationEvent.EventVersion;

    private static InboxConsumerOptions ToInboxOptions(AccountLifecycleConsumerOptions options)
        => new()
        {
            Enabled = options.Enabled,
            ConsumerName = options.ConsumerName,
            GroupId = options.GroupId,
            Topic = options.AccountLifecycleTopic,
            RetryTopics = options.RetryTopics,
            DeadLetterTopic = options.DeadLetterTopic,
            ImmediateRetryCount = options.ImmediateRetryCount,
            ProcessingTimeoutSeconds = options.ProcessingTimeoutSeconds,
            MaxPollIntervalMs = options.MaxPollIntervalMs,
            MaxPollRecords = options.MaxPollRecords,
        };
}
