using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Reconciliation.Infrastructure;

public sealed class PaymentReferenceRecord
{
    public Guid PaymentId { get; set; }
    public string PaymentReference { get; set; } = "";
    public string PaymentType { get; set; } = "";
    public string Status { get; set; } = "";
    public string Currency { get; set; } = "";
    public string? ReasonCode { get; set; }
    public DateTimeOffset SourceUpdatedAtUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class LedgerReferenceRecord
{
    public Guid TransactionId { get; set; }
    public string ExternalReference { get; set; } = "";
    public string TransactionType { get; set; } = "";
    public string Currency { get; set; } = "";
    public string Status { get; set; } = "Posted";
    public DateTimeOffset SourceUpdatedAtUtc { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ReconciliationPaymentConsumerOptions
{
    public bool Enabled { get; set; } = true;
    public string ConsumerName { get; set; } = "reconciliation.payment-lifecycle-v1";
    public string GroupId { get; set; } = "reconciliation-payment-read-model-v1";
    public string Topic { get; set; } = "payments.lifecycle.v1";
    public string[] RetryTopics { get; set; } = ["payments.lifecycle.v1.retry.1m"];
    public string DeadLetterTopic { get; set; } = "reconciliation.payment.lifecycle.v1.dlq";
}

public sealed class ReconciliationLedgerConsumerOptions
{
    public bool Enabled { get; set; } = true;
    public string ConsumerName { get; set; } = "reconciliation.ledger-posted-v1";
    public string GroupId { get; set; } = "reconciliation-ledger-read-model-v1";
    public string Topic { get; set; } = "ledger.transactions.v1";
    public string[] RetryTopics { get; set; } = ["ledger.transactions.v1.retry.1m"];
    public string DeadLetterTopic { get; set; } = "reconciliation.ledger.transactions.v1.dlq";
}

public sealed class PaymentReferenceHandler(ReconciliationDbContext db) : IIntegrationEventHandler<PaymentLifecycleIntegrationEvent>
{
    public async Task HandleAsync(IntegrationEventEnvelope<PaymentLifecycleIntegrationEvent> envelope, IntegrationEventContext context, CancellationToken ct = default)
    {
        var value = envelope.Payload;
        var record = await db.PaymentReferences.SingleOrDefaultAsync(x => x.PaymentId == value.PaymentId, ct).ConfigureAwait(false);
        if (record is null)
        {
            record = new PaymentReferenceRecord { PaymentId = value.PaymentId };
            db.PaymentReferences.Add(record);
        }
        if (record.SourceUpdatedAtUtc > envelope.OccurredAtUtc) return;
        record.PaymentReference = value.PaymentReference;
        record.PaymentType = value.PaymentType;
        record.Status = value.Status;
        record.Currency = value.Currency;
        record.ReasonCode = value.ReasonCode;
        record.SourceUpdatedAtUtc = envelope.OccurredAtUtc;
        record.ObservedAtUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class LedgerReferenceHandler(ReconciliationDbContext db) : IIntegrationEventHandler<LedgerTransactionPostedIntegrationEvent>
{
    public async Task HandleAsync(IntegrationEventEnvelope<LedgerTransactionPostedIntegrationEvent> envelope, IntegrationEventContext context, CancellationToken ct = default)
    {
        var value = envelope.Payload;
        var record = await db.LedgerReferences.SingleOrDefaultAsync(x => x.TransactionId == value.TransactionId, ct).ConfigureAwait(false);
        if (record is null)
        {
            record = new LedgerReferenceRecord { TransactionId = value.TransactionId };
            db.LedgerReferences.Add(record);
        }
        if (record.SourceUpdatedAtUtc > value.OccurredAtUtc) return;
        record.ExternalReference = value.ExternalReference;
        record.TransactionType = value.TransactionType;
        record.Currency = value.Currency;
        record.Status = "Posted";
        record.SourceUpdatedAtUtc = value.OccurredAtUtc;
        record.ObservedAtUtc = DateTimeOffset.UtcNow;
    }
}

public sealed class PaymentReferenceConsumer : KafkaInboxConsumer<PaymentLifecycleIntegrationEvent, ReconciliationDbContext, PaymentReferenceHandler>
{
    public PaymentReferenceConsumer(IServiceScopeFactory scopes, IOptions<KafkaOptions> kafka, IOptions<ReconciliationPaymentConsumerOptions> options, ILogger<PaymentReferenceConsumer> logger)
        : base(scopes, kafka, ToInbox(options.Value), logger) { }
    protected override string ExpectedEventType => PaymentLifecycleIntegrationEvent.EventType;
    protected override int ExpectedEventVersion => PaymentLifecycleIntegrationEvent.EventVersion;
    private static InboxConsumerOptions ToInbox(ReconciliationPaymentConsumerOptions value) => new() { Enabled = value.Enabled, ConsumerName = value.ConsumerName, GroupId = value.GroupId, Topic = value.Topic, RetryTopics = value.RetryTopics, DeadLetterTopic = value.DeadLetterTopic };
}

public sealed class LedgerReferenceConsumer : KafkaInboxConsumer<LedgerTransactionPostedIntegrationEvent, ReconciliationDbContext, LedgerReferenceHandler>
{
    public LedgerReferenceConsumer(IServiceScopeFactory scopes, IOptions<KafkaOptions> kafka, IOptions<ReconciliationLedgerConsumerOptions> options, ILogger<LedgerReferenceConsumer> logger)
        : base(scopes, kafka, ToInbox(options.Value), logger) { }
    protected override string ExpectedEventType => LedgerTransactionPostedIntegrationEvent.EventType;
    protected override int ExpectedEventVersion => LedgerTransactionPostedIntegrationEvent.EventVersion;
    private static InboxConsumerOptions ToInbox(ReconciliationLedgerConsumerOptions value) => new() { Enabled = value.Enabled, ConsumerName = value.ConsumerName, GroupId = value.GroupId, Topic = value.Topic, RetryTopics = value.RetryTopics, DeadLetterTopic = value.DeadLetterTopic };
}
