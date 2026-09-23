namespace Payments.Reconciliation.Domain;

public enum ReconciliationOutboxStatus { Pending, Published, Failed }

public sealed class ReconciliationOutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid EventId { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = "";
    public int EventVersion { get; set; } = 1;
    public string Topic { get; set; } = "reconciliation.lifecycle.v1";
    public string PartitionKey { get; set; } = "";
    public string Payload { get; set; } = "{}";
    public ReconciliationOutboxStatus Status { get; set; } = ReconciliationOutboxStatus.Pending;
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? NextAttemptAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? LastError { get; set; }
}
