namespace Payments.Reconciliation.Domain;

public enum ReconciliationMode { ProviderStatus, SettlementFile, InternalConsistency, Manual }
public enum RunStatus { Running, Completed, CompletedWithExceptions, Failed }
public enum FileStatus { Received, Validating, Processing, Completed, CompletedWithExceptions, Rejected, Failed }
public enum MatchStatus { Matched, Resolved, Exception, Pending, Ignored }
public enum ExceptionStatus { Open, Investigating, Resolved, Ignored }
public enum Severity { Low, Medium, High, Critical }
public enum ExceptionCode
{
    INTERNAL_PAYMENT_MISSING, PROVIDER_RECORD_MISSING, LEDGER_RECORD_MISSING, SETTLEMENT_RECORD_MISSING,
    AMOUNT_MISMATCH, CURRENCY_MISMATCH, STATUS_MISMATCH, REFERENCE_MISMATCH,
    DUPLICATE_PROVIDER_RECORD, DUPLICATE_INTERNAL_RECORD, UNKNOWN_PROVIDER_REFERENCE,
    PAYMENT_COMPLETED_PROVIDER_FAILED, PAYMENT_FAILED_PROVIDER_SUCCESS,
    LEDGER_POSTED_PROVIDER_FAILED, PROVIDER_SUCCESS_LEDGER_MISSING, UNMATCHED_SETTLEMENT_RECORD
}

public sealed class SettlementFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public string FileName { get; set; } = "";
    public string FileHash { get; set; } = "";
    public DateOnly SettlementDate { get; set; }
    public DateTimeOffset ReceivedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ProcessedAtUtc { get; set; }
    public FileStatus Status { get; set; } = FileStatus.Received;
    public long LastProcessedLine { get; set; }
    public long RecordCount { get; set; }
    public long ProcessedCount { get; set; }
    public long MatchedCount { get; set; }
    public long ExceptionCount { get; set; }
    public string? RejectionReason { get; set; }
    public string UploadedBy { get; set; } = "";
    public DateTimeOffset? LeaseUntilUtc { get; set; }
    public string? LeaseOwner { get; set; }
}

public sealed class SettlementRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid SettlementFileId { get; set; }
    public string ProviderReference { get; set; } = "";
    public string ClientReference { get; set; } = "";
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "";
    public string ProviderStatus { get; set; } = "";
    public DateOnly SettlementDate { get; set; }
    public long SourceLineNumber { get; set; }
    public string RawRecordHash { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ReconciliationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Provider { get; set; } = "";
    public ReconciliationMode Mode { get; set; }
    public Guid? SettlementFileId { get; set; }
    public DateOnly? SettlementDate { get; set; }
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Running;
    public long RecordsProcessed { get; set; }
    public long Matched { get; set; }
    public long Exceptions { get; set; }
    public long Resolved { get; set; }
    public long Failed { get; set; }
}

public sealed class ReconciliationMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid RunId { get; set; }
    public Guid? PaymentId { get; set; }
    public string? PaymentReference { get; set; }
    public string? ProviderReference { get; set; }
    public Guid? LedgerTransactionId { get; set; }
    public Guid? SettlementRecordId { get; set; }
    public MatchStatus Status { get; set; }
    public ExceptionCode? ExceptionCode { get; set; }
    public DateTimeOffset DetectedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvedAtUtc { get; set; }
    public string? ResolutionType { get; set; }
}

public sealed class ReconciliationException
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderReference { get; set; }
    public string? PaymentReference { get; set; }
    public ExceptionCode Code { get; set; }
    public Severity Severity { get; set; }
    public ExceptionStatus Status { get; set; } = ExceptionStatus.Open;
    public string Description { get; set; } = "";
    public string EvidenceJson { get; set; } = "{}";
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string? AssignedTo { get; set; }
    public DateTimeOffset? AssignedAtUtc { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

public sealed class ReconciliationAuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? RunId { get; set; }
    public Guid? SettlementFileId { get; set; }
    public Guid? ExceptionId { get; set; }
    public string Action { get; set; } = "";
    public string Actor { get; set; } = "";
    public string? ReasonCode { get; set; }
    public string? Comment { get; set; }
    public string? DownstreamCommand { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ProviderStatusObservation
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid PaymentId { get; set; }
    public string Provider { get; set; } = "";
    public string? ProviderReference { get; set; }
    public string ObservedStatus { get; set; } = "";
    public string? ResponseCode { get; set; }
    public DateTimeOffset ObservedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
