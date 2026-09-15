using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Ledger.Domain.Ledger;

public enum LedgerAccountType { Asset = 1, Liability = 2, Equity = 3, Revenue = 4, Expense = 5 }
public enum LedgerAccountStatus { Active = 1, Frozen = 2, Closed = 3 }
public enum EntrySide { Debit = 1, Credit = 2 }
public enum LedgerTransactionStatus { Posted = 1 }
public enum ActorType { User = 1, Service = 2, SystemJob = 3, Administrator = 4 }

public readonly record struct Currency
{
    private static readonly HashSet<string> SupportedCodes = new(StringComparer.OrdinalIgnoreCase) { "NGN", "KES", "GHS", "USD" };

    public string Code { get; }

    private Currency(string code) => Code = code;

    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("ledger.currency.required", "Currency is required.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (normalized.Length != 3 || !SupportedCodes.Contains(normalized))
        {
            throw new DomainException("ledger.currency.unsupported", $"Currency '{code}' is not supported.");
        }

        return new Currency(normalized);
    }

    public override string ToString() => Code;
}

public readonly record struct Money
{
    public decimal Amount { get; }
    public Currency Currency { get; }

    private Money(decimal amount, Currency currency)
    {
        Amount = amount;
        Currency = currency;
    }

    public static Money Of(decimal amount, Currency currency)
    {
        if (amount <= 0m)
        {
            throw new DomainException("ledger.money.positive_required", "Posting amount must be greater than zero.");
        }

        if (decimal.Round(amount, 4) != amount)
        {
            throw new DomainException("ledger.money.scale_exceeded", "Money supports at most four decimal places.");
        }

        return new Money(amount, currency);
    }

    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        return new Money(Amount - other.Amount, Currency);
    }

    private void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new DomainException("ledger.money.currency_mismatch", "Money values must share the same currency.");
        }
    }
}

public sealed class LedgerAccount
{
    private LedgerAccount() { }

    private LedgerAccount(Guid id, string externalReference, string accountCode, string accountName, LedgerAccountType accountType, Currency currency, DateTimeOffset createdAtUtc)
    {
        Id = id;
        ExternalReference = externalReference.Trim();
        AccountCode = accountCode.Trim().ToUpperInvariant();
        AccountName = accountName.Trim();
        AccountType = accountType;
        Currency = currency;
        Status = LedgerAccountStatus.Active;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public string AccountCode { get; private set; } = string.Empty;
    public string AccountName { get; private set; } = string.Empty;
    public LedgerAccountType AccountType { get; private set; }
    public Currency Currency { get; private set; }
    public LedgerAccountStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }

    public bool IsDebitNormal => AccountType is LedgerAccountType.Asset or LedgerAccountType.Expense;

    public static LedgerAccount Create(string externalReference, string accountCode, string accountName, LedgerAccountType accountType, Currency currency, DateTimeOffset createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new DomainException("ledger.account.external_reference_required", "External reference is required.");
        }

        if (string.IsNullOrWhiteSpace(accountCode))
        {
            throw new DomainException("ledger.account.code_required", "Account code is required.");
        }

        if (string.IsNullOrWhiteSpace(accountName))
        {
            throw new DomainException("ledger.account.name_required", "Account name is required.");
        }

        return new LedgerAccount(Guid.NewGuid(), externalReference, accountCode, accountName, accountType, currency, createdAtUtc);
    }

    public void Freeze()
    {
        if (Status == LedgerAccountStatus.Closed)
        {
            throw new DomainException("ledger.account.closed", "Closed ledger accounts cannot be frozen.");
        }

        Status = LedgerAccountStatus.Frozen;
    }

    public void Close(DateTimeOffset closedAtUtc)
    {
        if (Status == LedgerAccountStatus.Closed)
        {
            return;
        }

        Status = LedgerAccountStatus.Closed;
        ClosedAtUtc = closedAtUtc;
    }
}

public sealed class LedgerTransaction
{
    private readonly List<Posting> _postings = [];

    private LedgerTransaction() { }

    private LedgerTransaction(Guid id, string externalReference, string transactionType, Currency currency, string description, DateTimeOffset occurredAtUtc, DateTimeOffset postedAtUtc, string correlationId, string? causationId, Guid? originalTransactionId, string? reversalReason)
    {
        Id = id;
        ExternalReference = externalReference.Trim();
        TransactionType = transactionType.Trim();
        Currency = currency;
        Description = description.Trim();
        Status = LedgerTransactionStatus.Posted;
        OccurredAtUtc = occurredAtUtc;
        PostedAtUtc = postedAtUtc;
        CorrelationId = correlationId;
        CausationId = causationId;
        OriginalTransactionId = originalTransactionId;
        ReversalReason = reversalReason;
    }

    public Guid Id { get; private set; }
    public string ExternalReference { get; private set; } = string.Empty;
    public string TransactionType { get; private set; } = string.Empty;
    public Currency Currency { get; private set; }
    public string Description { get; private set; } = string.Empty;
    public LedgerTransactionStatus Status { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset PostedAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public Guid? OriginalTransactionId { get; private set; }
    public string? ReversalReason { get; private set; }
    public IReadOnlyCollection<Posting> Postings => _postings.AsReadOnly();

    public static LedgerTransaction Post(string externalReference, string transactionType, Currency currency, string description, IEnumerable<PostingDraft> postingDrafts, DateTimeOffset occurredAtUtc, DateTimeOffset postedAtUtc, string correlationId, string? causationId, Guid? originalTransactionId = null, string? reversalReason = null)
    {
        if (string.IsNullOrWhiteSpace(externalReference))
        {
            throw new DomainException("ledger.transaction.external_reference_required", "External reference is required.");
        }

        if (string.IsNullOrWhiteSpace(transactionType))
        {
            throw new DomainException("ledger.transaction.type_required", "Transaction type is required.");
        }

        var drafts = postingDrafts.OrderBy(posting => posting.Sequence).ToArray();
        if (drafts.Length < 2)
        {
            throw new DomainException("ledger.transaction.minimum_postings", "A ledger transaction requires at least two postings.");
        }

        if (drafts.Any(posting => !posting.Money.Currency.Equals(currency)))
        {
            throw new DomainException("ledger.transaction.currency_mismatch", "All postings must share the transaction currency.");
        }

        var debitTotal = drafts.Where(posting => posting.Side == EntrySide.Debit).Sum(posting => posting.Money.Amount);
        var creditTotal = drafts.Where(posting => posting.Side == EntrySide.Credit).Sum(posting => posting.Money.Amount);
        if (debitTotal <= 0m || creditTotal <= 0m || debitTotal != creditTotal)
        {
            throw new DomainException("ledger.transaction.unbalanced", "Ledger transaction debits must equal credits.");
        }

        var transaction = new LedgerTransaction(Guid.NewGuid(), externalReference, transactionType, currency, description, occurredAtUtc, postedAtUtc, correlationId, causationId, originalTransactionId, reversalReason);
        var sequence = 1;
        foreach (var draft in drafts)
        {
            transaction._postings.Add(Posting.Create(transaction.Id, draft.LedgerAccountId, draft.Side, draft.Money, sequence++, draft.Description, postedAtUtc));
        }

        return transaction;
    }

    public LedgerTransaction CreateReversal(string externalReference, string reason, DateTimeOffset occurredAtUtc, DateTimeOffset postedAtUtc, string correlationId, string? causationId)
    {
        var inverse = _postings
            .OrderBy(posting => posting.Sequence)
            .Select(posting => new PostingDraft(posting.LedgerAccountId, posting.Side == EntrySide.Debit ? EntrySide.Credit : EntrySide.Debit, Money.Of(posting.Amount, posting.Currency), posting.Description, posting.Sequence));
        return Post(externalReference, "Reversal", Currency, $"Reversal of {ExternalReference}", inverse, occurredAtUtc, postedAtUtc, correlationId, causationId, Id, reason);
    }
}

public sealed record PostingDraft(Guid LedgerAccountId, EntrySide Side, Money Money, string? Description, int Sequence);

public sealed class Posting
{
    private Posting() { }

    private Posting(Guid id, Guid transactionId, Guid ledgerAccountId, EntrySide side, Money money, int sequence, string? description, DateTimeOffset createdAtUtc)
    {
        Id = id;
        TransactionId = transactionId;
        LedgerAccountId = ledgerAccountId;
        Side = side;
        Amount = money.Amount;
        Currency = money.Currency;
        Sequence = sequence;
        Description = description;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid TransactionId { get; private set; }
    public Guid LedgerAccountId { get; private set; }
    public EntrySide Side { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; }
    public int Sequence { get; private set; }
    public string? Description { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    internal static Posting Create(Guid transactionId, Guid ledgerAccountId, EntrySide side, Money money, int sequence, string? description, DateTimeOffset createdAtUtc)
        => new(Guid.NewGuid(), transactionId, ledgerAccountId, side, money, sequence, description, createdAtUtc);
}

public sealed class LedgerAccountBalance
{
    private LedgerAccountBalance() { }

    private LedgerAccountBalance(Guid ledgerAccountId, Currency currency, DateTimeOffset updatedAtUtc)
    {
        LedgerAccountId = ledgerAccountId;
        Currency = currency;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid LedgerAccountId { get; private set; }
    public Currency Currency { get; private set; }
    public decimal DebitTotal { get; private set; }
    public decimal CreditTotal { get; private set; }
    public long Version { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static LedgerAccountBalance Create(Guid ledgerAccountId, Currency currency, DateTimeOffset updatedAtUtc) => new(ledgerAccountId, currency, updatedAtUtc);

    public void Apply(Posting posting, DateTimeOffset updatedAtUtc)
    {
        if (!posting.Currency.Equals(Currency))
        {
            throw new DomainException("ledger.balance.currency_mismatch", "Posting currency must match balance currency.");
        }

        if (posting.Side == EntrySide.Debit)
        {
            DebitTotal += posting.Amount;
        }
        else
        {
            CreditTotal += posting.Amount;
        }

        Version++;
        UpdatedAtUtc = updatedAtUtc;
    }

    public decimal CalculateBalance(LedgerAccountType accountType)
        => accountType is LedgerAccountType.Asset or LedgerAccountType.Expense ? DebitTotal - CreditTotal : CreditTotal - DebitTotal;
}

public sealed class LedgerReversal
{
    private LedgerReversal() { }

    private LedgerReversal(Guid originalTransactionId, Guid reversalTransactionId, string reason, string reversedBy, DateTimeOffset reversedAtUtc)
    {
        OriginalTransactionId = originalTransactionId;
        ReversalTransactionId = reversalTransactionId;
        Reason = reason;
        ReversedBy = reversedBy;
        ReversedAtUtc = reversedAtUtc;
    }

    public Guid OriginalTransactionId { get; private set; }
    public Guid ReversalTransactionId { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public string ReversedBy { get; private set; } = string.Empty;
    public DateTimeOffset ReversedAtUtc { get; private set; }

    public static LedgerReversal Create(Guid originalTransactionId, Guid reversalTransactionId, string reason, string reversedBy, DateTimeOffset reversedAtUtc)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("ledger.reversal.reason_required", "Reversal reason is required.");
        }

        return new LedgerReversal(originalTransactionId, reversalTransactionId, reason.Trim(), reversedBy, reversedAtUtc);
    }
}

public sealed class LedgerIdempotencyRecord
{
    private LedgerIdempotencyRecord() { }

    private LedgerIdempotencyRecord(string idempotencyKey, string requestHash, Guid transactionId, DateTimeOffset createdAtUtc)
    {
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        TransactionId = transactionId;
        CreatedAtUtc = createdAtUtc;
    }

    public string IdempotencyKey { get; private set; } = string.Empty;
    public string RequestHash { get; private set; } = string.Empty;
    public Guid TransactionId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }

    public static LedgerIdempotencyRecord Create(string idempotencyKey, string requestHash, Guid transactionId, DateTimeOffset createdAtUtc)
        => new(idempotencyKey.Trim(), requestHash, transactionId, createdAtUtc);
}

public sealed class LedgerAuditEvent
{
    private LedgerAuditEvent() { }

    private LedgerAuditEvent(Guid id, string eventType, ActorType actorType, string? actorId, Guid? transactionId, Guid? ledgerAccountId, DateTimeOffset occurredAtUtc, string correlationId, string? causationId, string? reason, string metadata)
    {
        Id = id;
        EventType = eventType;
        ActorType = actorType;
        ActorId = actorId;
        TransactionId = transactionId;
        LedgerAccountId = ledgerAccountId;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        CausationId = causationId;
        Reason = reason;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public ActorType ActorType { get; private set; }
    public string? ActorId { get; private set; }
    public Guid? TransactionId { get; private set; }
    public Guid? LedgerAccountId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? CausationId { get; private set; }
    public string? Reason { get; private set; }
    public string Metadata { get; private set; } = "{}";

    public static LedgerAuditEvent Create(string eventType, ActorType actorType, string? actorId, Guid? transactionId, Guid? ledgerAccountId, DateTimeOffset occurredAtUtc, string correlationId, string? causationId, string? reason = null, string metadata = "{}")
        => new(Guid.NewGuid(), eventType, actorType, actorId, transactionId, ledgerAccountId, occurredAtUtc, correlationId, causationId, reason, metadata);
}

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(Guid id, string topic, string key, string eventType, string payload, DateTimeOffset occurredAtUtc)
    {
        Id = id;
        Topic = topic;
        Key = key;
        EventType = eventType;
        Payload = payload;
        OccurredAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string topic, string key, string eventType, string payload, DateTimeOffset occurredAtUtc) => new(Guid.NewGuid(), topic, key, eventType, payload, occurredAtUtc);

    public void MarkPublished(DateTimeOffset publishedAtUtc)
    {
        PublishedAtUtc = publishedAtUtc;
        LastError = null;
    }

    public void MarkFailed(string error)
    {
        AttemptCount++;
        LastError = error.Length > 1024 ? error[..1024] : error;
    }
}

public sealed class ProcessedIntegrationEvent
{
    private ProcessedIntegrationEvent() { }

    private ProcessedIntegrationEvent(Guid eventId, string eventType, DateTimeOffset processedAtUtc)
    {
        EventId = eventId;
        EventType = eventType;
        ProcessedAtUtc = processedAtUtc;
    }

    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public DateTimeOffset ProcessedAtUtc { get; private set; }

    public static ProcessedIntegrationEvent Create(Guid eventId, string eventType, DateTimeOffset processedAtUtc) => new(eventId, eventType, processedAtUtc);
}
