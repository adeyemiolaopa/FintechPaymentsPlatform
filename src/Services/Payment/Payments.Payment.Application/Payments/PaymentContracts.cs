using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.Payment.Domain.Payments;

namespace Payments.Payment.Application.Payments;

public sealed record CreatePaymentRequest(Guid SourceAccountId, string Type, decimal Amount, string Currency, PaymentDestinationRequest Destination, string? Description = null);
public sealed record PaymentDestinationRequest(Guid? AccountId = null, string? BankCode = null, string? AccountNumber = null, string? AccountName = null, string? CountryCode = null);
public sealed record CancelPaymentRequest(string? Reason = null);
public sealed record PaymentResponse(Guid PaymentId, string Reference, string Status, string Type, decimal Amount, string Currency, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, Guid? FundsReservationId, Guid? LedgerTransactionId, string? ReasonCode, string? ReasonDescription);
public sealed record PaymentDetailResponse(Guid PaymentId, Guid CustomerId, Guid SourceAccountId, string Reference, string Status, string Type, decimal Amount, string Currency, PaymentDestinationResponse Destination, string? Description, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc, DateTimeOffset? CompletedAtUtc, DateTimeOffset? FailedAtUtc, Guid? FundsReservationId, Guid? LedgerTransactionId, string? ReasonCode, string? ReasonDescription);
public sealed record PaymentDestinationResponse(string Type, Guid? AccountId, string? BankCode, string? AccountNumberMasked, string? AccountName, string? CountryCode);
public sealed record PaymentTimelineEntryResponse(string? FromStatus, string ToStatus, string? ReasonCode, string? ReasonDescription, DateTimeOffset OccurredAtUtc, string ActorType, string ActorId, string CorrelationId);
public sealed record PaymentPageResponse(IReadOnlyCollection<PaymentResponse> Items, int Page, int PageSize, int TotalCount);
public sealed record PaymentSearchRequest(string? Status, string? Type, string? Currency, DateTimeOffset? FromUtc, DateTimeOffset? ToUtc, int Page = 1, int PageSize = 50);

public interface IPaymentService
{
    Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default);
    Task<PaymentDetailResponse> GetAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<PaymentTimelineEntryResponse>> GetTimelineAsync(Guid paymentId, CancellationToken cancellationToken = default);
    Task<PaymentPageResponse> ListAsync(PaymentSearchRequest request, CancellationToken cancellationToken = default);
    Task<PaymentResponse> CancelAsync(Guid paymentId, CancelPaymentRequest request, CancellationToken cancellationToken = default);
    Task<int> RecoverAsync(int batchSize, TimeSpan minAge, CancellationToken cancellationToken = default);
}

public sealed record AccountClientResponse(Guid AccountId, Guid CustomerId, string Currency, string AccountType, string Status);
public sealed record AccountBalanceClientResponse(Guid AccountId, string Currency, decimal LedgerBalance, decimal ReservedBalance, decimal AvailableBalance, DateTimeOffset AsOfUtc);
public sealed record ReservationClientResponse(Guid ReservationId, Guid AccountId, string ReferenceId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);
public sealed record CreateFundsReservationCommand(string ReferenceId, decimal Amount, string Currency, DateTimeOffset ExpiresAtUtc);

public interface IAccountServiceClient
{
    Task<AccountClientResponse> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountBalanceClientResponse> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<ReservationClientResponse> ReserveFundsAsync(Guid accountId, CreateFundsReservationCommand command, CancellationToken cancellationToken = default);
    Task<ReservationClientResponse> CommitReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default);
    Task<ReservationClientResponse> ReleaseReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default);
}

public sealed record LedgerPostingCommand(Guid LedgerAccountId, string Side, decimal Amount, string? Description);
public sealed record LedgerTransactionClientResponse(Guid TransactionId, string ExternalReference, string Status);
public sealed record PostLedgerTransactionCommand(string ExternalReference, string TransactionType, string Currency, string Description, IReadOnlyCollection<LedgerPostingCommand> Postings, DateTimeOffset? OccurredAtUtc = null);

public interface ILedgerServiceClient
{
    Task<LedgerTransactionClientResponse> PostTransactionAsync(PostLedgerTransactionCommand command, CancellationToken cancellationToken = default);
}

public sealed class DownstreamBusinessException : Exception
{
    public DownstreamBusinessException(PaymentFailureReasonCode reasonCode, string message) : base(message) => ReasonCode = reasonCode;
    public PaymentFailureReasonCode ReasonCode { get; }
}

public sealed class DownstreamTransientException : Exception
{
    public DownstreamTransientException(PaymentFailureReasonCode reasonCode, string message, Exception? innerException = null) : base(message, innerException) => ReasonCode = reasonCode;
    public PaymentFailureReasonCode ReasonCode { get; }
}

public sealed class CreatePaymentRequestValidator : AbstractValidator<CreatePaymentRequest>
{
    public CreatePaymentRequestValidator()
    {
        RuleFor(request => request.SourceAccountId).NotEmpty();
        RuleFor(request => request.Type).NotEmpty().Must(value => Enum.TryParse<PaymentType>(value, true, out _)).WithMessage("Payment type is invalid.");
        RuleFor(request => request.Amount).GreaterThan(0m).PrecisionScale(19, 4, false);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Description).MaximumLength(240);
        RuleFor(request => request.Destination).NotNull();
        When(request => string.Equals(request.Type, PaymentType.InternalTransfer.ToString(), StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(request => request.Destination.AccountId).NotEmpty();
        });
        When(request => string.Equals(request.Type, PaymentType.ExternalBankTransfer.ToString(), StringComparison.OrdinalIgnoreCase), () =>
        {
            RuleFor(request => request.Destination.BankCode).NotEmpty().MaximumLength(12);
            RuleFor(request => request.Destination.AccountNumber).NotEmpty().Matches("^[0-9]{6,20}$");
            RuleFor(request => request.Destination.AccountName).NotEmpty().MaximumLength(120);
            RuleFor(request => request.Destination.CountryCode).NotEmpty().Length(2);
        });
    }
}

public sealed class PaymentSearchRequestValidator : AbstractValidator<PaymentSearchRequest>
{
    public PaymentSearchRequestValidator()
    {
        RuleFor(request => request.Status).Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<PaymentStatus>(value, true, out _)).WithMessage("Payment status is invalid.");
        RuleFor(request => request.Type).Must(value => string.IsNullOrWhiteSpace(value) || Enum.TryParse<PaymentType>(value, true, out _)).WithMessage("Payment type is invalid.");
        RuleFor(request => request.Currency).Length(3).When(request => !string.IsNullOrWhiteSpace(request.Currency));
        RuleFor(request => request.Page).GreaterThan(0);
        RuleFor(request => request.PageSize).InclusiveBetween(1, 100);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddPaymentApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreatePaymentRequest>, CreatePaymentRequestValidator>();
        services.AddScoped<IValidator<PaymentSearchRequest>, PaymentSearchRequestValidator>();
        return services;
    }
}
