using FluentAssertions;
using Payments.BuildingBlocks.Domain.Primitives;
using Payments.Payment.Domain.Payments;

namespace Payments.Payment.UnitTests;

public sealed class PaymentDomainTests
{
    private static readonly Guid CustomerId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid SourceAccountId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid DestinationAccountId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly DateTimeOffset Now = new(2026, 9, 15, 8, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Creates_initiated_payment_with_initial_transition()
    {
        var payment = CreatePayment();
        payment.Status.Should().Be(PaymentStatus.Initiated);
        payment.StateTransitions.Should().ContainSingle(item => item.FromStatus == null && item.ToStatus == PaymentStatus.Initiated);
    }

    [Fact]
    public void Rejects_invalid_amount()
    {
        Action act = () => Money.Of(0m, Currency.FromCode("NGN"));
        act.Should().Throw<DomainException>().WithMessage("*greater than zero*");
    }

    [Fact]
    public void Rejects_same_account_internal_transfer()
    {
        Action act = () => Domain.Payments.Payment.Create(CustomerId, SourceAccountId, PaymentType.InternalTransfer, Money.Of(100m, Currency.FromCode("NGN")), PaymentDestination.InternalAccount(SourceAccountId), "PAY-20260915-ABC", null, "PAY-20260915-ABC", Now, "corr", PaymentActorType.Customer, "user");
        act.Should().Throw<DomainException>().Where(exception => exception.Code == "payment.same_account");
    }

    [Fact]
    public void Allows_valid_internal_transfer_path_to_completed()
    {
        var payment = CreatePayment();
        payment.StartValidation(Now.AddSeconds(1), PaymentActorType.Customer, "user", "corr");
        payment.MarkFundsReserved(Guid.NewGuid(), Now.AddSeconds(2), PaymentActorType.Customer, "user", "corr");
        payment.StartProcessing(Now.AddSeconds(3), PaymentActorType.Customer, "user", "corr");
        payment.MarkCompleted(Guid.NewGuid(), Now.AddSeconds(4), PaymentActorType.Customer, "user", "corr");

        payment.Status.Should().Be(PaymentStatus.Completed);
        payment.CompletedAtUtc.Should().NotBeNull();
        payment.StateTransitions.Should().HaveCount(5);
    }

    [Fact]
    public void Prevents_invalid_transition()
    {
        var payment = CreatePayment();
        Action act = () => payment.MarkCompleted(Guid.NewGuid(), Now, PaymentActorType.Customer, "user", "corr");
        act.Should().Throw<DomainException>().Where(exception => exception.Code == "payment.invalid_transition");
    }

    [Theory]
    [InlineData(PaymentStatus.Initiated, PaymentStatus.PendingValidation, true)]
    [InlineData(PaymentStatus.PendingValidation, PaymentStatus.FundsReserved, true)]
    [InlineData(PaymentStatus.FundsReserved, PaymentStatus.Processing, true)]
    [InlineData(PaymentStatus.Processing, PaymentStatus.Completed, true)]
    [InlineData(PaymentStatus.Completed, PaymentStatus.Processing, false)]
    [InlineData(PaymentStatus.Rejected, PaymentStatus.Completed, false)]
    public void State_machine_exposes_transition_rules(PaymentStatus from, PaymentStatus to, bool expected)
        => Domain.Payments.Payment.IsValidTransition(from, to).Should().Be(expected);

    [Fact]
    public void Can_cancel_before_processing()
    {
        var payment = CreatePayment();
        payment.Cancel(PaymentFailureReasonCode.CancelledByCustomer, "No longer needed", Now.AddSeconds(1), PaymentActorType.Customer, "user", "corr");
        payment.Status.Should().Be(PaymentStatus.Cancelled);
    }

    private static Domain.Payments.Payment CreatePayment()
        => Domain.Payments.Payment.Create(CustomerId, SourceAccountId, PaymentType.InternalTransfer, Money.Of(100m, Currency.FromCode("NGN")), PaymentDestination.InternalAccount(DestinationAccountId), "PAY-20260915-ABC", "test", "PAY-20260915-ABC", Now, "corr", PaymentActorType.Customer, "user");
}
