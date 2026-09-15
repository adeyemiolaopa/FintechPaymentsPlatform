using FluentAssertions;
using Payments.Account.Domain.Accounts;
using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Account.UnitTests;

public sealed class AccountDomainTests
{
    [Fact]
    public void Money_rejects_cross_currency_arithmetic()
    {
        var ngn = Money.Of(100m, Currency.FromCode("NGN"));
        var kes = Money.Of(50m, Currency.FromCode("KES"));

        var act = () => ngn.Add(kes);

        act.Should().Throw<DomainException>().Where(exception => exception.Code == "money.currency_mismatch");
    }

    [Fact]
    public void Currency_rejects_over_precision()
    {
        var act = () => Money.Of(10.123m, Currency.FromCode("NGN"));

        act.Should().Throw<DomainException>().Where(exception => exception.Code == "money.precision");
    }

    [Fact]
    public void Account_available_balance_is_derived_from_ledger_less_reserved()
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account.Domain.Accounts.Account.OpenWallet(Guid.NewGuid(), AccountNumber.Generate(), Currency.FromCode("NGN"), now);
        account.SeedLedgerBalanceForTest(Money.Of(100_000m, Currency.FromCode("NGN")), now);

        account.ReserveFunds("order-1", Money.Of(30_000m, Currency.FromCode("NGN")), now, now.AddMinutes(10), debitBlocked: false);

        account.LedgerBalance.Should().Be(100_000m);
        account.ReservedBalance.Should().Be(30_000m);
        account.AvailableBalance.Should().Be(70_000m);
    }

    [Fact]
    public void Frozen_account_cannot_reserve()
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account.Domain.Accounts.Account.OpenWallet(Guid.NewGuid(), AccountNumber.Generate(), Currency.FromCode("NGN"), now);
        account.SeedLedgerBalanceForTest(Money.Of(100_000m, Currency.FromCode("NGN")), now);
        account.Freeze(Guid.NewGuid(), "Risk review", now);

        var act = () => account.ReserveFunds("order-1", Money.Of(1_000m, Currency.FromCode("NGN")), now, now.AddMinutes(5), debitBlocked: false);

        act.Should().Throw<DomainException>().Where(exception => exception.Code == "account.frozen");
    }

    [Fact]
    public void Debit_blocked_account_cannot_reserve()
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account.Domain.Accounts.Account.OpenWallet(Guid.NewGuid(), AccountNumber.Generate(), Currency.FromCode("NGN"), now);
        account.SeedLedgerBalanceForTest(Money.Of(100_000m, Currency.FromCode("NGN")), now);

        var act = () => account.ReserveFunds("order-1", Money.Of(1_000m, Currency.FromCode("NGN")), now, now.AddMinutes(5), debitBlocked: true);

        act.Should().Throw<DomainException>().Where(exception => exception.Code == "account.debit_blocked");
    }

    [Fact]
    public void Account_with_balance_cannot_close()
    {
        var now = DateTimeOffset.UtcNow;
        var account = Account.Domain.Accounts.Account.OpenWallet(Guid.NewGuid(), AccountNumber.Generate(), Currency.FromCode("NGN"), now);
        account.SeedLedgerBalanceForTest(Money.Of(100m, Currency.FromCode("NGN")), now);

        var act = () => account.Close(Guid.NewGuid(), now);

        act.Should().Throw<DomainException>().Where(exception => exception.Code == "account.close_balance");
    }

    [Fact]
    public void Beneficiary_removal_is_soft()
    {
        var beneficiary = Beneficiary.Create(Guid.NewGuid(), BeneficiaryType.ExternalBank, "John Doe", "058", "0123456789", Currency.FromCode("NGN"), "NG", null, DateTimeOffset.UtcNow);

        beneficiary.Remove(DateTimeOffset.UtcNow.AddMinutes(1));

        beneficiary.Status.Should().Be(BeneficiaryStatus.Removed);
        beneficiary.RemovedAtUtc.Should().NotBeNull();
    }
}
