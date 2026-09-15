using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Payments.Account.Domain.Accounts;

namespace Payments.Account.Application.Accounts;

public sealed record CreateAccountRequest(string Currency, string AccountType);
public sealed record AccountResponse(Guid AccountId, Guid CustomerId, string AccountNumberMasked, string AccountName, string Currency, string AccountType, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset? ClosedAtUtc);
public sealed record AccountBalanceResponse(Guid AccountId, string Currency, decimal LedgerBalance, decimal ReservedBalance, decimal AvailableBalance, DateTimeOffset AsOfUtc);
public sealed record FreezeAccountRequest(string Reason);
public sealed record CreateRestrictionRequest(string RestrictionType, string Reason);
public sealed record AccountRestrictionResponse(Guid RestrictionId, Guid AccountId, string RestrictionType, string Reason, DateTimeOffset AppliedAtUtc, Guid AppliedBy, DateTimeOffset? RemovedAtUtc, Guid? RemovedBy);
public sealed record CreateBeneficiaryRequest(string Type, string Name, string? BankCode, string AccountNumber, string Currency, string CountryCode, string? Nickname);
public sealed record BeneficiaryResponse(Guid BeneficiaryId, string Type, string Name, string? BankCode, string AccountNumberMasked, string Currency, string CountryCode, string? Nickname, string Status, DateTimeOffset CreatedAtUtc);
public sealed record PagedResponse<T>(IReadOnlyCollection<T> Items, int Page, int PageSize, int TotalCount);
public sealed record CreateReservationRequest(string ReferenceId, decimal Amount, string Currency, DateTimeOffset ExpiresAtUtc);
public sealed record ReservationResponse(Guid ReservationId, Guid AccountId, string ReferenceId, decimal Amount, string Currency, string Status, DateTimeOffset CreatedAtUtc, DateTimeOffset ExpiresAtUtc);

public interface IAccountService
{
    Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default);
    Task<IReadOnlyCollection<AccountResponse>> ListMyAccountsAsync(CancellationToken cancellationToken = default);
    Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountBalanceResponse> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task FreezeAsync(Guid accountId, FreezeAccountRequest request, CancellationToken cancellationToken = default);
    Task UnfreezeAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task CloseAsync(Guid accountId, CancellationToken cancellationToken = default);
    Task<AccountRestrictionResponse> AddRestrictionAsync(Guid accountId, CreateRestrictionRequest request, CancellationToken cancellationToken = default);
    Task RemoveRestrictionAsync(Guid accountId, Guid restrictionId, CancellationToken cancellationToken = default);
    Task<ReservationResponse> ReserveFundsAsync(Guid accountId, CreateReservationRequest request, CancellationToken cancellationToken = default);
    Task<ReservationResponse> CommitReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default);
    Task<ReservationResponse> ReleaseReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default);
}

public interface IBeneficiaryService
{
    Task<BeneficiaryResponse> AddAsync(CreateBeneficiaryRequest request, CancellationToken cancellationToken = default);
    Task<PagedResponse<BeneficiaryResponse>> ListAsync(int page, int pageSize, string? status, string? type, CancellationToken cancellationToken = default);
    Task RemoveAsync(Guid beneficiaryId, CancellationToken cancellationToken = default);
}

public sealed class CreateAccountRequestValidator : AbstractValidator<CreateAccountRequest>
{
    public CreateAccountRequestValidator()
    {
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.AccountType).NotEmpty().Must(value => Enum.TryParse<AccountType>(value, true, out _)).WithMessage("Account type is invalid.");
    }
}

public sealed class FreezeAccountRequestValidator : AbstractValidator<FreezeAccountRequest>
{
    public FreezeAccountRequestValidator() => RuleFor(request => request.Reason).NotEmpty().MaximumLength(240);
}

public sealed class CreateRestrictionRequestValidator : AbstractValidator<CreateRestrictionRequest>
{
    public CreateRestrictionRequestValidator()
    {
        RuleFor(request => request.RestrictionType).NotEmpty().Must(value => Enum.TryParse<AccountRestrictionType>(value, true, out _)).WithMessage("Restriction type is invalid.");
        RuleFor(request => request.Reason).NotEmpty().MaximumLength(240);
    }
}

public sealed class CreateBeneficiaryRequestValidator : AbstractValidator<CreateBeneficiaryRequest>
{
    public CreateBeneficiaryRequestValidator()
    {
        RuleFor(request => request.Type).NotEmpty().Must(value => Enum.TryParse<BeneficiaryType>(value, true, out _)).WithMessage("Beneficiary type is invalid.");
        RuleFor(request => request.Name).NotEmpty().MaximumLength(120);
        RuleFor(request => request.AccountNumber).NotEmpty().Matches("^[0-9]{6,20}$");
        RuleFor(request => request.BankCode).Matches("^[0-9A-Z]{2,12}$").When(request => !string.IsNullOrWhiteSpace(request.BankCode));
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.CountryCode).NotEmpty().Length(2);
        RuleFor(request => request.Nickname).MaximumLength(80);
    }
}

public sealed class CreateReservationRequestValidator : AbstractValidator<CreateReservationRequest>
{
    public CreateReservationRequestValidator()
    {
        RuleFor(request => request.ReferenceId).NotEmpty().MaximumLength(128);
        RuleFor(request => request.Amount).GreaterThan(0m);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.ExpiresAtUtc).GreaterThan(DateTimeOffset.UtcNow.AddSeconds(-1));
    }
}

public static class DependencyInjection
{
    public static IServiceCollection AddAccountApplication(this IServiceCollection services)
    {
        services.AddScoped<IValidator<CreateAccountRequest>, CreateAccountRequestValidator>();
        services.AddScoped<IValidator<FreezeAccountRequest>, FreezeAccountRequestValidator>();
        services.AddScoped<IValidator<CreateRestrictionRequest>, CreateRestrictionRequestValidator>();
        services.AddScoped<IValidator<CreateBeneficiaryRequest>, CreateBeneficiaryRequestValidator>();
        services.AddScoped<IValidator<CreateReservationRequest>, CreateReservationRequestValidator>();
        return services;
    }
}
