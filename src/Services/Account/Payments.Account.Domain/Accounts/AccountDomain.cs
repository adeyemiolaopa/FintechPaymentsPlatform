using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Account.Domain.Accounts;

public enum AccountType { Wallet = 0, Current = 1, Savings = 2, Settlement = 3, Internal = 4 }
public enum AccountStatus { Pending = 0, Active = 1, Frozen = 2, Closed = 3 }
public enum AccountRestrictionType { DebitBlocked = 0, CreditBlocked = 1, TransferBlocked = 2, WithdrawalBlocked = 3 }
public enum FundsReservationStatus { Active = 0, Released = 1, Committed = 2, Expired = 3 }
public enum BeneficiaryType { Internal = 0, ExternalBank = 1 }
public enum BeneficiaryStatus { Active = 0, Removed = 1 }
public enum CustomerReferenceStatus { Pending = 0, Active = 1, Suspended = 2, Closed = 3 }
public enum CustomerReferenceKycStatus { NotStarted = 0, Pending = 1, Verified = 2, Rejected = 3, NeedsReview = 4 }
public enum AccountLimitType { MaximumBalance = 0, MaximumReservation = 1, DailyDebit = 2, DailyCredit = 3, MaximumAccounts = 4 }

public sealed class Currency : ValueObject
{
    private static readonly IReadOnlyDictionary<string, int> MinorUnits = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        ["NGN"] = 2,
        ["KES"] = 2,
        ["GHS"] = 2,
        ["USD"] = 2,
    };

    private Currency(string code)
    {
        Code = code;
        MinorUnit = MinorUnits[code];
    }

    public string Code { get; private set; } = string.Empty;
    public int MinorUnit { get; private set; }
    public static IReadOnlyCollection<string> SupportedCodes => MinorUnits.Keys.OrderBy(code => code).ToArray();

    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || !Regex.IsMatch(code.Trim(), "^[A-Z]{3}$"))
        {
            throw new DomainException("currency.invalid", "Currency must be a three-letter ISO 4217-style code.");
        }

        var normalized = code.Trim().ToUpperInvariant();
        if (!MinorUnits.ContainsKey(normalized))
        {
            throw new DomainException("currency.unsupported", $"Currency '{normalized}' is not supported.");
        }

        return new Currency(normalized);
    }

    public void EnsureValidAmount(decimal amount)
    {
        if (amount < 0)
        {
            throw new DomainException("money.negative", "Money amount cannot be negative.");
        }

        var scale = GetScale(amount);
        if (scale > MinorUnit)
        {
            throw new DomainException("money.precision", $"Currency {Code} supports at most {MinorUnit} decimal places.");
        }
    }

    private static int GetScale(decimal value)
    {
        var bits = decimal.GetBits(value);
        return (bits[3] >> 16) & 0x7F;
    }



    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Code;
    }

    public override string ToString() => Code;
}

public sealed class Money : ValueObject
{
    private Money(decimal amount, Currency currency)
    {
        currency.EnsureValidAmount(amount);
        Amount = amount;
        Currency = currency;
    }

    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.FromCode("NGN");

    public static Money Of(decimal amount, Currency currency) => new(amount, currency);
    public static Money Zero(Currency currency) => new(0m, currency);

    public Money Add(Money other)
    {
        EnsureSameCurrency(other);
        return Of(Amount + other.Amount, Currency);
    }

    public Money Subtract(Money other)
    {
        EnsureSameCurrency(other);
        if (Amount < other.Amount)
        {
            throw new DomainException("money.insufficient", "Money subtraction would create a negative amount.");
        }

        return Of(Amount - other.Amount, Currency);
    }

    public void EnsureSameCurrency(Money other)
    {
        if (!Currency.Equals(other.Currency))
        {
            throw new DomainException("money.currency_mismatch", "Cannot operate on money with different currencies.");
        }
    }



    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Amount;
        yield return Currency;
    }
}

public sealed class AccountNumber : ValueObject
{
    private static readonly Regex Format = new("^9[0-9]{9}$", RegexOptions.Compiled);

    private AccountNumber(string value) => Value = value;

    public string Value { get; private set; } = string.Empty;

    public static AccountNumber Create(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Format.IsMatch(value.Trim()))
        {
            throw new DomainException("account_number.invalid", "Account number format is invalid.");
        }

        return new AccountNumber(value.Trim());
    }

    public static AccountNumber Generate()
    {
        Span<byte> bytes = stackalloc byte[8];
        RandomNumberGenerator.Fill(bytes);
        var number = BitConverter.ToUInt64(bytes) % 1_000_000_000UL;
        return new AccountNumber($"9{number:000000000}");
    }



    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value;
}

public sealed class Account : AggregateRoot<Guid>
{
    private Account() : base(Guid.Empty) { }

    private Account(Guid id, Guid customerId, AccountNumber accountNumber, string accountName, Currency currency, AccountType accountType, DateTimeOffset now)
        : base(id)
    {
        CustomerId = customerId;
        AccountNumber = accountNumber;
        AccountName = accountName;
        Currency = currency;
        AccountType = accountType;
        Status = AccountStatus.Active;
        LedgerBalance = 0m;
        ReservedBalance = 0m;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        RaiseDomainEvent(new AccountCreatedDomainEvent(Guid.NewGuid(), id, customerId, currency.Code, accountType.ToString(), now));
    }

    public Guid CustomerId { get; private set; }
    public AccountNumber AccountNumber { get; private set; } = AccountNumber.Create("9000000000");
    public string AccountName { get; private set; } = string.Empty;
    public Currency Currency { get; private set; } = Currency.FromCode("NGN");
    public AccountType AccountType { get; private set; }
    public AccountStatus Status { get; private set; }
    public decimal LedgerBalance { get; private set; }
    public decimal ReservedBalance { get; private set; }
    public decimal AvailableBalance => LedgerBalance - ReservedBalance;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? ClosedAtUtc { get; private set; }
    public int Version { get; private set; }

    public static Account OpenWallet(Guid customerId, AccountNumber accountNumber, Currency currency, DateTimeOffset now)
        => new(Guid.NewGuid(), customerId, accountNumber, $"{currency.Code} Wallet", currency, AccountType.Wallet, now);

    public void SeedLedgerBalanceForTest(Money amount, DateTimeOffset now)
    {
        EnsureCurrency(amount);
        LedgerBalance = amount.Amount;
        Touch(now);
    }

    public void Freeze(Guid actorUserId, string reason, DateTimeOffset now)
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account.closed", "Closed accounts cannot be frozen.");
        }

        if (Status == AccountStatus.Frozen)
        {
            return;
        }

        Status = AccountStatus.Frozen;
        Touch(now);
        RaiseDomainEvent(new AccountFrozenDomainEvent(Guid.NewGuid(), Id, CustomerId, actorUserId, reason, now));
    }

    public void Unfreeze(Guid actorUserId, DateTimeOffset now)
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account.closed", "Closed accounts cannot be unfrozen.");
        }

        if (Status != AccountStatus.Frozen)
        {
            return;
        }

        Status = AccountStatus.Active;
        Touch(now);
        RaiseDomainEvent(new AccountUnfrozenDomainEvent(Guid.NewGuid(), Id, CustomerId, actorUserId, now));
    }

    public void Close(Guid actorUserId, DateTimeOffset now)
    {
        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account.closed", "Account is already closed.");
        }

        if (LedgerBalance != 0m || ReservedBalance != 0m)
        {
            throw new DomainException("account.close_balance", "Account can only be closed when ledger and reserved balances are zero.");
        }

        Status = AccountStatus.Closed;
        ClosedAtUtc = now;
        Touch(now);
        RaiseDomainEvent(new AccountClosedDomainEvent(Guid.NewGuid(), Id, CustomerId, actorUserId, now));
    }

    public FundsReservation ReserveFunds(string referenceId, Money amount, DateTimeOffset now, DateTimeOffset expiresAtUtc, bool debitBlocked)
    {
        EnsureCanReserve(amount, debitBlocked, now, expiresAtUtc);
        ReservedBalance += amount.Amount;
        Touch(now);
        var reservation = FundsReservation.Create(Id, referenceId, amount, now, expiresAtUtc);
        RaiseDomainEvent(new FundsReservedDomainEvent(Guid.NewGuid(), Id, CustomerId, reservation.Id, referenceId, amount.Amount, Currency.Code, now));
        return reservation;
    }

    public void ReleaseReservation(FundsReservation reservation, DateTimeOffset now)
    {
        if (reservation.AccountId != Id)
        {
            throw new DomainException("reservation.account_mismatch", "Reservation does not belong to this account.");
        }

        if (reservation.Status == FundsReservationStatus.Released)
        {
            return;
        }

        if (reservation.Status == FundsReservationStatus.Committed)
        {
            throw new DomainException("reservation.committed", "Committed reservations cannot be released.");
        }

        reservation.Release(now);
        ReservedBalance -= reservation.Amount;
        if (ReservedBalance < 0m)
        {
            throw new DomainException("account.reserved_negative", "Reserved balance cannot become negative.");
        }

        Touch(now);
        RaiseDomainEvent(new FundsReleasedDomainEvent(Guid.NewGuid(), Id, CustomerId, reservation.Id, reservation.ReferenceId, reservation.Amount, Currency.Code, now));
    }

    public void CommitReservation(FundsReservation reservation, DateTimeOffset now)
    {
        if (reservation.AccountId != Id)
        {
            throw new DomainException("reservation.account_mismatch", "Reservation does not belong to this account.");
        }

        if (reservation.Status == FundsReservationStatus.Committed)
        {
            return;
        }

        if (reservation.Status != FundsReservationStatus.Active)
        {
            throw new DomainException("reservation.inactive", "Only active reservations can be committed.");
        }

        reservation.Commit(now);
        ReservedBalance -= reservation.Amount;
        if (ReservedBalance < 0m)
        {
            throw new DomainException("account.reserved_negative", "Reserved balance cannot become negative.");
        }

        Touch(now);
    }

    private void EnsureCanReserve(Money amount, bool debitBlocked, DateTimeOffset now, DateTimeOffset expiresAtUtc)
    {
        EnsureCurrency(amount);
        if (Status == AccountStatus.Frozen)
        {
            throw new DomainException("account.frozen", "Frozen accounts cannot reserve funds.");
        }

        if (Status == AccountStatus.Closed)
        {
            throw new DomainException("account.closed", "Closed accounts cannot reserve funds.");
        }

        if (debitBlocked)
        {
            throw new DomainException("account.debit_blocked", "Debit-blocked accounts cannot reserve funds.");
        }

        if (amount.Amount <= 0m)
        {
            throw new DomainException("reservation.amount", "Reservation amount must be greater than zero.");
        }

        if (expiresAtUtc <= now)
        {
            throw new DomainException("reservation.expiry", "Reservation expiry must be in the future.");
        }

        if (AvailableBalance < amount.Amount)
        {
            throw new DomainException("account.insufficient_funds", "Available balance is insufficient for this reservation.");
        }
    }

    private void EnsureCurrency(Money amount)
    {
        if (!amount.Currency.Equals(Currency))
        {
            throw new DomainException("account.currency_mismatch", "Money currency must match account currency.");
        }
    }

    private void Touch(DateTimeOffset now)
    {
        UpdatedAtUtc = now;
        Version++;
    }
}

public sealed class AccountRestriction
{
    private AccountRestriction() { }

    private AccountRestriction(Guid accountId, AccountRestrictionType type, string reason, Guid actorUserId, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        AccountId = accountId;
        RestrictionType = type;
        Reason = reason.Trim();
        AppliedAtUtc = now;
        AppliedBy = actorUserId;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public AccountRestrictionType RestrictionType { get; private set; }
    public string Reason { get; private set; } = string.Empty;
    public DateTimeOffset AppliedAtUtc { get; private set; }
    public Guid AppliedBy { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }
    public Guid? RemovedBy { get; private set; }
    public bool IsActive => RemovedAtUtc is null;

    public static AccountRestriction Apply(Guid accountId, AccountRestrictionType type, string reason, Guid actorUserId, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("restriction.reason", "Restriction reason is required.");
        }

        return new AccountRestriction(accountId, type, reason, actorUserId, now);
    }

    public void Remove(Guid actorUserId, DateTimeOffset now)
    {
        if (RemovedAtUtc is not null)
        {
            return;
        }

        RemovedAtUtc = now;
        RemovedBy = actorUserId;
    }
}

public sealed class FundsReservation
{
    private FundsReservation() { }

    private FundsReservation(Guid accountId, string referenceId, Money amount, DateTimeOffset now, DateTimeOffset expiresAtUtc)
    {
        Id = Guid.NewGuid();
        AccountId = accountId;
        ReferenceId = NormalizeReference(referenceId);
        Amount = amount.Amount;
        Currency = amount.Currency;
        Status = FundsReservationStatus.Active;
        CreatedAtUtc = now;
        ExpiresAtUtc = expiresAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public string ReferenceId { get; private set; } = string.Empty;
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.FromCode("NGN");
    public FundsReservationStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? ReleasedAtUtc { get; private set; }
    public DateTimeOffset? CommittedAtUtc { get; private set; }
    public DateTimeOffset? ExpiredAtUtc { get; private set; }

    public static FundsReservation Create(Guid accountId, string referenceId, Money amount, DateTimeOffset now, DateTimeOffset expiresAtUtc)
    {
        if (amount.Amount <= 0m)
        {
            throw new DomainException("reservation.amount", "Reservation amount must be greater than zero.");
        }

        return new FundsReservation(accountId, referenceId, amount, now, expiresAtUtc);
    }

    public void Release(DateTimeOffset now)
    {
        if (Status == FundsReservationStatus.Released)
        {
            return;
        }

        if (Status == FundsReservationStatus.Committed)
        {
            throw new DomainException("reservation.committed", "Committed reservations cannot be released.");
        }

        Status = FundsReservationStatus.Released;
        ReleasedAtUtc = now;
    }

    public void Commit(DateTimeOffset now)
    {
        if (Status == FundsReservationStatus.Committed)
        {
            return;
        }

        if (Status != FundsReservationStatus.Active)
        {
            throw new DomainException("reservation.inactive", "Only active reservations can be committed.");
        }

        Status = FundsReservationStatus.Committed;
        CommittedAtUtc = now;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != FundsReservationStatus.Active)
        {
            return;
        }

        Status = FundsReservationStatus.Expired;
        ExpiredAtUtc = now;
    }

    private static string NormalizeReference(string referenceId)
    {
        if (string.IsNullOrWhiteSpace(referenceId) || referenceId.Length > 128)
        {
            throw new DomainException("reservation.reference", "Reservation reference is required and must be at most 128 characters.");
        }

        return referenceId.Trim();
    }
}

public sealed class Beneficiary
{
    private Beneficiary() { }

    private Beneficiary(Guid customerId, BeneficiaryType type, string name, string? bankCode, string accountNumber, Currency currency, string countryCode, string? nickname, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        CustomerId = customerId;
        Type = type;
        Name = NormalizeName(name);
        BankCode = NormalizeBankCode(bankCode, type);
        AccountNumber = NormalizeAccountNumber(accountNumber);
        Currency = currency;
        CountryCode = NormalizeCountry(countryCode);
        Nickname = string.IsNullOrWhiteSpace(nickname) ? null : nickname.Trim();
        Status = BeneficiaryStatus.Active;
        CreatedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public BeneficiaryType Type { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? BankCode { get; private set; }
    public string AccountNumber { get; private set; } = string.Empty;
    public Currency Currency { get; private set; } = Currency.FromCode("NGN");
    public string CountryCode { get; private set; } = string.Empty;
    public string? Nickname { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? RemovedAtUtc { get; private set; }
    public BeneficiaryStatus Status { get; private set; }

    public static Beneficiary Create(Guid customerId, BeneficiaryType type, string name, string? bankCode, string accountNumber, Currency currency, string countryCode, string? nickname, DateTimeOffset now)
        => new(customerId, type, name, bankCode, accountNumber, currency, countryCode, nickname, now);

    public void Remove(DateTimeOffset now)
    {
        if (Status == BeneficiaryStatus.Removed)
        {
            return;
        }

        Status = BeneficiaryStatus.Removed;
        RemovedAtUtc = now;
    }

    private static string NormalizeName(string name) => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 120 ? throw new DomainException("beneficiary.name", "Beneficiary name is required and must be at most 120 characters.") : name.Trim();
    private static string NormalizeCountry(string countryCode) => string.IsNullOrWhiteSpace(countryCode) || !Regex.IsMatch(countryCode.Trim(), "^[A-Z]{2}$") ? throw new DomainException("beneficiary.country", "Country code must be two uppercase letters.") : countryCode.Trim().ToUpperInvariant();
    private static string NormalizeAccountNumber(string accountNumber) => string.IsNullOrWhiteSpace(accountNumber) || !Regex.IsMatch(accountNumber.Trim(), "^[0-9]{6,20}$") ? throw new DomainException("beneficiary.account_number", "Beneficiary account number format is invalid.") : accountNumber.Trim();
    private static string? NormalizeBankCode(string? bankCode, BeneficiaryType type)
    {
        if (type == BeneficiaryType.ExternalBank && (string.IsNullOrWhiteSpace(bankCode) || !Regex.IsMatch(bankCode.Trim(), "^[0-9A-Z]{2,12}$")))
        {
            throw new DomainException("beneficiary.bank_code", "External bank beneficiaries require a valid bank code.");
        }

        return string.IsNullOrWhiteSpace(bankCode) ? null : bankCode.Trim().ToUpperInvariant();
    }
}

public sealed class AccountLimit
{
    private AccountLimit() { }

    private AccountLimit(Guid accountId, AccountLimitType limitType, Money amount, DateTimeOffset now, DateTimeOffset? effectiveToUtc)
    {
        Id = Guid.NewGuid();
        AccountId = accountId;
        LimitType = limitType;
        Amount = amount.Amount;
        Currency = amount.Currency;
        EffectiveFromUtc = now;
        EffectiveToUtc = effectiveToUtc;
    }

    public Guid Id { get; private set; }
    public Guid AccountId { get; private set; }
    public AccountLimitType LimitType { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = Currency.FromCode("NGN");
    public DateTimeOffset EffectiveFromUtc { get; private set; }
    public DateTimeOffset? EffectiveToUtc { get; private set; }

    public static AccountLimit Create(Guid accountId, AccountLimitType limitType, Money amount, DateTimeOffset now, DateTimeOffset? effectiveToUtc = null) => new(accountId, limitType, amount, now, effectiveToUtc);
}

public sealed class CustomerReference
{
    private CustomerReference() { }

    private CustomerReference(Guid customerId, CustomerReferenceStatus status, CustomerReferenceKycStatus kycStatus, DateTimeOffset updatedAtUtc)
    {
        CustomerId = customerId;
        Status = status;
        KycStatus = kycStatus;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid CustomerId { get; private set; }
    public CustomerReferenceStatus Status { get; private set; }
    public CustomerReferenceKycStatus KycStatus { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool CanOpenAccount => Status == CustomerReferenceStatus.Active && KycStatus is CustomerReferenceKycStatus.Verified or CustomerReferenceKycStatus.NotStarted;
    public bool CanReserve => Status == CustomerReferenceStatus.Active;

    public static CustomerReference Upsert(Guid customerId, string status, string kycStatus, DateTimeOffset updatedAtUtc)
    {
        return new CustomerReference(customerId, ParseStatus(status), ParseKyc(kycStatus), updatedAtUtc);
    }

    public void Apply(string status, string kycStatus, DateTimeOffset updatedAtUtc)
    {
        if (updatedAtUtc < UpdatedAtUtc)
        {
            return;
        }

        Status = ParseStatus(status);
        KycStatus = ParseKyc(kycStatus);
        UpdatedAtUtc = updatedAtUtc;
    }

    private static CustomerReferenceStatus ParseStatus(string status) => Enum.TryParse<CustomerReferenceStatus>(status, true, out var parsed) ? parsed : CustomerReferenceStatus.Pending;
    private static CustomerReferenceKycStatus ParseKyc(string kycStatus) => Enum.TryParse<CustomerReferenceKycStatus>(kycStatus, true, out var parsed) ? parsed : CustomerReferenceKycStatus.NotStarted;
}

public sealed class AccountAuditEvent
{
    private AccountAuditEvent() { }

    private AccountAuditEvent(string eventType, Guid? accountId, Guid customerId, Guid? actorUserId, DateTimeOffset now, string correlationId, string? reason, string metadata)
    {
        Id = Guid.NewGuid();
        EventType = eventType;
        AccountId = accountId;
        CustomerId = customerId;
        ActorUserId = actorUserId;
        OccurredAtUtc = now;
        CorrelationId = correlationId;
        Reason = reason;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }
    public Guid? AccountId { get; private set; }
    public Guid CustomerId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public Guid? ActorUserId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string Metadata { get; private set; } = "{}";

    public static AccountAuditEvent Create(string eventType, Guid? accountId, Guid customerId, Guid? actorUserId, DateTimeOffset now, string correlationId, string? reason = null, string metadata = "{}")
        => new(eventType, accountId, customerId, actorUserId, now, correlationId, reason, metadata);
}

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(string topic, string key, string eventType, string payload, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Topic = topic;
        Key = key;
        EventType = eventType;
        Payload = payload;
        OccurredAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string EventType { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string topic, string key, string eventType, string payload, DateTimeOffset now) => new(topic, key, eventType, payload, now);
    public void MarkPublished(DateTimeOffset now) { PublishedAtUtc = now; LastError = null; }
    public void MarkFailed(string error) => LastError = error.Length > 1024 ? error[..1024] : error;
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

public sealed record AccountCreatedDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, string Currency, string AccountType, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record AccountFrozenDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, Guid ActorUserId, string Reason, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record AccountUnfrozenDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record AccountClosedDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, Guid ActorUserId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record FundsReservedDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, Guid ReservationId, string ReferenceId, decimal Amount, string Currency, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record FundsReleasedDomainEvent(Guid EventId, Guid AccountId, Guid CustomerId, Guid ReservationId, string ReferenceId, decimal Amount, string Currency, DateTimeOffset OccurredAtUtc) : IDomainEvent;
public sealed record BeneficiaryAddedDomainEvent(Guid EventId, Guid BeneficiaryId, Guid CustomerId, DateTimeOffset OccurredAtUtc) : IDomainEvent;
