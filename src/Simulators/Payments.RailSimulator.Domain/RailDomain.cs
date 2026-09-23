using System.Security.Cryptography;
using System.Text;

namespace Payments.RailSimulator.Domain;

public enum RailTransferStatus { Received = 0, Processing = 1, Successful = 2, Failed = 3, Unknown = 4, Reversed = 5 }
public enum RailScenarioMode { Success = 0, Failure = 1, PendingThenSuccess = 2, PendingThenFailure = 3, TimeoutBeforeProcessing = 4, TimeoutAfterProcessing = 5, Http500BeforeProcessing = 6, Http500AfterProcessing = 7, Throttled = 8, Outage = 9, DuplicateCallback = 10, NoCallback = 11, MalformedResponse = 12, InconsistentStatus = 13 }
public enum CallbackAttemptStatus { Pending = 0, Succeeded = 1, Failed = 2, Abandoned = 3 }
public enum ProviderHealthState { Available = 0, Degraded = 1, Unavailable = 2 }

public sealed class RailTransfer
{
    private RailTransfer() { }

    private RailTransfer(Guid id, string providerReference, string clientId, string clientReference, string sourceInstitution, string destinationBankCode, string destinationAccountNumber, string destinationAccountName, decimal amount, string currency, string? narration, RailScenarioMode scenario, string requestHash, DateTimeOffset now)
    {
        Id = id;
        ProviderReference = providerReference;
        ClientId = Normalize(clientId, 80);
        ClientReference = Normalize(clientReference, 128);
        SourceInstitution = Normalize(sourceInstitution, 32);
        DestinationBankCode = Normalize(destinationBankCode, 12);
        DestinationAccountNumberMasked = MaskAccount(destinationAccountNumber);
        DestinationAccountName = Normalize(destinationAccountName, 160);
        Amount = amount <= 0m ? throw new RailDomainException("rail.amount", "Amount must be greater than zero.") : decimal.Round(amount, 4);
        Currency = NormalizeCurrency(currency);
        Narration = string.IsNullOrWhiteSpace(narration) ? null : narration.Trim()[..Math.Min(narration.Trim().Length, 240)];
        Status = RailTransferStatus.Received;
        ReceivedAtUtc = now;
        ConfiguredScenario = scenario;
        RequestHash = requestHash;
        CallbackEventId = Guid.NewGuid();
    }

    public Guid Id { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public string ClientId { get; private set; } = string.Empty;
    public string ClientReference { get; private set; } = string.Empty;
    public string SourceInstitution { get; private set; } = string.Empty;
    public string DestinationBankCode { get; private set; } = string.Empty;
    public string DestinationAccountNumberMasked { get; private set; } = string.Empty;
    public string DestinationAccountName { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string? Narration { get; private set; }
    public RailTransferStatus Status { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? FailureCode { get; private set; }
    public string? FailureReason { get; private set; }
    public RailScenarioMode ConfiguredScenario { get; private set; }
    public string RequestHash { get; private set; } = string.Empty;
    public Guid CallbackEventId { get; private set; }
    public bool CallbackEnabled { get; private set; } = true;
    public int DuplicateCallbackCount { get; private set; } = 1;
    public DateTimeOffset? NextProcessingAtUtc { get; private set; }
    public DateTimeOffset? CallbackScheduledAtUtc { get; private set; }
    public int StatusQueryCount { get; private set; }
    public int Version { get; private set; }

    public static RailTransfer Create(string providerReference, string clientId, string clientReference, string sourceInstitution, string destinationBankCode, string destinationAccountNumber, string destinationAccountName, decimal amount, string currency, string? narration, RailScenarioMode scenario, string requestHash, DateTimeOffset now)
        => new(Guid.NewGuid(), providerReference, clientId, clientReference, sourceInstitution, destinationBankCode, destinationAccountNumber, destinationAccountName, amount, currency, narration, scenario, requestHash, now);

    public void MarkProcessing(DateTimeOffset now, DateTimeOffset? nextProcessingAtUtc = null)
    {
        if (Status is RailTransferStatus.Successful or RailTransferStatus.Failed or RailTransferStatus.Reversed) return;
        Status = RailTransferStatus.Processing;
        ProcessedAtUtc ??= now;
        NextProcessingAtUtc = nextProcessingAtUtc;
        Touch();
    }

    public void MarkSuccessful(DateTimeOffset now)
    {
        Status = RailTransferStatus.Successful;
        ProcessedAtUtc ??= now;
        CompletedAtUtc = now;
        FailureCode = null;
        FailureReason = null;
        NextProcessingAtUtc = null;
        Touch();
    }

    public void MarkFailed(string code, string reason, DateTimeOffset now)
    {
        Status = RailTransferStatus.Failed;
        ProcessedAtUtc ??= now;
        CompletedAtUtc = now;
        FailureCode = Normalize(code, 16);
        FailureReason = Normalize(reason, 240);
        NextProcessingAtUtc = null;
        Touch();
    }

    public void DisableCallbacks() { CallbackEnabled = false; Touch(); }
    public void ConfigureCallbacks(int duplicateCount, DateTimeOffset? scheduledAtUtc)
    {
        DuplicateCallbackCount = Math.Clamp(duplicateCount, 1, 20);
        CallbackScheduledAtUtc = scheduledAtUtc;
        Touch();
    }

    public void MarkCallbackScheduled(DateTimeOffset now)
    {
        CallbackScheduledAtUtc = now;
        Touch();
    }

    public int IncrementStatusQueryCount()
    {
        StatusQueryCount++;
        Touch();
        return StatusQueryCount;
    }

    public bool SameFinancialInstruction(string requestHash) => string.Equals(RequestHash, requestHash, StringComparison.Ordinal);

    private void Touch() => Version++;

    private static string Normalize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new RailDomainException("rail.required", "Required rail field was empty.");
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string NormalizeCurrency(string value)
    {
        var normalized = Normalize(value, 3).ToUpperInvariant();
        if (normalized.Length != 3 || !normalized.All(char.IsLetter)) throw new RailDomainException("rail.currency", "Currency must be a 3-letter code.");
        return normalized;
    }

    private static string MaskAccount(string accountNumber)
    {
        var trimmed = Normalize(accountNumber, 32);
        return trimmed.Length <= 4 ? "****" : $"******{trimmed[^4..]}";
    }
}

public sealed class CallbackAttempt
{
    private CallbackAttempt() { }
    private CallbackAttempt(Guid railTransferId, Guid callbackEventId, int attemptNumber, string targetUrl, DateTimeOffset nextAttemptAtUtc, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        RailTransferId = railTransferId;
        CallbackEventId = callbackEventId;
        AttemptNumber = attemptNumber;
        TargetUrl = targetUrl;
        Status = CallbackAttemptStatus.Pending;
        NextAttemptAtUtc = nextAttemptAtUtc;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid RailTransferId { get; private set; }
    public Guid CallbackEventId { get; private set; }
    public int AttemptNumber { get; private set; }
    public string TargetUrl { get; private set; } = string.Empty;
    public CallbackAttemptStatus Status { get; private set; }
    public int? StatusCode { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public string? Error { get; private set; }

    public static CallbackAttempt Schedule(Guid railTransferId, Guid callbackEventId, int attemptNumber, string targetUrl, DateTimeOffset nextAttemptAtUtc, DateTimeOffset now) => new(railTransferId, callbackEventId, attemptNumber, targetUrl, nextAttemptAtUtc, now);
    public void Start(DateTimeOffset now) => StartedAtUtc = now;
    public void MarkSucceeded(int statusCode, DateTimeOffset now) { Status = CallbackAttemptStatus.Succeeded; StatusCode = statusCode; CompletedAtUtc = now; NextAttemptAtUtc = null; Error = null; }
    public void MarkFailed(int? statusCode, string error, DateTimeOffset now, DateTimeOffset? retryAtUtc) { Status = retryAtUtc is null ? CallbackAttemptStatus.Abandoned : CallbackAttemptStatus.Failed; StatusCode = statusCode; Error = error.Length > 1024 ? error[..1024] : error; CompletedAtUtc = now; NextAttemptAtUtc = retryAtUtc; }
}

public sealed class ProviderScenario
{
    private ProviderScenario() { }
    private ProviderScenario(DateTimeOffset now)
    {
        Id = Guid.Parse("10000000-0000-0000-0000-000000000001");
        Mode = RailScenarioMode.Success;
        CallbackEnabled = true;
        DuplicateCallbackCount = 1;
        DelayMs = 100;
        TimeoutDelayMs = 1500;
        MaxCallbackAttempts = 4;
        CallbackRetryBaseMs = 250;
        HealthState = ProviderHealthState.Available;
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public RailScenarioMode Mode { get; private set; }
    public bool CallbackEnabled { get; private set; }
    public int DuplicateCallbackCount { get; private set; }
    public int DelayMs { get; private set; }
    public int TimeoutDelayMs { get; private set; }
    public int MaxCallbackAttempts { get; private set; }
    public int CallbackRetryBaseMs { get; private set; }
    public ProviderHealthState HealthState { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static ProviderScenario Default(DateTimeOffset now) => new(now);
    public void Configure(RailScenarioMode mode, bool callbackEnabled, int duplicateCallbackCount, int delayMs, int timeoutDelayMs, ProviderHealthState healthState, DateTimeOffset now)
    {
        Mode = mode;
        CallbackEnabled = callbackEnabled;
        DuplicateCallbackCount = Math.Clamp(duplicateCallbackCount, 1, 20);
        DelayMs = Math.Clamp(delayMs, 0, 120_000);
        TimeoutDelayMs = Math.Clamp(timeoutDelayMs, 1, 120_000);
        HealthState = healthState;
        UpdatedAtUtc = now;
    }
}

public sealed class CallbackConfiguration
{
    private CallbackConfiguration() { }
    private CallbackConfiguration(DateTimeOffset now)
    {
        Id = Guid.Parse("20000000-0000-0000-0000-000000000001");
        Url = string.Empty;
        WebhookSecret = "local-webhook-secret";
        UpdatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Url { get; private set; } = string.Empty;
    public string WebhookSecret { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static CallbackConfiguration Default(DateTimeOffset now) => new(now);
    public void Configure(string url, string secret, DateTimeOffset now)
    {
        if (!string.IsNullOrWhiteSpace(url) && !Uri.TryCreate(url, UriKind.Absolute, out _)) throw new RailDomainException("rail.callback_url", "Callback URL must be absolute.");
        Url = url.Trim();
        WebhookSecret = string.IsNullOrWhiteSpace(secret) ? WebhookSecret : secret.Trim();
        UpdatedAtUtc = now;
    }
}

public sealed class SettlementRecord
{
    private SettlementRecord() { }
    private SettlementRecord(RailTransfer transfer, DateOnly settlementDate)
    {
        Id = Guid.NewGuid();
        ProviderReference = transfer.ProviderReference;
        ClientReference = transfer.ClientReference;
        Amount = transfer.Amount;
        Currency = transfer.Currency;
        Status = transfer.Status.ToString();
        SettlementDate = settlementDate;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    public Guid Id { get; private set; }
    public string ProviderReference { get; private set; } = string.Empty;
    public string ClientReference { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public DateOnly SettlementDate { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static SettlementRecord FromTransfer(RailTransfer transfer, DateOnly settlementDate) => new(transfer, settlementDate);
}

public sealed class ProviderClient
{
    private ProviderClient() { }
    private ProviderClient(string clientId, string apiKey, string secret, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        ClientId = clientId;
        ApiKey = apiKey;
        Secret = secret;
        IsActive = true;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string ClientId { get; private set; } = string.Empty;
    public string ApiKey { get; private set; } = string.Empty;
    public string Secret { get; private set; } = string.Empty;
    public bool IsActive { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static ProviderClient Create(string clientId, string apiKey, string secret, DateTimeOffset now) => new(clientId, apiKey, secret, now);
}

public sealed class RequestAuditEvent
{
    private RequestAuditEvent() { }
    private RequestAuditEvent(string eventType, string? providerReference, string? clientReference, string message, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        ProviderReference = providerReference;
        ClientReference = clientReference;
        Message = message;
        OccurredAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public string? ClientReference { get; private set; }
    public string Message { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }

    public static RequestAuditEvent Create(string eventType, string? providerReference, string? clientReference, string message, DateTimeOffset now) => new(eventType, providerReference, clientReference, message, now);
}

public sealed class RailDomainException : Exception
{
    public RailDomainException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public static class RailProviderReference
{
    public static string Generate(DateTimeOffset now)
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return $"RAIL-NG-{now:yyyyMMdd}-{Convert.ToHexString(bytes)}";
    }
}

public static class RailRequestHasher
{
    public static string Compute(string clientReference, string destinationBankCode, string destinationAccountNumber, string destinationAccountName, decimal amount, string currency, string? narration)
    {
        var canonical = string.Join("|", clientReference.Trim(), destinationBankCode.Trim(), destinationAccountNumber.Trim(), destinationAccountName.Trim(), decimal.Round(amount, 4).ToString("0.0000"), currency.Trim().ToUpperInvariant(), narration?.Trim() ?? string.Empty);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
