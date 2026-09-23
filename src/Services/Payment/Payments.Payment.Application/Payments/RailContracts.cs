using Payments.Payment.Domain.Payments;

namespace Payments.Payment.Application.Payments;

public sealed record RailTransferInstruction(
    Guid PaymentId,
    string ClientReference,
    string SourceAccountId,
    string DestinationBankCode,
    string DestinationAccountNumber,
    string DestinationAccountName,
    string DestinationCountryCode,
    decimal Amount,
    string Currency,
    string? Narration);

public sealed record RailRoute(string ProviderName, string Market, string Currency, string PaymentType, IPaymentRailAdapter Adapter);

public sealed record RailSubmissionResult(
    RailSubmissionOutcome Outcome,
    string? ProviderReference,
    string? ProviderResponseCode,
    string? ProviderMessage,
    DateTimeOffset SubmittedAtUtc,
    TimeSpan? RetryAfter,
    string? RawStatus,
    string? Error = null);

public sealed record RailStatusResult(
    RailSubmissionOutcome Outcome,
    string? ProviderReference,
    string? ProviderResponseCode,
    string? ProviderMessage,
    DateTimeOffset CheckedAtUtc,
    TimeSpan? RetryAfter,
    string? RawStatus,
    string? Error = null,
    decimal? ProviderAmount = null,
    string? ProviderCurrency = null,
    string? ProviderClientReference = null);

public sealed record RailCallbackPayload(
    string EventId,
    string ProviderReference,
    string ClientReference,
    string Status,
    decimal Amount,
    string Currency,
    string? ResponseCode,
    DateTimeOffset OccurredAtUtc);

public sealed record RailCallbackProcessResult(bool Accepted, bool Duplicate, string Status, string? Reason = null);

public sealed record RailCallbackAuthenticationInput(string Provider, string? Timestamp, string? Signature, string RawBody);

public interface IRailRouter
{
    Task<RailRoute> RouteAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default);
}

public interface IPaymentRailAdapter
{
    string ProviderName { get; }
    Task<RailSubmissionResult> SubmitTransferAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default);
    Task<RailStatusResult> GetTransferStatusAsync(string clientReference, string? providerReference, CancellationToken cancellationToken = default);
    Task<bool> ValidateDestinationAsync(PaymentDestinationRequest destination, CancellationToken cancellationToken = default);
}

public interface IRailCallbackAuthenticator
{
    bool Validate(RailCallbackAuthenticationInput input);
}

public interface IPaymentRailCallbackService
{
    Task<RailCallbackProcessResult> ProcessCallbackAsync(string provider, string rawBody, IReadOnlyDictionary<string, string?> headers, CancellationToken cancellationToken = default);
}