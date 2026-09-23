using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Application;

public sealed record PaymentEvidence(Guid PaymentId, string PaymentReference, string Provider, string? ProviderReference, string ClientReference, decimal Amount, string Currency, string Status, Guid? LedgerTransactionId, DateTimeOffset SourceUpdatedAtUtc);
public sealed record LedgerEvidence(Guid TransactionId, string ExternalReference, string Status, decimal Amount, string Currency, DateTimeOffset ObservedAtUtc);
public sealed record ProviderEvidence(string Provider, string? ProviderReference, string ClientReference, string Status, decimal? Amount, string? Currency, string? ProviderClientReference, string? ResponseCode, DateTimeOffset ObservedAtUtc);
public sealed record SettlementEvidence(string ProviderReference, string ClientReference, decimal Amount, string Currency, string Status, DateOnly SettlementDate);
public sealed record MatchDecision(MatchStatus Status, ExceptionCode? Code, Severity? Severity, bool RequestPaymentRecovery, string Description);

public interface IReconciliationSources
{
    Task<PaymentEvidence?> FindPaymentAsync(string provider, string? providerReference, string? clientReference, CancellationToken cancellationToken);
    Task<PaymentEvidence?> GetPaymentAsync(Guid paymentId, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentEvidence>> PendingPaymentsAsync(int offset, int limit, CancellationToken cancellationToken);
    Task<LedgerEvidence?> FindLedgerAsync(PaymentEvidence payment, CancellationToken cancellationToken);
    Task<ProviderEvidence?> QueryProviderAsync(PaymentEvidence payment, CancellationToken cancellationToken);
    Task<IReadOnlyList<PaymentEvidence>> ExpectedSettlementsAsync(string provider, DateOnly settlementDate, CancellationToken cancellationToken);
    Task<PaymentEvidence?> RequestPaymentRecoveryAsync(Guid paymentId, CancellationToken cancellationToken);
}

public interface IProviderReconciliationPolicy
{
    bool IsSuccess(string provider, string status);
    bool IsFailure(string provider, string status);
    TimeSpan MissingReferenceGrace(string provider);
    TimeSpan SettlementDelay(string provider);
}

public static class ReconciliationMatcher
{
    public static MatchDecision Match(PaymentEvidence? payment, LedgerEvidence? ledger, ProviderEvidence? provider, SettlementEvidence? settlement, IProviderReconciliationPolicy policy)
    {
        if (payment is null)
            return new(MatchStatus.Pending, ExceptionCode.UNKNOWN_PROVIDER_REFERENCE, Severity.High, false, "No internal payment matched the provider or client reference.");

        var observedReference = settlement?.ProviderReference ?? provider?.ProviderReference;
        if (!string.IsNullOrWhiteSpace(observedReference) && !string.IsNullOrWhiteSpace(payment.ProviderReference) && !string.Equals(observedReference, payment.ProviderReference, StringComparison.Ordinal))
            return Conflict(ExceptionCode.REFERENCE_MISMATCH, Severity.High, "Provider references conflict.");
        var clientReference = settlement?.ClientReference ?? provider?.ClientReference;
        if (!string.IsNullOrWhiteSpace(clientReference) && !string.Equals(clientReference, payment.ClientReference, StringComparison.Ordinal))
            return Conflict(ExceptionCode.REFERENCE_MISMATCH, Severity.High, "Client references conflict.");

        if ((settlement is not null && settlement.Amount != payment.Amount) || (provider?.Amount is { } providerAmount && providerAmount != payment.Amount) || (ledger is not null && ledger.Amount != payment.Amount))
            return Conflict(ExceptionCode.AMOUNT_MISMATCH, Severity.Critical, "Amounts disagree across financial evidence.");
        if ((settlement is not null && !SameCurrency(settlement.Currency, payment.Currency)) || (provider?.Currency is { } providerCurrency && !SameCurrency(providerCurrency, payment.Currency)) || (ledger is not null && !SameCurrency(ledger.Currency, payment.Currency)))
            return Conflict(ExceptionCode.CURRENCY_MISMATCH, Severity.Critical, "Currencies disagree across financial evidence.");
        if (ledger is not null && (!string.Equals(ledger.ExternalReference, payment.PaymentId.ToString("D"), StringComparison.OrdinalIgnoreCase) || !string.Equals(ledger.Status, "Posted", StringComparison.OrdinalIgnoreCase)))
            return Conflict(ExceptionCode.REFERENCE_MISMATCH, Severity.Critical, "Ledger posting does not match this payment.");

        if (provider is not null && (provider.Amount is null || provider.Currency is null || provider.ProviderClientReference is null))
            return new(MatchStatus.Pending, null, null, false, "Provider status response lacks independently verified financial fields.");
        if (provider is not null && !string.Equals(provider.ProviderClientReference, payment.ClientReference, StringComparison.Ordinal))
            return Conflict(ExceptionCode.REFERENCE_MISMATCH, Severity.High, "Provider client reference conflicts with payment.");
        var providerStatus = settlement?.Status ?? provider?.Status;
        if (providerStatus is null)
            return new(MatchStatus.Pending, ExceptionCode.PROVIDER_RECORD_MISSING, Severity.Medium, false, "Provider final status is not yet available.");
        var success = policy.IsSuccess(payment.Provider, providerStatus);
        var failure = policy.IsFailure(payment.Provider, providerStatus);
        if (!success && !failure)
            return ledger is null
                ? new(MatchStatus.Pending, null, null, false, "Provider outcome remains pending.")
                : Conflict(ExceptionCode.STATUS_MISMATCH, Severity.High, "Ledger posted while provider outcome remains pending.");

        if (failure && ledger is not null) return Conflict(ExceptionCode.LEDGER_POSTED_PROVIDER_FAILED, Severity.Critical, "Provider failed but ledger is posted.");
        if (failure && payment.Status == "Completed") return Conflict(ExceptionCode.PAYMENT_COMPLETED_PROVIDER_FAILED, Severity.Critical, "Provider failed but payment completed.");
        if (success && payment.Status == "Failed") return Conflict(ExceptionCode.PAYMENT_FAILED_PROVIDER_SUCCESS, Severity.High, "Provider succeeded but payment failed.");
        if (success && ledger is null)
            return payment.Status is "PendingReconciliation" or "SubmittedToRail" or "Processing"
                ? new(MatchStatus.Pending, ExceptionCode.PROVIDER_SUCCESS_LEDGER_MISSING, Severity.High, true, "Provider succeeded; request owning Payment Service to recover ledger posting.")
                : Conflict(ExceptionCode.PROVIDER_SUCCESS_LEDGER_MISSING, Severity.Critical, "Provider succeeded and payment completed without a ledger posting.");
        if (failure && payment.Status is "PendingReconciliation" or "SubmittedToRail" or "Processing")
            return new(MatchStatus.Pending, null, null, true, "Provider definitively failed; request Payment Service recovery.");
        if (success && payment.Status == "Completed" && ledger is not null)
            return new(MatchStatus.Matched, null, null, false, "Payment, provider and ledger agree.");
        if (failure && payment.Status == "Failed" && ledger is null)
            return new(MatchStatus.Matched, null, null, false, "Provider failure and internal failure agree.");
        return Conflict(ExceptionCode.STATUS_MISMATCH, Severity.High, "Payment and provider statuses disagree.");
    }

    private static MatchDecision Conflict(ExceptionCode code, Severity severity, string description) => new(MatchStatus.Exception, code, severity, false, description);
    private static bool SameCurrency(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
}
