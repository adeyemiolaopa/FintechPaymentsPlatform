using FluentAssertions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Ledger.Domain.Ledger;

namespace Payments.Ledger.UnitTests;

public sealed class LedgerDomainTests
{
    [Fact]
    public void Balanced_transaction_can_be_posted()
    {
        var currency = Currency.FromCode("NGN");
        var debitAccount = Guid.NewGuid();
        var creditAccount = Guid.NewGuid();

        var transaction = LedgerTransaction.Post("DEP-1", "WalletFunding", currency, "Wallet funding", [new PostingDraft(debitAccount, EntrySide.Debit, Money.Of(100_000m, currency), null, 1), new PostingDraft(creditAccount, EntrySide.Credit, Money.Of(100_000m, currency), null, 2)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        transaction.Postings.Should().HaveCount(2);
        transaction.Postings.Sum(posting => posting.Side == EntrySide.Debit ? posting.Amount : 0m).Should().Be(100_000m);
        transaction.Postings.Sum(posting => posting.Side == EntrySide.Credit ? posting.Amount : 0m).Should().Be(100_000m);
    }

    [Fact]
    public void Unbalanced_transaction_is_rejected()
    {
        var currency = Currency.FromCode("NGN");

        var act = () => LedgerTransaction.Post("BAD-1", "WalletFunding", currency, "Bad funding", [new PostingDraft(Guid.NewGuid(), EntrySide.Debit, Money.Of(100_000m, currency), null, 1), new PostingDraft(Guid.NewGuid(), EntrySide.Credit, Money.Of(90_000m, currency), null, 2)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        act.Should().Throw<DomainException>().WithMessage("*debits must equal credits*");
    }

    [Fact]
    public void Mixed_currency_transaction_is_rejected()
    {
        var ngn = Currency.FromCode("NGN");
        var usd = Currency.FromCode("USD");

        var act = () => LedgerTransaction.Post("FX-1", "MixedCurrency", ngn, "Mixed", [new PostingDraft(Guid.NewGuid(), EntrySide.Debit, Money.Of(10m, ngn), null, 1), new PostingDraft(Guid.NewGuid(), EntrySide.Credit, Money.Of(10m, usd), null, 2)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        act.Should().Throw<DomainException>().WithMessage("*currency*");
    }

    [Fact]
    public void Multi_leg_transaction_can_balance()
    {
        var currency = Currency.FromCode("NGN");
        var transaction = LedgerTransaction.Post("FEE-1", "PaymentWithFee", currency, "Payment with fee", [new PostingDraft(Guid.NewGuid(), EntrySide.Debit, Money.Of(101_000m, currency), null, 1), new PostingDraft(Guid.NewGuid(), EntrySide.Credit, Money.Of(100_000m, currency), null, 2), new PostingDraft(Guid.NewGuid(), EntrySide.Credit, Money.Of(1_000m, currency), null, 3)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        transaction.Postings.Should().HaveCount(3);
    }

    [Fact]
    public void Reversal_creates_inverse_postings_without_mutating_original()
    {
        var currency = Currency.FromCode("NGN");
        var asset = Guid.NewGuid();
        var liability = Guid.NewGuid();
        var original = LedgerTransaction.Post("DEP-2", "WalletFunding", currency, "Wallet funding", [new PostingDraft(asset, EntrySide.Debit, Money.Of(50_000m, currency), null, 1), new PostingDraft(liability, EntrySide.Credit, Money.Of(50_000m, currency), null, 2)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        var reversal = original.CreateReversal("REV-DEP-2", "Customer refund", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);

        original.OriginalTransactionId.Should().BeNull();
        reversal.OriginalTransactionId.Should().Be(original.Id);
        reversal.Postings.Single(posting => posting.LedgerAccountId == asset).Side.Should().Be(EntrySide.Credit);
        reversal.Postings.Single(posting => posting.LedgerAccountId == liability).Side.Should().Be(EntrySide.Debit);
    }

    [Theory]
    [InlineData(LedgerAccountType.Asset, 100, 25, 75)]
    [InlineData(LedgerAccountType.Expense, 100, 25, 75)]
    [InlineData(LedgerAccountType.Liability, 100, 25, -75)]
    [InlineData(LedgerAccountType.Revenue, 100, 25, -75)]
    public void Balance_uses_normal_balance_by_account_type(LedgerAccountType type, decimal debit, decimal credit, decimal expected)
    {
        var currency = Currency.FromCode("NGN");
        var balance = LedgerAccountBalance.Create(Guid.NewGuid(), currency, DateTimeOffset.UtcNow);
        balance.Apply(Post(Guid.NewGuid(), EntrySide.Debit, debit, currency), DateTimeOffset.UtcNow);
        balance.Apply(Post(Guid.NewGuid(), EntrySide.Credit, credit, currency), DateTimeOffset.UtcNow);

        balance.CalculateBalance(type).Should().Be(expected);
    }

    private static Posting Post(Guid accountId, EntrySide side, decimal amount, Currency currency)
    {
        var transaction = LedgerTransaction.Post(Guid.NewGuid().ToString("N"), "Test", currency, "Test", [new PostingDraft(accountId, side, Money.Of(amount, currency), null, 1), new PostingDraft(Guid.NewGuid(), side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit, Money.Of(amount, currency), null, 2)], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null);
        return transaction.Postings.First(posting => posting.LedgerAccountId == accountId);
    }
}
