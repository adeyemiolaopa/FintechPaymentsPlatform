using Payments.RailSimulator.Domain;

namespace Payments.RailSimulator.Application;

public sealed record SubmitRailTransferRequest(string ClientReference, string DestinationBankCode, string DestinationAccountNumber, string DestinationAccountName, decimal Amount, string Currency, string? Narration = null, string SourceInstitution = "FINTECHPAY");
public sealed record RailTransferResponse(string ProviderReference, string ClientReference, string Status, string ResponseCode, string ResponseMessage, decimal Amount, string Currency, DateTimeOffset? ProcessedAtUtc = null, DateTimeOffset? CompletedAtUtc = null);
public sealed record RailStatusResponse(string ProviderReference, string ClientReference, string Status, decimal Amount, string Currency, DateTimeOffset ReceivedAtUtc, DateTimeOffset? ProcessedAtUtc, DateTimeOffset? CompletedAtUtc, string? FailureCode, string? FailureReason);
public sealed record ProviderScenarioRequest(string Mode, bool CallbackEnabled = true, int DuplicateCallbackCount = 1, int DelayMs = 100, int TimeoutDelayMs = 1500, string HealthState = "Available");
public sealed record ProviderScenarioResponse(string Mode, bool CallbackEnabled, int DuplicateCallbackCount, int DelayMs, int TimeoutDelayMs, string HealthState, DateTimeOffset UpdatedAtUtc);
public sealed record CallbackConfigurationRequest(string Url, string WebhookSecret);
public sealed record CallbackConfigurationResponse(string Url, DateTimeOffset UpdatedAtUtc);
public sealed record ProviderStatusResponse(string HealthState, string ScenarioMode, DateTimeOffset AsOfUtc);
public sealed record CallbackAttemptResponse(Guid Id, Guid RailTransferId, Guid CallbackEventId, int AttemptNumber, string TargetUrl, string Status, int? StatusCode, DateTimeOffset? StartedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? NextAttemptAtUtc, string? Error);
public sealed record SettlementRecordResponse(string ProviderReference, string ClientReference, decimal Amount, string Currency, string Status, DateOnly SettlementDate);
public sealed record NameEnquiryRequest(string BankCode, string AccountNumber, string? Scenario = null);
public sealed record NameEnquiryResponse(string BankCode, string AccountNumberMasked, string AccountName, string AccountStatus, string ResponseCode, string ResponseMessage);
public sealed record BankDirectoryResponse(string BankCode, string BankName, bool IsAvailable);
public sealed record RailRequestContext(string ClientId, RailScenarioMode? ScenarioOverride = null, bool AllowScenarioOverride = true);

public interface IRailSimulatorService
{
    Task<RailTransferResponse> SubmitAsync(SubmitRailTransferRequest request, RailRequestContext context, CancellationToken cancellationToken = default);
    Task<RailStatusResponse> GetByProviderReferenceAsync(string providerReference, CancellationToken cancellationToken = default);
    Task<RailStatusResponse> GetByClientReferenceAsync(string clientId, string clientReference, CancellationToken cancellationToken = default);
    Task<ProviderScenarioResponse> GetScenarioAsync(CancellationToken cancellationToken = default);
    Task<ProviderScenarioResponse> ConfigureScenarioAsync(ProviderScenarioRequest request, CancellationToken cancellationToken = default);
    Task ResetAsync(CancellationToken cancellationToken = default);
    Task<CallbackConfigurationResponse> ConfigureCallbackAsync(CallbackConfigurationRequest request, CancellationToken cancellationToken = default);
    Task<ProviderStatusResponse> GetProviderStatusAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<CallbackAttemptResponse>> GetCallbackAttemptsAsync(string providerReference, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<SettlementRecordResponse>> GenerateSettlementAsync(DateOnly settlementDate, string anomaly = "normal", CancellationToken cancellationToken = default);
    Task<NameEnquiryResponse> NameEnquiryAsync(NameEnquiryRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<BankDirectoryResponse>> GetBanksAsync(CancellationToken cancellationToken = default);
}

public interface IRailRequestAuthenticator
{
    Task<RailRequestContext> AuthenticateAsync(RailAuthenticationInput input, CancellationToken cancellationToken = default);
    string SignCallback(string secret, string timestamp, string rawBody);
}

public sealed record RailAuthenticationInput(string? ClientId, string? ApiKey, string? Timestamp, string? Signature, string Method, string Path, string RawBody, string? ScenarioOverride);

public sealed class RailSimulatorException : Exception
{
    public RailSimulatorException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }

    public string Code { get; }
    public int StatusCode { get; }
}

public static class RailResponseCodes
{
    public const string Successful = "00";
    public const string Pending = "01";
    public const string Declined = "05";
    public const string Failed = "51";
    public const string ProviderUnavailable = "91";
    public const string SystemMalfunction = "96";
    public const string Unknown = "ZX99";

    public static (string Code, string Message) ForStatus(RailTransferStatus status) => status switch
    {
        RailTransferStatus.Successful => (Successful, "Successful"),
        RailTransferStatus.Processing or RailTransferStatus.Received => (Pending, "Processing"),
        RailTransferStatus.Failed => (Failed, "Transaction failed"),
        RailTransferStatus.Unknown => (Unknown, "Unknown"),
        RailTransferStatus.Reversed => (Successful, "Reversed"),
        _ => (SystemMalfunction, "System malfunction"),
    };
}
