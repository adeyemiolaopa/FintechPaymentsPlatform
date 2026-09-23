using System.Data;
using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Payments.Account.Application.Accounts;
using Payments.Account.Domain.Accounts;
using Payments.Account.Infrastructure.Persistence;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using DomainException = Payments.BuildingBlocks.Domain.Primitives.DomainException;

namespace Payments.Account.Infrastructure.Services;

public sealed class AccountPolicyOptions
{
    public const string SectionName = "AccountPolicy";
    public int MaxAccountsPerCustomer { get; init; } = 5;
    public decimal DefaultMaximumReservation { get; init; } = 1_000_000m;
}

public sealed class AccountService : IAccountService
{
    private const string AccountTopic = "account.lifecycle.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("Payments.Account");
    private static readonly Counter<long> AccountsCreated = Meter.CreateCounter<long>("accounts_created_total");
    private static readonly Counter<long> AccountsFrozen = Meter.CreateCounter<long>("accounts_frozen_total");
    private static readonly Counter<long> AccountsClosed = Meter.CreateCounter<long>("accounts_closed_total");
    private static readonly Counter<long> ReservationAttempts = Meter.CreateCounter<long>("funds_reservation_attempts_total");
    private static readonly Counter<long> ReservationSuccesses = Meter.CreateCounter<long>("funds_reservation_success_total");
    private static readonly Counter<long> ReservationFailures = Meter.CreateCounter<long>("funds_reservation_failure_total");

    private readonly AccountDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IValidator<CreateAccountRequest> _createAccountValidator;
    private readonly IValidator<FreezeAccountRequest> _freezeValidator;
    private readonly IValidator<CreateRestrictionRequest> _restrictionValidator;
    private readonly IValidator<CreateReservationRequest> _reservationValidator;
    private readonly AccountPolicyOptions _policyOptions;

    public AccountService(AccountDbContext dbContext, IClock clock, ICurrentUser currentUser, IRequestContext requestContext, IValidator<CreateAccountRequest> createAccountValidator, IValidator<FreezeAccountRequest> freezeValidator, IValidator<CreateRestrictionRequest> restrictionValidator, IValidator<CreateReservationRequest> reservationValidator, IOptions<AccountPolicyOptions> policyOptions)
    {
        _dbContext = dbContext;
        _clock = clock;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _createAccountValidator = createAccountValidator;
        _freezeValidator = freezeValidator;
        _restrictionValidator = restrictionValidator;
        _reservationValidator = reservationValidator;
        _policyOptions = policyOptions.Value;
    }

    public async Task<AccountResponse> CreateAccountAsync(CreateAccountRequest request, CancellationToken cancellationToken = default)
    {
        await _createAccountValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var customerId = RequireCustomerId();
        var now = _clock.UtcNow;
        var currency = Currency.FromCode(request.Currency);
        var accountType = Enum.Parse<AccountType>(request.AccountType, true);
        if (accountType != AccountType.Wallet)
        {
            throw new ConflictException("Self-service customers can only create wallet accounts in Week 3.");
        }

        var customerReference = await _dbContext.CustomerReferences.SingleOrDefaultAsync(reference => reference.CustomerId == customerId, cancellationToken).ConfigureAwait(false)
            ?? throw new ConflictException("Customer reference is not available for account opening yet.");
        if (!customerReference.CanOpenAccount)
        {
            throw new ConflictException("Customer status does not currently allow account opening.");
        }

        var accountCount = await _dbContext.Accounts.CountAsync(account => account.CustomerId == customerId && account.Status != AccountStatus.Closed, cancellationToken).ConfigureAwait(false);
        if (accountCount >= _policyOptions.MaxAccountsPerCustomer)
        {
            throw new ConflictException("Customer account-count policy has been reached.");
        }

        var account = Domain.Accounts.Account.OpenWallet(customerId, AccountNumber.Generate(), currency, now);
        _dbContext.Accounts.Add(account);
        _dbContext.AccountLimits.Add(AccountLimit.Create(account.Id, AccountLimitType.MaximumReservation, Money.Of(_policyOptions.DefaultMaximumReservation, currency), now));
        AddAudit("AccountCreated", account.Id, account.CustomerId, RequireUserIdOrNull(), now);
        AddAccountOutbox(account, "Active", null, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("An active account already exists for this customer, currency, and account type.");
        }

        AccountsCreated.Add(1);
        return Map(account);
    }

    public async Task<IReadOnlyCollection<AccountResponse>> ListMyAccountsAsync(CancellationToken cancellationToken = default)
    {
        var customerId = RequireCustomerId();
        return await _dbContext.Accounts.AsNoTracking()
            .Where(account => account.CustomerId == customerId)
            .OrderBy(account => account.CreatedAtUtc)
            .Select(account => Map(account))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<AccountResponse> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
        => Map(await LoadAuthorizedAccountAsync(accountId, cancellationToken).ConfigureAwait(false));

    public async Task<AccountBalanceResponse> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var account = await LoadAuthorizedAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        return new AccountBalanceResponse(account.Id, account.Currency.Code, account.LedgerBalance, account.ReservedBalance, account.AvailableBalance, _clock.UtcNow);
    }

    public async Task FreezeAsync(Guid accountId, FreezeAccountRequest request, CancellationToken cancellationToken = default)
    {
        await _freezeValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var actor = RequireUserId();
        var now = _clock.UtcNow;
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        account.Freeze(actor, request.Reason, now);
        AddAudit("AccountFrozen", account.Id, account.CustomerId, actor, now, request.Reason);
        AddAccountOutbox(account, "Frozen", request.Reason, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        AccountsFrozen.Add(1);
    }

    public async Task UnfreezeAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = RequireUserId();
        var now = _clock.UtcNow;
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        account.Unfreeze(actor, now);
        AddAudit("AccountUnfrozen", account.Id, account.CustomerId, actor, now);
        AddAccountOutbox(account, "Active", null, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task CloseAsync(Guid accountId, CancellationToken cancellationToken = default)
    {
        var actor = RequireUserId();
        var now = _clock.UtcNow;
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var activeReservations = await _dbContext.FundsReservations.AnyAsync(reservation => reservation.AccountId == accountId && reservation.Status == FundsReservationStatus.Active, cancellationToken).ConfigureAwait(false);
        if (activeReservations)
        {
            throw new ConflictException("Account cannot be closed while active reservations exist.");
        }

        account.Close(actor, now);
        AddAudit("AccountClosed", account.Id, account.CustomerId, actor, now);
        AddAccountOutbox(account, "Closed", null, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        AccountsClosed.Add(1);
    }

    public async Task<AccountRestrictionResponse> AddRestrictionAsync(Guid accountId, CreateRestrictionRequest request, CancellationToken cancellationToken = default)
    {
        await _restrictionValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var actor = RequireUserId();
        var now = _clock.UtcNow;
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var restrictionType = Enum.Parse<AccountRestrictionType>(request.RestrictionType, true);
        var existing = await _dbContext.AccountRestrictions.SingleOrDefaultAsync(restriction => restriction.AccountId == accountId && restriction.RestrictionType == restrictionType && restriction.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return Map(existing);
        }

        var restriction = AccountRestriction.Apply(accountId, restrictionType, request.Reason, actor, now);
        _dbContext.AccountRestrictions.Add(restriction);
        AddAudit("RestrictionApplied", account.Id, account.CustomerId, actor, now, request.Reason, JsonSerializer.Serialize(new { restrictionType = restrictionType.ToString() }));
        AddRestrictionOutbox(account, restriction, "Applied", request.Reason, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Map(restriction);
    }

    public async Task RemoveRestrictionAsync(Guid accountId, Guid restrictionId, CancellationToken cancellationToken = default)
    {
        var actor = RequireUserId();
        var now = _clock.UtcNow;
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var restriction = await _dbContext.AccountRestrictions.SingleOrDefaultAsync(item => item.Id == restrictionId && item.AccountId == accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("AccountRestriction", restrictionId.ToString("D"));
        restriction.Remove(actor, now);
        AddAudit("RestrictionRemoved", account.Id, account.CustomerId, actor, now, null, JsonSerializer.Serialize(new { restrictionType = restriction.RestrictionType.ToString() }));
        AddRestrictionOutbox(account, restriction, "Removed", null, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<ReservationResponse> ReserveFundsAsync(Guid accountId, CreateReservationRequest request, CancellationToken cancellationToken = default)
    {
        await _reservationValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        ReservationAttempts.Add(1);
        var now = _clock.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        try
        {
            var account = await _dbContext.Accounts.FromSqlInterpolated($"SELECT * FROM account.accounts WHERE \"Id\" = {accountId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new NotFoundException("Account", accountId.ToString("D"));
            EnsureCanAccess(account);
            var customerReference = await _dbContext.CustomerReferences.SingleOrDefaultAsync(reference => reference.CustomerId == account.CustomerId, cancellationToken).ConfigureAwait(false)
                ?? throw new ConflictException("Customer reference is not available for reservations.");
            if (!customerReference.CanReserve)
            {
                throw new ConflictException("Customer status does not allow new reservations.");
            }

            var existing = await _dbContext.FundsReservations.SingleOrDefaultAsync(reservation => reservation.AccountId == accountId && reservation.ReferenceId == request.ReferenceId.Trim(), cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return Map(existing);
            }

            var debitBlocked = await _dbContext.AccountRestrictions.AnyAsync(restriction => restriction.AccountId == accountId && restriction.RestrictionType == AccountRestrictionType.DebitBlocked && restriction.RemovedAtUtc == null, cancellationToken).ConfigureAwait(false);
            var reservation = account.ReserveFunds(request.ReferenceId, Money.Of(request.Amount, Currency.FromCode(request.Currency)), now, request.ExpiresAtUtc, debitBlocked);
            _dbContext.FundsReservations.Add(reservation);
            AddAudit("FundsReserved", account.Id, account.CustomerId, RequireUserIdOrNull(), now, null, JsonSerializer.Serialize(new { reservationId = reservation.Id, amount = reservation.Amount, currency = reservation.Currency.Code }));
            AddReservationOutbox(account, reservation, "Active", now);
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            ReservationSuccesses.Add(1);
            return Map(reservation);
        }
        catch (DomainException)
        {
            ReservationFailures.Add(1);
            throw;
        }
        catch (DbUpdateConcurrencyException exception)
        {
            ReservationFailures.Add(1);
            throw new ConflictException($"Account concurrency conflict: {exception.Message}");
        }
    }

    public async Task<ReservationResponse> GetReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        var account = await LoadAuthorizedAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        var reservation = await _dbContext.FundsReservations.AsNoTracking().SingleOrDefaultAsync(item => item.Id == reservationId && item.AccountId == account.Id, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("FundsReservation", reservationId.ToString("D"));
        return Map(reservation);
    }
    public async Task<ReservationResponse> CommitReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        var account = await _dbContext.Accounts.FromSqlInterpolated($"SELECT * FROM account.accounts WHERE \"Id\" = {accountId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Account", accountId.ToString("D"));
        EnsureCanAccess(account);
        var reservation = await _dbContext.FundsReservations.SingleOrDefaultAsync(item => item.Id == reservationId && item.AccountId == accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("FundsReservation", reservationId.ToString("D"));
        account.CommitReservation(reservation, now);
        AddAudit("FundsReservationCommitted", account.Id, account.CustomerId, RequireUserIdOrNull(), now, null, JsonSerializer.Serialize(new { reservationId = reservation.Id, amount = reservation.Amount, currency = reservation.Currency.Code }));
        AddReservationOutbox(account, reservation, "Committed", now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(reservation);
    }
    public async Task<ReservationResponse> ReleaseReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        var account = await _dbContext.Accounts.FromSqlInterpolated($"SELECT * FROM account.accounts WHERE \"Id\" = {accountId} FOR UPDATE").SingleOrDefaultAsync(cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Account", accountId.ToString("D"));
        EnsureCanAccess(account);
        var reservation = await _dbContext.FundsReservations.SingleOrDefaultAsync(item => item.Id == reservationId && item.AccountId == accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("FundsReservation", reservationId.ToString("D"));
        account.ReleaseReservation(reservation, now);
        AddAudit("FundsReleased", account.Id, account.CustomerId, RequireUserIdOrNull(), now, null, JsonSerializer.Serialize(new { reservationId = reservation.Id, amount = reservation.Amount, currency = reservation.Currency.Code }));
        AddReservationOutbox(account, reservation, "Released", now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(reservation);
    }

    private async Task<Domain.Accounts.Account> LoadAuthorizedAccountAsync(Guid accountId, CancellationToken cancellationToken)
    {
        var account = await LoadAccountAsync(accountId, cancellationToken).ConfigureAwait(false);
        EnsureCanAccess(account);
        return account;
    }

    private async Task<Domain.Accounts.Account> LoadAccountAsync(Guid accountId, CancellationToken cancellationToken)
        => await _dbContext.Accounts.SingleOrDefaultAsync(account => account.Id == accountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Account", accountId.ToString("D"));

    private void EnsureCanAccess(Domain.Accounts.Account account)
    {
        if (_currentUser.HasPermission("account.read.any"))
        {
            return;
        }

        var customerId = RequireCustomerId();
        if (account.CustomerId != customerId)
        {
            throw new ForbiddenApplicationException();
        }
    }

    private Guid RequireCustomerId() => Guid.TryParse(_currentUser.CustomerId, out var customerId) ? customerId : throw new UnauthorizedApplicationException();
    private Guid RequireUserId() => Guid.TryParse(_currentUser.UserId, out var userId) ? userId : throw new UnauthorizedApplicationException();
    private Guid? RequireUserIdOrNull() => Guid.TryParse(_currentUser.UserId, out var userId) ? userId : null;

    private void AddAudit(string eventType, Guid? accountId, Guid customerId, Guid? actorUserId, DateTimeOffset now, string? reason = null, string metadata = "{}")
        => _dbContext.AccountAuditEvents.Add(AccountAuditEvent.Create(eventType, accountId, customerId, actorUserId, now, _requestContext.CorrelationId, reason, metadata));

    private void AddAccountOutbox(Domain.Accounts.Account account, string status, string? reason, DateTimeOffset now)
    {
        var envelope = new IntegrationEventEnvelope<AccountLifecycleIntegrationEvent>(Guid.NewGuid(), AccountLifecycleIntegrationEvent.EventType, AccountLifecycleIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "account-service", new AccountLifecycleIntegrationEvent(account.Id, account.CustomerId, account.Currency.Code, account.AccountType.ToString(), status, reason));
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(AccountTopic, account.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "Account", account.Id.ToString("D")));
    }

    private void AddReservationOutbox(Domain.Accounts.Account account, FundsReservation reservation, string status, DateTimeOffset now)
    {
        var envelope = new IntegrationEventEnvelope<FundsReservationIntegrationEvent>(Guid.NewGuid(), FundsReservationIntegrationEvent.EventType, FundsReservationIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "account-service", new FundsReservationIntegrationEvent(account.Id, account.CustomerId, reservation.Id, reservation.ReferenceId, reservation.Amount, reservation.Currency.Code, status));
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(AccountTopic, account.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "Account", account.Id.ToString("D")));
    }

    private void AddRestrictionOutbox(Domain.Accounts.Account account, AccountRestriction restriction, string status, string? reason, DateTimeOffset now)
    {
        var envelope = new IntegrationEventEnvelope<AccountRestrictionIntegrationEvent>(Guid.NewGuid(), AccountRestrictionIntegrationEvent.EventType, AccountRestrictionIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "account-service", new AccountRestrictionIntegrationEvent(account.Id, account.CustomerId, restriction.Id, restriction.RestrictionType.ToString(), status, reason));
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(AccountTopic, account.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "Account", account.Id.ToString("D")));
    }

    private static AccountResponse Map(Domain.Accounts.Account account) => new(account.Id, account.CustomerId, Mask(account.AccountNumber.Value), account.AccountName, account.Currency.Code, account.AccountType.ToString(), account.Status.ToString(), account.CreatedAtUtc, account.ClosedAtUtc);
    private static AccountRestrictionResponse Map(AccountRestriction restriction) => new(restriction.Id, restriction.AccountId, restriction.RestrictionType.ToString(), restriction.Reason, restriction.AppliedAtUtc, restriction.AppliedBy, restriction.RemovedAtUtc, restriction.RemovedBy);
    private static ReservationResponse Map(FundsReservation reservation) => new(reservation.Id, reservation.AccountId, reservation.ReferenceId, reservation.Amount, reservation.Currency.Code, reservation.Status.ToString(), reservation.CreatedAtUtc, reservation.ExpiresAtUtc);
    private static string Mask(string accountNumber) => accountNumber.Length <= 4 ? "****" : $"******{accountNumber[^4..]}";
}
