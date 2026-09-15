using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Payments.Ledger.Application.Ledger;

public sealed record CreateLedgerAccountRequest(string ExternalReference, string AccountCode, string AccountName, string AccountType, string Currency);
public sealed record LedgerAccountResponse(Guid LedgerAccountId, string ExternalReference, string AccountCode, string AccountName, string AccountType, string Currency, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc);

public sealed record PostingRequest(Guid LedgerAccountId, string Side, decimal Amount, string? Description = null);
public sealed record PostLedgerTransactionRequest(string ExternalReference, string TransactionType, string Currency, string Description, IReadOnlyCollection<PostingRequest> Postings, DateTimeOffset? OccurredAtUtc = null);
public sealed record ReverseLedgerTransactionRequest(string ExternalReference, string Reason);

public sealed record PostingResponse(Guid PostingId, Guid LedgerAccountId, string Side, decimal Amount, string Currency, int Sequence, string? Description, DateTimeOffset CreatedAtUtc);
public sealed record LedgerTransactionResponse(Guid TransactionId, string ExternalReference, string TransactionType, string Currency, string Description, string Status, DateTimeOffset OccurredAtUtc, DateTimeOffset PostedAtUtc, Guid? OriginalTransactionId, string? ReversalReason, IReadOnlyCollection<PostingResponse> Postings);

public sealed record LedgerBalanceResponse(Guid LedgerAccountId, string Currency, decimal DebitTotal, decimal CreditTotal, decimal Balance, DateTimeOffset AsOfUtc);
public sealed record LedgerEntryResponse(Guid PostingId, Guid TransactionId, Guid LedgerAccountId, string ExternalReference, string TransactionType, string Side, decimal Amount, string Currency, int Sequence, string? Description, DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
public sealed record LedgerEntryPageResponse(IReadOnlyCollection<LedgerEntryResponse> Entries, string? NextCursor);
public sealed record LedgerIntegrityResponse(bool IsValid, int TransactionsChecked, int ProjectionAccountsChecked, IReadOnlyCollection<string> Errors, DateTimeOffset VerifiedAtUtc);

public interface ILedgerService
{
    Task<LedgerAccountResponse> CreateAccountAsync(CreateLedgerAccountRequest request, CancellationToken cancellationToken = default);
    Task<LedgerAccountResponse> GetAccountAsync(Guid ledgerAccountId, CancellationToken cancellationToken = default);
    Task<LedgerTransactionResponse> PostTransactionAsync(PostLedgerTransactionRequest request, CancellationToken cancellationToken = default);
    Task<LedgerTransactionResponse> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default);
    Task<LedgerTransactionResponse> ReverseTransactionAsync(Guid transactionId, ReverseLedgerTransactionRequest request, CancellationToken cancellationToken = default);
    Task<LedgerBalanceResponse> GetBalanceAsync(Guid ledgerAccountId, DateTimeOffset? asOfUtc, CancellationToken cancellationToken = default);
    Task<LedgerEntryPageResponse> GetEntriesAsync(Guid ledgerAccountId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? side, string? transactionType, string? cursor, int pageSize, CancellationToken cancellationToken = default);
    Task<LedgerIntegrityResponse> VerifyIntegrityAsync(CancellationToken cancellationToken = default);
}

public sealed class CreateLedgerAccountRequestValidator : AbstractValidator<CreateLedgerAccountRequest>
{
    public CreateLedgerAccountRequestValidator()
    {
        RuleFor(request => request.ExternalReference).NotEmpty().MaximumLength(128);
        RuleFor(request => request.AccountCode).NotEmpty().MaximumLength(64);
        RuleFor(request => request.AccountName).NotEmpty().MaximumLength(160);
        RuleFor(request => request.AccountType).NotEmpty().Must(value => Enum.TryParse<Domain.Ledger.LedgerAccountType>(value, true, out _)).WithMessage("Account type is invalid.");
        RuleFor(request => request.Currency).NotEmpty().Length(3);
    }
}

public sealed class PostLedgerTransactionRequestValidator : AbstractValidator<PostLedgerTransactionRequest>
{
    public PostLedgerTransactionRequestValidator()
    {
        RuleFor(request => request.ExternalReference).NotEmpty().MaximumLength(128);
        RuleFor(request => request.TransactionType).NotEmpty().MaximumLength(80);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Description).NotEmpty().MaximumLength(240);
        RuleFor(request => request.Postings).NotNull().Must(postings => postings.Count >= 2).WithMessage("At least two postings are required.");
        RuleForEach(request => request.Postings).ChildRules(posting =>
        {
            posting.RuleFor(value => value.LedgerAccountId).NotEmpty();
            posting.RuleFor(value => value.Side).NotEmpty().Must(value => Enum.TryParse<Domain.Ledger.EntrySide>(value, true, out _)).WithMessage("Posting side is invalid.");
            posting.RuleFor(value => value.Amount).GreaterThan(0m).PrecisionScale(19, 4, false);
            posting.RuleFor(value => value.Description).MaximumLength(240);
        });
    }
}

public sealed class ReverseLedgerTransactionRequestValidator : AbstractValidator<ReverseLedgerTransactionRequest>
{
    public ReverseLedgerTransactionRequestValidator()
    {
        RuleFor(request => request.ExternalReference).NotEmpty().MaximumLength(128);
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(240);
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddLedgerApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateLedgerAccountRequest>, CreateLedgerAccountRequestValidator>();
        services.AddScoped<IValidator<PostLedgerTransactionRequest>, PostLedgerTransactionRequestValidator>();
        services.AddScoped<IValidator<ReverseLedgerTransactionRequest>, ReverseLedgerTransactionRequestValidator>();
        return services;
    }
}
