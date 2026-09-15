using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.UnitTests;

public sealed class InboxMessageTests
{
    [Fact]
    public void Processing_message_marks_processed_once_business_effect_is_committed()
    {
        var now = DateTimeOffset.Parse("2026-09-15T20:00:00Z");
        var message = InboxMessage.Start(Guid.NewGuid(), PaymentLifecycleIntegrationEvent.EventType, 1, "notification.payments-v1", "payments.lifecycle.v1", 0, 42, now, "hash-a", "corr-1", "worker-1");

        message.MarkProcessed(now.AddSeconds(2));

        Assert.Equal(InboxMessageStatus.Processed, message.Status);
        Assert.Equal(now.AddSeconds(2), message.ProcessedAtUtc);
        Assert.Null(message.LastError);
    }

    [Fact]
    public void Same_event_id_with_different_payload_hash_is_detected()
    {
        var message = InboxMessage.Start(Guid.NewGuid(), PaymentLifecycleIntegrationEvent.EventType, 1, "notification.payments-v1", "payments.lifecycle.v1", 0, 42, DateTimeOffset.UtcNow, "hash-a", "corr-1", "worker-1");

        Assert.False(message.HasDifferentPayload("hash-a"));
        Assert.True(message.HasDifferentPayload("hash-b"));
    }

    [Fact]
    public void Stale_processing_message_can_be_restarted_with_new_owner_and_offset()
    {
        var started = DateTimeOffset.Parse("2026-09-15T20:00:00Z");
        var message = InboxMessage.Start(Guid.NewGuid(), AccountLifecycleIntegrationEvent.EventType, 1, "ledger.account-lifecycle-v1", "account.lifecycle.v1", 0, 12, started, "hash-a", "corr-1", "worker-1");

        Assert.True(message.IsStale(started.AddMinutes(10), TimeSpan.FromMinutes(5)));

        message.RestartProcessing("account.lifecycle.v1.retry.1m", 1, 99, started.AddMinutes(10), "worker-2");

        Assert.Equal(InboxMessageStatus.Processing, message.Status);
        Assert.Equal(2, message.AttemptCount);
        Assert.Equal("worker-2", message.ProcessingInstanceId);
        Assert.Equal("account.lifecycle.v1.retry.1m", message.Topic);
        Assert.Equal(99, message.Offset);
    }
}
