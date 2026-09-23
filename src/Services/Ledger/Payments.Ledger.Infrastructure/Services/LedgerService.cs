using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Application.Ledger;
using Payments.Ledger.Domain.Ledger;
using Payments.Ledger.Infrastructure.Persistence;
using DomainException = Payments.BuildingBlocks.Domain.Primitives.DomainException;

namespace Payments.Ledger.Infrastructure.Services;

public sealed class LedgerService : ILedgerService
{
    private const string LedgerTransactionsTopic = "ledger.transactions.v1";
    private const string LedgerAccountsTopic = "ledger.accounts.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly LedgerDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IValidator<CreateLedgerAccountRequest> _accountValidator;
    private readonly IValidator<PostLedgerTransactionRequest> _postValidator;
    private readonly IValidator<ReverseLedgerTransactionRequest> _reverseValidator;

    public LedgerService(LedgerDbContext dbContext, IClock clock, ICurrentUser currentUser, IRequestContext requestContext, IValidator<CreateLedgerAccountRequest> accountValidator, IValidator<PostLedgerTransactionRequest> postValidator, IValidator<ReverseLedgerTransactionRequest> reverseValidator)
    {
        _dbContext = dbContext;
        _clock = clock;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _accountValidator = accountValidator;
        _postValidator = postValidator;
        _reverseValidator = reverseValidator;
    }

    public async Task<LedgerAccountResponse> CreateAccountAsync(CreateLedgerAccountRequest request, CancellationToken cancellationToken = default)
    {
        await _accountValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var account = LedgerAccount.Create(request.ExternalReference, request.AccountCode, request.AccountName, Enum.Parse<LedgerAccountType>(request.AccountType, true), Currency.FromCode(request.Currency), now);
        _dbContext.LedgerAccounts.Add(account);
        _dbContext.BalanceProjections.Add(LedgerAccountBalance.Create(account.Id, account.Currency, now));
        AddAudit("LedgerAccountCreated", null, account.Id, now, null, JsonSerializer.Serialize(new { account.ExternalReference, account.AccountCode, account.AccountType, currency = account.Currency.Code }, SerializerOptions));
        AddAccountOutbox(account, now);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A ledger account already exists for this external reference or account code.");
        }

        return Map(account);
    }

    public async Task<LedgerAccountResponse> GetAccountAsync(Guid ledgerAccountId, CancellationToken cancellationToken = default)
        => Map(await LoadAccountAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false));

    public async Task<LedgerTransactionResponse> PostTransactionAsync(PostLedgerTransactionRequest request, CancellationToken cancellationToken = default)
    {
        await _postValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var hash = ComputeRequestHash(request);
        try
        {
            return await PostTransactionCoreAsync(request, hash, null, null, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            return await ResolveIdempotentRetryAsync(request.ExternalReference, hash, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<LedgerTransactionResponse> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
        => Map(await LoadTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false));

    public async Task<LedgerTransactionResponse?> GetTransactionByExternalReferenceAsync(string externalReference, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(externalReference) || externalReference.Length > 128) throw new ArgumentException("Invalid ledger external reference.", nameof(externalReference));
        var record = await _dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(x => x.IdempotencyKey == externalReference, cancellationToken).ConfigureAwait(false);
        return record is null ? null : Map(await LoadTransactionAsync(record.TransactionId, cancellationToken).ConfigureAwait(false));
    }
    public async Task<LedgerTransactionResponse> ReverseTransactionAsync(Guid transactionId, ReverseLedgerTransactionRequest request, CancellationToken cancellationToken = default)
    {
        await _reverseValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var original = await LoadTransactionAsync(transactionId, cancellationToken).ConfigureAwait(false);
        if (original.OriginalTransactionId is not null)
        {
            throw new ConflictException("A reversal transaction cannot be reversed in Week 4; create a controlled adjustment in a future workflow.");
        }

        if (await _dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record => record.IdempotencyKey == request.ExternalReference, cancellationToken).ConfigureAwait(false) is { } existingRecord)
        {
            var existingTransaction = await LoadTransactionAsync(existingRecord.TransactionId, cancellationToken).ConfigureAwait(false);
            if (existingTransaction.OriginalTransactionId == transactionId)
            {
                return Map(existingTransaction);
            }

            throw new ConflictException("Reversal idempotency key already exists for a different original transaction.");
        }

        if (await _dbContext.Reversals.AnyAsync(reversal => reversal.OriginalTransactionId == transactionId, cancellationToken).ConfigureAwait(false))
        {
            throw new ConflictException("Ledger transaction has already been reversed.");
        }

        var reversalRequest = new PostLedgerTransactionRequest(request.ExternalReference, "Reversal", original.Currency.Code, $"Reversal of {original.ExternalReference}", original.Postings.OrderBy(posting => posting.Sequence).Select(posting => new PostingRequest(posting.LedgerAccountId, posting.Side == EntrySide.Debit ? "Credit" : "Debit", posting.Amount, posting.Description)).ToArray(), _clock.UtcNow);
        var hash = ComputeRequestHash(reversalRequest);

        try
        {
            return await PostTransactionCoreAsync(reversalRequest, hash, original, request.Reason, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            if (await _dbContext.Reversals.AsNoTracking().SingleOrDefaultAsync(reversal => reversal.OriginalTransactionId == transactionId, cancellationToken).ConfigureAwait(false) is { } existingReversal)
            {
                return Map(await LoadTransactionAsync(existingReversal.ReversalTransactionId, cancellationToken).ConfigureAwait(false));
            }

            return await ResolveIdempotentRetryAsync(request.ExternalReference, hash, cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<LedgerBalanceResponse> GetBalanceAsync(Guid ledgerAccountId, DateTimeOffset? asOfUtc, CancellationToken cancellationToken = default)
    {
        var account = await LoadAccountAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        if (asOfUtc is null)
        {
            var projection = await _dbContext.BalanceProjections.AsNoTracking().SingleAsync(balance => balance.LedgerAccountId == ledgerAccountId, cancellationToken).ConfigureAwait(false);
            return new LedgerBalanceResponse(account.Id, account.Currency.Code, projection.DebitTotal, projection.CreditTotal, projection.CalculateBalance(account.AccountType), now);
        }

        var rows = await _dbContext.Postings.AsNoTracking()
            .Where(posting => posting.LedgerAccountId == ledgerAccountId)
            .Join(_dbContext.LedgerTransactions.AsNoTracking().Where(transaction => transaction.OccurredAtUtc <= asOfUtc.Value), posting => posting.TransactionId, transaction => transaction.Id, (posting, transaction) => posting)
            .GroupBy(_ => 1)
            .Select(group => new { Debit = group.Where(posting => posting.Side == EntrySide.Debit).Sum(posting => posting.Amount), Credit = group.Where(posting => posting.Side == EntrySide.Credit).Sum(posting => posting.Amount) })
            .SingleOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
        var debit = rows?.Debit ?? 0m;
        var credit = rows?.Credit ?? 0m;
        var balance = account.IsDebitNormal ? debit - credit : credit - debit;
        return new LedgerBalanceResponse(account.Id, account.Currency.Code, debit, credit, balance, asOfUtc.Value);
    }

    public async Task<LedgerEntryPageResponse> GetEntriesAsync(Guid ledgerAccountId, DateTimeOffset? fromUtc, DateTimeOffset? toUtc, string? side, string? transactionType, string? cursor, int pageSize, CancellationToken cancellationToken = default)
    {
        _ = await LoadAccountAsync(ledgerAccountId, cancellationToken).ConfigureAwait(false);
        var take = Math.Clamp(pageSize <= 0 ? 50 : pageSize, 1, 100);
        EntrySide? parsedSide = string.IsNullOrWhiteSpace(side) ? null : Enum.Parse<EntrySide>(side, true);
        var (cursorTicks, cursorPostingId) = ParseCursor(cursor);

        var query = _dbContext.Postings.AsNoTracking()
            .Where(posting => posting.LedgerAccountId == ledgerAccountId)
            .Join(_dbContext.LedgerTransactions.AsNoTracking(), posting => posting.TransactionId, transaction => transaction.Id, (posting, transaction) => new LedgerEntryProjection(posting.Id, posting.TransactionId, posting.LedgerAccountId, transaction.ExternalReference, transaction.TransactionType, posting.Side, posting.Amount, posting.Currency.Code, posting.Sequence, posting.Description, transaction.OccurredAtUtc, posting.CreatedAtUtc));

        if (fromUtc is not null)
        {
            query = query.Where(entry => entry.OccurredAtUtc >= fromUtc.Value);
        }

        if (toUtc is not null)
        {
            query = query.Where(entry => entry.OccurredAtUtc <= toUtc.Value);
        }

        if (parsedSide is not null)
        {
            query = query.Where(entry => entry.Side == parsedSide.Value);
        }

        if (!string.IsNullOrWhiteSpace(transactionType))
        {
            query = query.Where(entry => entry.TransactionType == transactionType);
        }

        if (cursorTicks is not null && cursorPostingId is not null)
        {
            var cursorTime = new DateTimeOffset(cursorTicks.Value, TimeSpan.Zero);
            query = query.Where(entry => entry.CreatedAtUtc > cursorTime || entry.CreatedAtUtc == cursorTime && entry.PostingId.CompareTo(cursorPostingId.Value) > 0);
        }

        var results = await query.OrderBy(entry => entry.CreatedAtUtc).ThenBy(entry => entry.PostingId).Take(take + 1).ToListAsync(cancellationToken).ConfigureAwait(false);
        string? nextCursor = null;
        if (results.Count > take)
        {
            var last = results[take - 1];
            nextCursor = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{last.CreatedAtUtc.UtcTicks}|{last.PostingId:D}"));
            results = results.Take(take).ToList();
        }

        return new LedgerEntryPageResponse(results.Select(MapEntry).ToArray(), nextCursor);
    }

    public async Task<LedgerIntegrityResponse> VerifyIntegrityAsync(CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();
        var now = _clock.UtcNow;

        var transactionChecks = await _dbContext.LedgerTransactions.AsNoTracking()
            .Select(transaction => new
            {
                transaction.Id,
                transaction.Currency,
                Debit = transaction.Postings.Where(posting => posting.Side == EntrySide.Debit).Sum(posting => posting.Amount),
                Credit = transaction.Postings.Where(posting => posting.Side == EntrySide.Credit).Sum(posting => posting.Amount),
                PostingCount = transaction.Postings.Count,
                MixedCurrency = transaction.Postings.Any(posting => !posting.Currency.Equals(transaction.Currency)),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var check in transactionChecks)
        {
            if (check.PostingCount < 2 || check.Debit != check.Credit || check.MixedCurrency)
            {
                errors.Add($"Transaction {check.Id:D} failed balance, count, or currency integrity.");
            }
        }

        var accountChecks = await _dbContext.BalanceProjections.AsNoTracking()
            .Join(_dbContext.LedgerAccounts.AsNoTracking(), balance => balance.LedgerAccountId, account => account.Id, (balance, account) => new { balance, account })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var item in accountChecks)
        {
            var totals = await _dbContext.Postings.AsNoTracking()
                .Where(posting => posting.LedgerAccountId == item.account.Id)
                .GroupBy(_ => 1)
                .Select(group => new { Debit = group.Where(posting => posting.Side == EntrySide.Debit).Sum(posting => posting.Amount), Credit = group.Where(posting => posting.Side == EntrySide.Credit).Sum(posting => posting.Amount) })
                .SingleOrDefaultAsync(cancellationToken)
                .ConfigureAwait(false);
            var debit = totals?.Debit ?? 0m;
            var credit = totals?.Credit ?? 0m;
            if (debit != item.balance.DebitTotal || credit != item.balance.CreditTotal)
            {
                errors.Add($"Projection mismatch for ledger account {item.account.Id:D}.");
            }
        }

        var invalidReversals = await _dbContext.Reversals.AsNoTracking()
            .CountAsync(reversal => !_dbContext.LedgerTransactions.Any(transaction => transaction.Id == reversal.OriginalTransactionId) || !_dbContext.LedgerTransactions.Any(transaction => transaction.Id == reversal.ReversalTransactionId), cancellationToken)
            .ConfigureAwait(false);
        if (invalidReversals > 0)
        {
            errors.Add($"{invalidReversals} reversal links are invalid.");
        }

        AddAudit(errors.Count == 0 ? "BalanceProjectionVerified" : "IntegrityCheckFailed", null, null, now, errors.Count == 0 ? null : "Integrity verification found mismatches", JsonSerializer.Serialize(new { errorCount = errors.Count }, SerializerOptions));
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return new LedgerIntegrityResponse(errors.Count == 0, transactionChecks.Count, accountChecks.Count, errors, now);
    }

    private async Task<LedgerTransactionResponse> PostTransactionCoreAsync(PostLedgerTransactionRequest request, string requestHash, LedgerTransaction? originalTransaction, string? reversalReason, CancellationToken cancellationToken)
    {
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;

        if (await _dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record => record.IdempotencyKey == request.ExternalReference, cancellationToken).ConfigureAwait(false) is { } existingRecord)
        {
            if (!string.Equals(existingRecord.RequestHash, requestHash, StringComparison.Ordinal))
            {
                throw new ConflictException("Idempotency key already exists with a different financial instruction.");
            }

            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return Map(await LoadTransactionAsync(existingRecord.TransactionId, cancellationToken).ConfigureAwait(false));
        }

        var currency = Currency.FromCode(request.Currency);
        var accountIds = request.Postings.Select(posting => posting.LedgerAccountId).Distinct().OrderBy(id => id).ToArray();
        var accounts = await _dbContext.LedgerAccounts.Where(account => accountIds.Contains(account.Id)).ToDictionaryAsync(account => account.Id, cancellationToken).ConfigureAwait(false);
        if (accounts.Count != accountIds.Length)
        {
            throw new NotFoundException("LedgerAccount", "one or more requested accounts");
        }

        foreach (var account in accounts.Values)
        {
            if (!account.Currency.Equals(currency))
            {
                throw new DomainException("ledger.transaction.account_currency_mismatch", "Posting account currency must match transaction currency.");
            }

            if (account.Status == LedgerAccountStatus.Closed)
            {
                throw new ConflictException($"Ledger account '{account.Id:D}' is closed.");
            }

            if (account.Status == LedgerAccountStatus.Frozen && originalTransaction is null)
            {
                throw new ConflictException($"Ledger account '{account.Id:D}' is frozen for normal postings.");
            }
        }

        foreach (var accountId in accountIds)
        {
            _ = await _dbContext.BalanceProjections.FromSqlInterpolated($"SELECT * FROM ledger.ledger_account_balances WHERE \"LedgerAccountId\" = {accountId} FOR UPDATE").SingleAsync(cancellationToken).ConfigureAwait(false);
        }

        var drafts = request.Postings.Select((posting, index) => new PostingDraft(posting.LedgerAccountId, Enum.Parse<EntrySide>(posting.Side, true), Money.Of(posting.Amount, currency), posting.Description, index + 1)).ToArray();
        var ledgerTransaction = LedgerTransaction.Post(request.ExternalReference, request.TransactionType, currency, request.Description, drafts, request.OccurredAtUtc ?? now, now, _requestContext.CorrelationId, _requestContext.CausationId, originalTransaction?.Id, reversalReason);
        _dbContext.LedgerTransactions.Add(ledgerTransaction);
        _dbContext.IdempotencyRecords.Add(LedgerIdempotencyRecord.Create(request.ExternalReference, requestHash, ledgerTransaction.Id, now));

        if (originalTransaction is not null)
        {
            _dbContext.Reversals.Add(LedgerReversal.Create(originalTransaction.Id, ledgerTransaction.Id, reversalReason ?? "Reversal", ActorId(), now));
        }

        foreach (var posting in ledgerTransaction.Postings)
        {
            var balance = await _dbContext.BalanceProjections.SingleAsync(item => item.LedgerAccountId == posting.LedgerAccountId, cancellationToken).ConfigureAwait(false);
            balance.Apply(posting, now);
        }

        AddAudit(originalTransaction is null ? "LedgerTransactionPosted" : "LedgerTransactionReversed", ledgerTransaction.Id, null, now, reversalReason, JsonSerializer.Serialize(new { ledgerTransaction.ExternalReference, ledgerTransaction.TransactionType, ledgerTransaction.Currency.Code, originalTransactionId = originalTransaction?.Id }, SerializerOptions));
        AddTransactionOutbox(ledgerTransaction, originalTransaction?.Id, reversalReason, now);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return Map(ledgerTransaction);
    }

    private async Task<LedgerTransactionResponse> ResolveIdempotentRetryAsync(string externalReference, string requestHash, CancellationToken cancellationToken)
    {
        var record = await _dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(item => item.IdempotencyKey == externalReference, cancellationToken).ConfigureAwait(false);
        if (record is null)
        {
            throw new ConflictException("Duplicate ledger request could not be resolved safely; retry with the same idempotency key.");
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new ConflictException("Idempotency key already exists with a different financial instruction.");
        }

        return Map(await LoadTransactionAsync(record.TransactionId, cancellationToken).ConfigureAwait(false));
    }

    private async Task<LedgerAccount> LoadAccountAsync(Guid ledgerAccountId, CancellationToken cancellationToken)
        => await _dbContext.LedgerAccounts.SingleOrDefaultAsync(account => account.Id == ledgerAccountId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("LedgerAccount", ledgerAccountId.ToString("D"));

    private async Task<LedgerTransaction> LoadTransactionAsync(Guid transactionId, CancellationToken cancellationToken)
        => await _dbContext.LedgerTransactions.Include(transaction => transaction.Postings).SingleOrDefaultAsync(transaction => transaction.Id == transactionId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("LedgerTransaction", transactionId.ToString("D"));

    private void AddAudit(string eventType, Guid? transactionId, Guid? ledgerAccountId, DateTimeOffset now, string? reason = null, string metadata = "{}")
        => _dbContext.LedgerAuditEvents.Add(LedgerAuditEvent.Create(eventType, ResolveActorType(), ActorId(), transactionId, ledgerAccountId, now, _requestContext.CorrelationId, _requestContext.CausationId, reason, metadata));

    private void AddAccountOutbox(LedgerAccount account, DateTimeOffset now)
    {
        var payload = new LedgerAccountCreatedIntegrationEvent(account.Id, account.ExternalReference, account.AccountCode, account.AccountType.ToString(), account.Currency.Code);
        var envelope = new IntegrationEventEnvelope<LedgerAccountCreatedIntegrationEvent>(Guid.NewGuid(), LedgerAccountCreatedIntegrationEvent.EventType, LedgerAccountCreatedIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "ledger-service", payload);
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(LedgerAccountsTopic, account.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "LedgerAccount", account.Id.ToString("D")));
    }

    private void AddTransactionOutbox(LedgerTransaction transaction, Guid? originalTransactionId, string? reversalReason, DateTimeOffset now)
    {
        if (originalTransactionId is null)
        {
            var payload = new LedgerTransactionPostedIntegrationEvent(transaction.Id, transaction.ExternalReference, transaction.TransactionType, transaction.Currency.Code, transaction.OccurredAtUtc);
            var envelope = new IntegrationEventEnvelope<LedgerTransactionPostedIntegrationEvent>(Guid.NewGuid(), LedgerTransactionPostedIntegrationEvent.EventType, LedgerTransactionPostedIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "ledger-service", payload);
            _dbContext.OutboxMessages.Add(OutboxMessage.Create(LedgerTransactionsTopic, transaction.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "LedgerTransaction", transaction.Id.ToString("D")));
            return;
        }

        var reversed = new LedgerTransactionReversedIntegrationEvent(originalTransactionId.Value, transaction.Id, transaction.ExternalReference, transaction.Currency.Code, reversalReason ?? "Reversal");
        var reversedEnvelope = new IntegrationEventEnvelope<LedgerTransactionReversedIntegrationEvent>(Guid.NewGuid(), LedgerTransactionReversedIntegrationEvent.EventType, LedgerTransactionReversedIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "ledger-service", reversed);
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(LedgerTransactionsTopic, transaction.Id.ToString("D"), reversedEnvelope.EventType, JsonSerializer.Serialize(reversedEnvelope, SerializerOptions), now, reversedEnvelope.EventId, reversedEnvelope.EventVersion, "LedgerTransaction", transaction.Id.ToString("D")));
    }

    private ActorType ResolveActorType()
    {
        if (_currentUser.Roles.Contains("Administrator", StringComparer.OrdinalIgnoreCase))
        {
            return ActorType.Administrator;
        }

        return _currentUser.IsAuthenticated ? ActorType.User : ActorType.Service;
    }

    private string ActorId() => _currentUser.UserId ?? "ledger-service";

    private static string ComputeRequestHash(PostLedgerTransactionRequest request)
    {
        var canonical = new
        {
            externalReference = request.ExternalReference.Trim(),
            transactionType = request.TransactionType.Trim(),
            currency = request.Currency.Trim().ToUpperInvariant(),
            postings = request.Postings.Select((posting, index) => new { sequence = index + 1, ledgerAccountId = posting.LedgerAccountId, side = posting.Side.Trim(), amount = decimal.Round(posting.Amount, 4), description = posting.Description ?? string.Empty }).ToArray(),
        };
        var json = JsonSerializer.Serialize(canonical, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
    }

    private static (long? Ticks, Guid? PostingId) ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return (null, null);
        }

        var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
        var parts = decoded.Split('|', 2);
        return parts.Length == 2 && long.TryParse(parts[0], out var ticks) && Guid.TryParse(parts[1], out var postingId) ? (ticks, postingId) : (null, null);
    }

    private static LedgerAccountResponse Map(LedgerAccount account) => new(account.Id, account.ExternalReference, account.AccountCode, account.AccountName, account.AccountType.ToString(), account.Currency.Code, account.Status.ToString(), account.CreatedAtUtc, account.ClosedAtUtc);

    private static LedgerTransactionResponse Map(LedgerTransaction transaction) => new(transaction.Id, transaction.ExternalReference, transaction.TransactionType, transaction.Currency.Code, transaction.Description, transaction.Status.ToString(), transaction.OccurredAtUtc, transaction.PostedAtUtc, transaction.OriginalTransactionId, transaction.ReversalReason, transaction.Postings.OrderBy(posting => posting.Sequence).Select(MapPosting).ToArray());

    private static PostingResponse MapPosting(Posting posting) => new(posting.Id, posting.LedgerAccountId, posting.Side.ToString(), posting.Amount, posting.Currency.Code, posting.Sequence, posting.Description, posting.CreatedAtUtc);

    private static LedgerEntryResponse MapEntry(LedgerEntryProjection entry) => new(entry.PostingId, entry.TransactionId, entry.LedgerAccountId, entry.ExternalReference, entry.TransactionType, entry.Side.ToString(), entry.Amount, entry.Currency, entry.Sequence, entry.Description, entry.OccurredAtUtc, entry.CreatedAtUtc);

    private sealed record LedgerEntryProjection(Guid PostingId, Guid TransactionId, Guid LedgerAccountId, string ExternalReference, string TransactionType, EntrySide Side, decimal Amount, string Currency, int Sequence, string? Description, DateTimeOffset OccurredAtUtc, DateTimeOffset CreatedAtUtc);
}
