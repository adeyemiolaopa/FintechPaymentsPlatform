namespace Payments.Reconciliation.Domain;

public sealed class ActiveReconciliationJob
{
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public DateTimeOffset NextReconciliationAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastCheckedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTimeOffset? LeaseUntilUtc { get; set; }
}
