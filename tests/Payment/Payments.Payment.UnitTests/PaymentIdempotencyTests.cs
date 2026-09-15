using FluentAssertions;
using Payments.Payment.Application.Payments;

namespace Payments.Payment.UnitTests;

public sealed class PaymentIdempotencyTests
{
    private static readonly Guid SourceAccountId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DestinationAccountId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Idempotency_key_validation_rejects_missing_blank_short_and_unsafe_values()
    {
        Action missing = () => IdempotencyKeyRules.Normalize(null);
        Action blank = () => IdempotencyKeyRules.Normalize("   ");
        Action tooShort = () => IdempotencyKeyRules.Normalize("abc");
        Action unsafeValue = () => IdempotencyKeyRules.Normalize("valid-length-but unsafe");

        missing.Should().Throw<IdempotencyKeyValidationException>();
        blank.Should().Throw<IdempotencyKeyValidationException>();
        tooShort.Should().Throw<IdempotencyKeyValidationException>();
        unsafeValue.Should().Throw<IdempotencyKeyValidationException>();
    }

    [Fact]
    public void Idempotency_key_validation_accepts_opaque_safe_values()
    {
        IdempotencyKeyRules.Normalize("  8f33de82-9969-4df0-98ef-e92db58f86ce  ").Should().Be("8f33de82-9969-4df0-98ef-e92db58f86ce");
        IdempotencyKeyRules.Normalize("mobile.checkout_123:attempt-1").Should().Be("mobile.checkout_123:attempt-1");
    }

    [Fact]
    public void Canonical_hash_normalizes_type_currency_and_destination_formatting()
    {
        var first = new CreatePaymentRequest(SourceAccountId, "internaltransfer", 1000.00m, "ngn", new PaymentDestinationRequest(AccountId: DestinationAccountId), "Rent");
        var second = new CreatePaymentRequest(SourceAccountId, "InternalTransfer", 1000m, "NGN", new PaymentDestinationRequest(AccountId: DestinationAccountId), "Rent");

        PaymentRequestHasher.ComputeHash(first).Should().Be(PaymentRequestHasher.ComputeHash(second));
    }

    [Fact]
    public void Canonical_hash_changes_when_financially_meaningful_input_changes()
    {
        var first = new CreatePaymentRequest(SourceAccountId, "InternalTransfer", 1000m, "NGN", new PaymentDestinationRequest(AccountId: DestinationAccountId), "Rent");
        var changedAmount = first with { Amount = 1001m };
        var changedDescription = first with { Description = "rent" };

        PaymentRequestHasher.ComputeHash(changedAmount).Should().NotBe(PaymentRequestHasher.ComputeHash(first));
        PaymentRequestHasher.ComputeHash(changedDescription).Should().NotBe(PaymentRequestHasher.ComputeHash(first));
    }

    [Fact]
    public void Canonical_hash_for_external_destination_normalizes_bank_and_country_codes()
    {
        var first = new CreatePaymentRequest(SourceAccountId, "ExternalBankTransfer", 5000m, "ngn", new PaymentDestinationRequest(BankCode: " 058 ", AccountNumber: "0123456789", AccountName: " Ada Lovelace ", CountryCode: "ng"), "Payout");
        var second = new CreatePaymentRequest(SourceAccountId, "ExternalBankTransfer", 5000.00m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "Payout");

        PaymentRequestHasher.ComputeHash(first).Should().Be(PaymentRequestHasher.ComputeHash(second));
    }
}
