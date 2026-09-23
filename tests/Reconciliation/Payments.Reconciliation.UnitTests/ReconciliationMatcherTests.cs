using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.UnitTests;

public sealed class ReconciliationMatcherTests
{
    private static readonly SimulatorReconciliationPolicy Policy = new();
    private static readonly Guid PaymentId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static PaymentEvidence Payment(string status = "Completed", decimal amount = 50000m, string currency = "NGN", Guid? ledgerId = null)
        => new(PaymentId, "PAY-001", "SimulatorRail", "RAIL-001", PaymentId.ToString("D"), amount, currency, status, ledgerId, DateTimeOffset.UtcNow);
    private static LedgerEvidence Ledger(decimal amount = 50000m, string currency = "NGN")
        => new(Guid.NewGuid(), PaymentId.ToString("D"), "Posted", amount, currency, DateTimeOffset.UtcNow);
    private static SettlementEvidence Settlement(string status = "SUCCESS", decimal amount = 50000m, string currency = "NGN")
        => new("RAIL-001", PaymentId.ToString("D"), amount, currency, status, new DateOnly(2026, 9, 17));

    [Fact] public void Perfect_match_requires_provider_payment_and_ledger_agreement()
        => Assert.Equal(MatchStatus.Matched, ReconciliationMatcher.Match(Payment(ledgerId: Guid.NewGuid()), Ledger(), null, Settlement(), Policy).Status);

    [Fact] public void Amount_mismatch_is_critical_and_never_auto_resolves()
    {
        var result = ReconciliationMatcher.Match(Payment(ledgerId: Guid.NewGuid()), Ledger(), null, Settlement(amount: 55000m), Policy);
        Assert.Equal(ExceptionCode.AMOUNT_MISMATCH, result.Code);
        Assert.Equal(Severity.Critical, result.Severity);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Currency_mismatch_is_critical_and_never_auto_resolves()
    {
        var result = ReconciliationMatcher.Match(Payment(ledgerId: Guid.NewGuid()), Ledger(), null, Settlement(currency: "USD"), Policy);
        Assert.Equal(ExceptionCode.CURRENCY_MISMATCH, result.Code);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Provider_success_payment_failed_is_high_severity()
    {
        var result = ReconciliationMatcher.Match(Payment("Failed"), null, null, Settlement(), Policy);
        Assert.Equal(ExceptionCode.PAYMENT_FAILED_PROVIDER_SUCCESS, result.Code);
        Assert.Equal(Severity.High, result.Severity);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Provider_failed_payment_completed_is_critical()
    {
        var result = ReconciliationMatcher.Match(Payment("Completed"), null, null, Settlement("FAILED"), Policy);
        Assert.Equal(ExceptionCode.PAYMENT_COMPLETED_PROVIDER_FAILED, result.Code);
        Assert.Equal(Severity.Critical, result.Severity);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Ledger_posted_provider_failed_is_critical()
    {
        var result = ReconciliationMatcher.Match(Payment("Completed", ledgerId: Guid.NewGuid()), Ledger(), null, Settlement("FAILED"), Policy);
        Assert.Equal(ExceptionCode.LEDGER_POSTED_PROVIDER_FAILED, result.Code);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Provider_success_pending_payment_without_ledger_requests_owner_recovery()
    {
        var result = ReconciliationMatcher.Match(Payment("PendingReconciliation"), null, null, Settlement(), Policy);
        Assert.Equal(ExceptionCode.PROVIDER_SUCCESS_LEDGER_MISSING, result.Code);
        Assert.True(result.RequestPaymentRecovery);
    }

    [Fact] public void Missing_independent_provider_financial_evidence_stays_pending()
    {
        var provider = new ProviderEvidence("SimulatorRail", "RAIL-001", PaymentId.ToString("D"), "Successful", null, null, null, "00", DateTimeOffset.UtcNow);
        var result = ReconciliationMatcher.Match(Payment("PendingReconciliation"), null, provider, null, Policy);
        Assert.Equal(MatchStatus.Pending, result.Status);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Provider_amount_mismatch_never_requests_recovery()
    {
        var provider = new ProviderEvidence("SimulatorRail", "RAIL-001", PaymentId.ToString("D"), "Successful", 55000m, "NGN", PaymentId.ToString("D"), "00", DateTimeOffset.UtcNow);
        var result = ReconciliationMatcher.Match(Payment("PendingReconciliation"), null, provider, null, Policy);
        Assert.Equal(ExceptionCode.AMOUNT_MISMATCH, result.Code);
        Assert.False(result.RequestPaymentRecovery);
    }

    [Fact] public void Unknown_reference_is_not_auto_resolved()
    {
        var result = ReconciliationMatcher.Match(null, null, null, Settlement(), Policy);
        Assert.Equal(ExceptionCode.UNKNOWN_PROVIDER_REFERENCE, result.Code);
        Assert.False(result.RequestPaymentRecovery);
    }
}
