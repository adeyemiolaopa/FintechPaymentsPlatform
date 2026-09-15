namespace Payments.Payment.Infrastructure.Persistence;

public enum IdempotencyRecordStatus { Processing = 0, Completed = 1, Failed = 2 }

public sealed class IdempotencyRecord
{
    private IdempotencyRecord() { }

    private IdempotencyRecord(Guid customerId, string operationType, string idempotencyKey, string requestHash, DateTimeOffset now, DateTimeOffset expiresAtUtc)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        OperationType = operationType;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        Status = IdempotencyRecordStatus.Processing;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        ProcessingStartedAtUtc = now;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public string OperationType { get; private set; } = string.Empty;
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public IdempotencyRecordStatus Status { get; private set; }
    public string? ResourceType { get; private set; }
    public Guid? ResourceId { get; private set; }
    public int? ResponseStatusCode { get; private set; }
    public string? ResponseBody { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset ProcessingStartedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? FailedAtUtc { get; private set; }

    public bool IsCompleted => Status == IdempotencyRecordStatus.Completed;
    public bool IsProcessing => Status == IdempotencyRecordStatus.Processing;

    public static IdempotencyRecord Create(Guid customerId, string operationType, string idempotencyKey, string requestHash, DateTimeOffset now, DateTimeOffset expiresAtUtc)
        => new(customerId, NormalizeOperation(operationType), idempotencyKey, requestHash, now, expiresAtUtc);

    public void AttachResource(string resourceType, Guid resourceId, DateTimeOffset now)
    {
        ResourceType = NormalizeOperation(resourceType);
        ResourceId = resourceId;
        Touch(now);
    }

    public void Complete(int responseStatusCode, string responseBody, DateTimeOffset now)
    {
        Status = IdempotencyRecordStatus.Completed;
        ResponseStatusCode = responseStatusCode;
        ResponseBody = responseBody;
        CompletedAtUtc = now;
        FailedAtUtc = null;
        Touch(now);
    }

    public void MarkFailed(DateTimeOffset now)
    {
        Status = IdempotencyRecordStatus.Failed;
        FailedAtUtc = now;
        Touch(now);
    }

    public void PruneResponse(DateTimeOffset now)
    {
        ResponseBody = null;
        Touch(now);
    }

    private void Touch(DateTimeOffset now) => UpdatedAtUtc = now;

    private static string NormalizeOperation(string value) => string.IsNullOrWhiteSpace(value) ? throw new ArgumentException("Operation value is required.", nameof(value)) : value.Trim();
}
