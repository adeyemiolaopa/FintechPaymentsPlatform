using System.Security.Cryptography;
using System.Text;
using Payments.BuildingBlocks.Domain.Primitives;

namespace Payments.Payment.Domain.Payments;

public enum PaymentType { InternalTransfer = 0, ExternalBankTransfer = 1 }
public enum PaymentStatus { Initiated = 0, PendingValidation = 1, FundsReserved = 2, Processing = 3, SubmittedToRail = 4, PendingReconciliation = 5, Completed = 6, Failed = 7, Rejected = 8, Cancelled = 9, ReversalPending = 10, Reversed = 11 }
public enum PaymentDestinationType { InternalAccount = 0, ExternalBank = 1 }
public enum PaymentActorType { Customer = 0, Service = 1, SystemWorker = 2, OperationsUser = 3 }
public enum RailSubmissionStatus { Pending = 0, Submitted = 1, Acknowledged = 2, Ambiguous = 3, Succeeded = 4, Failed = 5 }
public enum RailSubmissionOutcome { Pending = 0, Succeeded = 1, Failed = 2, Ambiguous = 3 }
public enum RailCallbackInboxStatus { Received = 0, Processed = 1, Duplicate = 2, Rejected = 3, Unmatched = 4, Conflict = 5 }
public enum PaymentFailureReasonCode
{
    None = 0,
    CustomerNotActive = 1,
    CustomerKycInsufficient = 2,
    SourceAccountNotFound = 3,
    DestinationAccountNotFound = 4,
    SourceAccountFrozen = 5,
    SourceAccountRestricted = 6,
    DestinationAccountRestricted = 7,
    InsufficientFunds = 8,
    CurrencyMismatch = 9,
    InvalidDestination = 10,
    AccountServiceUnavailable = 11,
    LedgerServiceUnavailable = 12,
    SystemFailure = 13,
    ExternalRailNotImplemented = 14,
    CancelledByCustomer = 15,
    CancelledByOperations = 16,
    ProviderUnavailable = 17,
    ProviderThrottled = 18,
    ProviderTimeout = 19,
    ProviderResponseAmbiguous = 20,
    ProviderDeclined = 21,
    ProviderCallbackInvalid = 22,
    ProviderStatusConflict = 23,
    ExternalLedgerClearingMissing = 24
}

public sealed record Currency
{
    private Currency(string code) => Code = code;
    public string Code { get; }

    public static Currency FromCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Trim().Length != 3 || !code.Trim().All(char.IsLetter))
        {
            throw new DomainException("payment.currency", "Currency must be a valid ISO-style 3-letter code.");
        }

        return new Currency(code.Trim().ToUpperInvariant());
    }

    public override string ToString() => Code;
}

public sealed record Money
{
    private Money(decimal amount, Currency currency)
    {
        Amount = decimal.Round(amount, 4);
        Currency = currency;
    }

    public decimal Amount { get; }
    public Currency Currency { get; }

    public static Money Of(decimal amount, Currency currency)
    {
        if (amount <= 0m)
        {
            throw new DomainException("payment.amount", "Payment amount must be greater than zero.");
        }

        return new Money(amount, currency);
    }
}

public sealed class PaymentDestination
{
    private PaymentDestination() { }

    private PaymentDestination(PaymentDestinationType destinationType, Guid? accountId, string? bankCode, string? accountNumber, string? accountName, string? countryCode)
    {
        DestinationType = destinationType;
        AccountId = accountId;
        BankCode = bankCode;
        AccountNumber = accountNumber;
        AccountName = accountName;
        CountryCode = countryCode;
    }

    public PaymentDestinationType DestinationType { get; private set; }
    public Guid? AccountId { get; private set; }
    public string? BankCode { get; private set; }
    public string? AccountNumber { get; private set; }
    public string? AccountName { get; private set; }
    public string? CountryCode { get; private set; }

    public static PaymentDestination InternalAccount(Guid accountId)
    {
        if (accountId == Guid.Empty)
        {
            throw new DomainException("payment.destination", "Internal account destination is required.");
        }

        return new PaymentDestination(PaymentDestinationType.InternalAccount, accountId, null, null, null, null);
    }

    public static PaymentDestination ExternalBank(string bankCode, string accountNumber, string accountName, string countryCode)
    {
        if (string.IsNullOrWhiteSpace(bankCode) || string.IsNullOrWhiteSpace(accountNumber) || string.IsNullOrWhiteSpace(accountName) || string.IsNullOrWhiteSpace(countryCode))
        {
            throw new DomainException("payment.destination", "External bank destination requires bank code, account number, account name, and country code.");
        }

        return new PaymentDestination(PaymentDestinationType.ExternalBank, null, bankCode.Trim().ToUpperInvariant(), accountNumber.Trim(), accountName.Trim(), countryCode.Trim().ToUpperInvariant());
    }
}

public sealed class Payment
{
    private readonly List<PaymentStateTransition> _stateTransitions = [];

    private Payment() { }

    private Payment(Guid id, Guid customerId, Guid sourceAccountId, PaymentType paymentType, Money amount, PaymentDestination destination, string reference, string? description, string externalReference, DateTimeOffset now, string correlationId, PaymentActorType actorType, string actorId)
    {
        if (customerId == Guid.Empty) throw new DomainException("payment.customer", "Customer is required.");
        if (sourceAccountId == Guid.Empty) throw new DomainException("payment.source_account", "Source account is required.");
        if (string.IsNullOrWhiteSpace(reference)) throw new DomainException("payment.reference", "Payment reference is required.");
        if (paymentType == PaymentType.InternalTransfer && destination.DestinationType != PaymentDestinationType.InternalAccount) throw new DomainException("payment.destination", "Internal transfers require an internal account destination.");
        if (paymentType == PaymentType.ExternalBankTransfer && destination.DestinationType != PaymentDestinationType.ExternalBank) throw new DomainException("payment.destination", "External bank transfers require an external bank destination.");
        if (paymentType == PaymentType.InternalTransfer && destination.AccountId == sourceAccountId) throw new DomainException("payment.same_account", "Internal transfers to the same account are not supported in Week 5.");

        Id = id;
        CustomerId = customerId;
        SourceAccountId = sourceAccountId;
        Destination = destination;
        PaymentType = paymentType;
        Amount = amount.Amount;
        Currency = amount.Currency;
        Reference = reference;
        Description = description?.Trim();
        Status = PaymentStatus.Initiated;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        CorrelationId = correlationId;
        ExternalReference = externalReference;
        AddTransition(null, PaymentStatus.Initiated, null, "Payment initiated", now, actorType, actorId, correlationId);
    }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid SourceAccountId { get; private set; }
    public PaymentDestination Destination { get; private set; } = null!;
    public PaymentType PaymentType { get; private set; }
    public decimal Amount { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public string Reference { get; private set; } = string.Empty;
    public string? Description { get; private set; }
    public PaymentStatus Status { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public DateTimeOffset? FailedAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string ExternalReference { get; private set; } = string.Empty;
    public Guid? FundsReservationId { get; private set; }
    public Guid? LedgerTransactionId { get; private set; }
    public Guid? ReversalLedgerTransactionId { get; private set; }
    public string? ReversalReason { get; private set; }
    public DateTimeOffset? ReversedAtUtc { get; private set; }
    public PaymentFailureReasonCode? ReasonCode { get; private set; }
    public string? ReasonDescription { get; private set; }
    public int Version { get; private set; }
    public IReadOnlyCollection<PaymentStateTransition> StateTransitions => _stateTransitions.AsReadOnly();

    public static Payment Create(Guid customerId, Guid sourceAccountId, PaymentType paymentType, Money amount, PaymentDestination destination, string reference, string? description, string externalReference, DateTimeOffset now, string correlationId, PaymentActorType actorType, string actorId)
        => new(Guid.NewGuid(), customerId, sourceAccountId, paymentType, amount, destination, reference, description, externalReference, now, correlationId, actorType, actorId);

    public void StartValidation(DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId) => TransitionTo(PaymentStatus.PendingValidation, null, "Validation started", now, actorType, actorId, correlationId);
    public void MarkFundsReserved(Guid reservationId, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        if (reservationId == Guid.Empty) throw new DomainException("payment.reservation", "Reservation id is required.");
        FundsReservationId = reservationId;
        TransitionTo(PaymentStatus.FundsReserved, null, "Funds reserved", now, actorType, actorId, correlationId);
    }

    public void StartProcessing(DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId) => TransitionTo(PaymentStatus.Processing, null, "Processing started", now, actorType, actorId, correlationId);

    public void MarkSubmittedToRail(DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
        => TransitionTo(PaymentStatus.SubmittedToRail, null, "Submitted to external rail", now, actorType, actorId, correlationId);

    public void MarkPendingReconciliation(PaymentFailureReasonCode? reasonCode, string reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        ReasonCode = reasonCode;
        ReasonDescription = reasonDescription;
        TransitionTo(PaymentStatus.PendingReconciliation, reasonCode, reasonDescription, now, actorType, actorId, correlationId);
    }

    public void MarkCompleted(Guid ledgerTransactionId, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        if (ledgerTransactionId == Guid.Empty) throw new DomainException("payment.ledger", "Ledger transaction id is required before completion.");
        LedgerTransactionId = ledgerTransactionId;
        CompletedAtUtc = now;
        TransitionTo(PaymentStatus.Completed, null, "Payment completed", now, actorType, actorId, correlationId);
    }

    public void StartReversal(string reason, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        if (Status == PaymentStatus.Reversed)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new DomainException("payment.reversal_reason", "Reversal reason is required.");
        }

        ReversalReason = reason.Trim();
        TransitionTo(PaymentStatus.ReversalPending, null, "Payment reversal started", now, actorType, actorId, correlationId);
    }

    public void MarkReversed(Guid reversalLedgerTransactionId, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        if (reversalLedgerTransactionId == Guid.Empty) throw new DomainException("payment.reversal_ledger", "Reversal ledger transaction id is required.");
        ReversalLedgerTransactionId = reversalLedgerTransactionId;
        ReversedAtUtc = now;
        TransitionTo(PaymentStatus.Reversed, null, "Payment reversed", now, actorType, actorId, correlationId);
    }

    public void MarkRejected(PaymentFailureReasonCode reasonCode, string reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        ReasonCode = reasonCode;
        ReasonDescription = reasonDescription;
        TransitionTo(PaymentStatus.Rejected, reasonCode, reasonDescription, now, actorType, actorId, correlationId);
    }

    public void MarkFailed(PaymentFailureReasonCode reasonCode, string reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        ReasonCode = reasonCode;
        ReasonDescription = reasonDescription;
        FailedAtUtc = now;
        TransitionTo(PaymentStatus.Failed, reasonCode, reasonDescription, now, actorType, actorId, correlationId);
    }

    public void Cancel(PaymentFailureReasonCode reasonCode, string reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        ReasonCode = reasonCode;
        ReasonDescription = reasonDescription;
        TransitionTo(PaymentStatus.Cancelled, reasonCode, reasonDescription, now, actorType, actorId, correlationId);
    }

    public bool IsRecoverable => Status is PaymentStatus.PendingValidation or PaymentStatus.FundsReserved or PaymentStatus.Processing or PaymentStatus.SubmittedToRail or PaymentStatus.PendingReconciliation or PaymentStatus.ReversalPending;

    private void TransitionTo(PaymentStatus toStatus, PaymentFailureReasonCode? reasonCode, string? reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
    {
        if (!IsValidTransition(Status, toStatus))
        {
            throw new DomainException("payment.invalid_transition", $"Payment cannot transition from {Status} to {toStatus}.");
        }

        var fromStatus = Status;
        Status = toStatus;
        UpdatedAtUtc = now;
        Version++;
        AddTransition(fromStatus, toStatus, reasonCode, reasonDescription, now, actorType, actorId, correlationId);
    }

    private void AddTransition(PaymentStatus? fromStatus, PaymentStatus toStatus, PaymentFailureReasonCode? reasonCode, string? reasonDescription, DateTimeOffset now, PaymentActorType actorType, string actorId, string correlationId)
        => _stateTransitions.Add(PaymentStateTransition.Create(Id, fromStatus, toStatus, reasonCode, reasonDescription, now, actorType, actorId, correlationId));

    public static bool IsValidTransition(PaymentStatus from, PaymentStatus to) => (from, to) switch
    {
        (PaymentStatus.Initiated, PaymentStatus.PendingValidation) => true,
        (PaymentStatus.Initiated, PaymentStatus.Cancelled) => true,
        (PaymentStatus.PendingValidation, PaymentStatus.FundsReserved) => true,
        (PaymentStatus.PendingValidation, PaymentStatus.Rejected) => true,
        (PaymentStatus.PendingValidation, PaymentStatus.Cancelled) => true,
        (PaymentStatus.FundsReserved, PaymentStatus.Processing) => true,
        (PaymentStatus.FundsReserved, PaymentStatus.Failed) => true,
        (PaymentStatus.FundsReserved, PaymentStatus.Cancelled) => true,
        (PaymentStatus.Processing, PaymentStatus.Completed) => true,
        (PaymentStatus.Processing, PaymentStatus.Failed) => true,
        (PaymentStatus.Processing, PaymentStatus.SubmittedToRail) => true,
        (PaymentStatus.Processing, PaymentStatus.PendingReconciliation) => true,
        (PaymentStatus.SubmittedToRail, PaymentStatus.PendingReconciliation) => true,
        (PaymentStatus.SubmittedToRail, PaymentStatus.Completed) => true,
        (PaymentStatus.SubmittedToRail, PaymentStatus.Failed) => true,
        (PaymentStatus.PendingReconciliation, PaymentStatus.Completed) => true,
        (PaymentStatus.PendingReconciliation, PaymentStatus.Failed) => true,
        (PaymentStatus.Completed, PaymentStatus.ReversalPending) => true,
        (PaymentStatus.ReversalPending, PaymentStatus.Reversed) => true,
        _ => false,
    };
}


public sealed class RailSubmission
{
    private RailSubmission() { }

    private RailSubmission(Guid paymentId, string provider, string market, string currency, string clientReference, string instructionHash, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        PaymentId = paymentId;
        Provider = Normalize(provider, 80);
        Market = Normalize(market, 16);
        Currency = global::Payments.Payment.Domain.Payments.Currency.FromCode(currency).Code;
        ClientReference = Normalize(clientReference, 128);
        InstructionHash = Normalize(instructionHash, 128);
        Status = RailSubmissionStatus.Pending;
        Outcome = RailSubmissionOutcome.Pending;
        CreatedAtUtc = now;
        UpdatedAtUtc = now;
        NextStatusCheckAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string Market { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public string ClientReference { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public string InstructionHash { get; private set; } = string.Empty;
    public RailSubmissionStatus Status { get; private set; }
    public RailSubmissionOutcome Outcome { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? SubmittedAtUtc { get; private set; }
    public DateTimeOffset? RespondedAtUtc { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ProviderMessage { get; private set; }
    public string? FailureCategory { get; private set; }
    public string? RawStatus { get; private set; }
    public DateTimeOffset? LastStatusCheckAtUtc { get; private set; }
    public DateTimeOffset? NextStatusCheckAtUtc { get; private set; }
    public int StatusCheckCount { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public static RailSubmission Create(Guid paymentId, string provider, string market, string currency, string clientReference, string instructionHash, DateTimeOffset now)
        => new(paymentId, provider, market, currency, clientReference, instructionHash, now);

    public int StartAttempt(DateTimeOffset now)
    {
        AttemptCount++;
        Status = RailSubmissionStatus.Submitted;
        SubmittedAtUtc ??= now;
        UpdatedAtUtc = now;
        return AttemptCount;
    }

    public void RecordOutcome(RailSubmissionOutcome outcome, string? providerReference, string? responseCode, string? providerMessage, string? rawStatus, DateTimeOffset now, TimeSpan? nextStatusDelay = null)
    {
        Outcome = outcome;
        ProviderReference = string.IsNullOrWhiteSpace(providerReference) ? ProviderReference : Normalize(providerReference, 128);
        ResponseCode = Truncate(responseCode, 64);
        ProviderMessage = Truncate(providerMessage, 512);
        RawStatus = Truncate(rawStatus, 80);
        RespondedAtUtc = now;
        UpdatedAtUtc = now;
        Status = outcome switch
        {
            RailSubmissionOutcome.Succeeded => RailSubmissionStatus.Succeeded,
            RailSubmissionOutcome.Failed => RailSubmissionStatus.Failed,
            RailSubmissionOutcome.Ambiguous => RailSubmissionStatus.Ambiguous,
            _ => RailSubmissionStatus.Acknowledged,
        };
        FailureCategory = outcome switch
        {
            RailSubmissionOutcome.Failed => "DefinitiveFailure",
            RailSubmissionOutcome.Ambiguous => "AmbiguousOutcome",
            _ => null,
        };
        NextStatusCheckAtUtc = outcome is RailSubmissionOutcome.Pending or RailSubmissionOutcome.Ambiguous
            ? now.Add(nextStatusDelay ?? TimeSpan.FromMinutes(2))
            : null;
    }

    public void RecordStatusCheck(DateTimeOffset now, TimeSpan nextStatusDelay)
    {
        LastStatusCheckAtUtc = now;
        StatusCheckCount++;
        NextStatusCheckAtUtc = now.Add(nextStatusDelay);
        UpdatedAtUtc = now;
    }

    private static string Normalize(string value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new DomainException("rail.required", "Rail submission field is required.");
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }

    private static string? Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        var trimmed = value.Trim();
        return trimmed.Length <= maxLength ? trimmed : trimmed[..maxLength];
    }
}

public sealed class RailSubmissionAttempt
{
    private RailSubmissionAttempt() { }

    private RailSubmissionAttempt(Guid railSubmissionId, Guid paymentId, string provider, int attemptNumber, string clientReference, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        RailSubmissionId = railSubmissionId;
        PaymentId = paymentId;
        Provider = provider;
        AttemptNumber = attemptNumber;
        ClientReference = clientReference;
        Outcome = RailSubmissionOutcome.Pending;
        Status = RailSubmissionStatus.Submitted;
        StartedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public Guid RailSubmissionId { get; private set; }
    public Guid PaymentId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public int AttemptNumber { get; private set; }
    public string ClientReference { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public RailSubmissionOutcome Outcome { get; private set; }
    public RailSubmissionStatus Status { get; private set; }
    public DateTimeOffset StartedAtUtc { get; private set; }
    public DateTimeOffset? CompletedAtUtc { get; private set; }
    public string? ResponseCode { get; private set; }
    public string? ProviderMessage { get; private set; }
    public string? RawStatus { get; private set; }
    public string? Error { get; private set; }

    public static RailSubmissionAttempt Create(Guid railSubmissionId, Guid paymentId, string provider, int attemptNumber, string clientReference, DateTimeOffset now)
        => new(railSubmissionId, paymentId, provider, attemptNumber, clientReference, now);

    public void Complete(RailSubmissionOutcome outcome, string? providerReference, string? responseCode, string? providerMessage, string? rawStatus, string? error, DateTimeOffset now)
    {
        Outcome = outcome;
        Status = outcome switch
        {
            RailSubmissionOutcome.Succeeded => RailSubmissionStatus.Succeeded,
            RailSubmissionOutcome.Failed => RailSubmissionStatus.Failed,
            RailSubmissionOutcome.Ambiguous => RailSubmissionStatus.Ambiguous,
            _ => RailSubmissionStatus.Acknowledged,
        };
        ProviderReference = providerReference;
        ResponseCode = responseCode;
        ProviderMessage = providerMessage;
        RawStatus = rawStatus;
        Error = error;
        CompletedAtUtc = now;
    }
}

public sealed class RailCallbackInbox
{
    private RailCallbackInbox() { }

    private RailCallbackInbox(string provider, string callbackEventId, string rawPayload, DateTimeOffset now)
    {
        Id = Guid.NewGuid();
        Provider = provider.Trim();
        CallbackEventId = callbackEventId.Trim();
        RawPayload = rawPayload;
        PayloadHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload)));
        Status = RailCallbackInboxStatus.Received;
        ReceivedAtUtc = now;
    }

    public Guid Id { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    public string CallbackEventId { get; private set; } = string.Empty;
    public string? ProviderReference { get; private set; }
    public string? ClientReference { get; private set; }
    public Guid? PaymentId { get; private set; }
    public RailCallbackInboxStatus Status { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public string RawPayload { get; private set; } = string.Empty;
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public string? FailureReason { get; private set; }

    public static RailCallbackInbox Receive(string provider, string callbackEventId, string rawPayload, DateTimeOffset now)
        => new(provider, callbackEventId, rawPayload, now);

    public void AttachReferences(Guid? paymentId, string? providerReference, string? clientReference)
    {
        PaymentId = paymentId;
        ProviderReference = providerReference;
        ClientReference = clientReference;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = RailCallbackInboxStatus.Processed;
        ProcessedAtUtc = now;
    }

    public void MarkRejected(string reason, DateTimeOffset now)
    {
        Status = RailCallbackInboxStatus.Rejected;
        FailureReason = reason.Length > 512 ? reason[..512] : reason;
        ProcessedAtUtc = now;
    }

    public void MarkUnmatched(string reason, DateTimeOffset now)
    {
        Status = RailCallbackInboxStatus.Unmatched;
        FailureReason = reason.Length > 512 ? reason[..512] : reason;
        ProcessedAtUtc = now;
    }

    public void MarkConflict(string reason, DateTimeOffset now)
    {
        Status = RailCallbackInboxStatus.Conflict;
        FailureReason = reason.Length > 512 ? reason[..512] : reason;
        ProcessedAtUtc = now;
    }
}
public sealed class PaymentStateTransition
{
    private PaymentStateTransition() { }

    private PaymentStateTransition(Guid id, Guid paymentId, PaymentStatus? fromStatus, PaymentStatus toStatus, PaymentFailureReasonCode? reasonCode, string? reasonDescription, DateTimeOffset occurredAtUtc, PaymentActorType actorType, string actorId, string correlationId)
    {
        Id = id;
        PaymentId = paymentId;
        FromStatus = fromStatus;
        ToStatus = toStatus;
        ReasonCode = reasonCode;
        ReasonDescription = reasonDescription;
        OccurredAtUtc = occurredAtUtc;
        ActorType = actorType;
        ActorId = actorId;
        CorrelationId = correlationId;
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public PaymentStatus? FromStatus { get; private set; }
    public PaymentStatus ToStatus { get; private set; }
    public PaymentFailureReasonCode? ReasonCode { get; private set; }
    public string? ReasonDescription { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public PaymentActorType ActorType { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;

    public static PaymentStateTransition Create(Guid paymentId, PaymentStatus? fromStatus, PaymentStatus toStatus, PaymentFailureReasonCode? reasonCode, string? reasonDescription, DateTimeOffset occurredAtUtc, PaymentActorType actorType, string actorId, string correlationId)
        => new(Guid.NewGuid(), paymentId, fromStatus, toStatus, reasonCode, reasonDescription, occurredAtUtc, actorType, actorId, correlationId);
}

public sealed class PaymentAuditEvent
{
    private PaymentAuditEvent() { }
    private PaymentAuditEvent(Guid id, Guid paymentId, string eventType, PaymentActorType actorType, string actorId, DateTimeOffset occurredAtUtc, string correlationId, string? reason, string metadata)
    {
        Id = id;
        PaymentId = paymentId;
        EventType = eventType;
        ActorType = actorType;
        ActorId = actorId;
        OccurredAtUtc = occurredAtUtc;
        CorrelationId = correlationId;
        Reason = reason;
        Metadata = metadata;
    }

    public Guid Id { get; private set; }
    public Guid PaymentId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public PaymentActorType ActorType { get; private set; }
    public string ActorId { get; private set; } = string.Empty;
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string CorrelationId { get; private set; } = string.Empty;
    public string? Reason { get; private set; }
    public string Metadata { get; private set; } = "{}";

    public static PaymentAuditEvent Create(Guid paymentId, string eventType, PaymentActorType actorType, string actorId, DateTimeOffset occurredAtUtc, string correlationId, string? reason = null, string metadata = "{}")
        => new(Guid.NewGuid(), paymentId, eventType, actorType, actorId, occurredAtUtc, correlationId, reason, metadata);
}

public enum OutboxMessageStatus { Pending = 0, Publishing = 1, Published = 2, Failed = 3 }

public sealed class OutboxMessage
{
    private OutboxMessage() { }

    private OutboxMessage(string topic, string key, string eventType, string payload, DateTimeOffset occurredAtUtc, Guid? eventId, int eventVersion, string? aggregateType, string? aggregateId, string? headers)
    {
        Id = Guid.NewGuid();
        EventId = eventId ?? Guid.NewGuid();
        EventVersion = eventVersion;
        AggregateType = string.IsNullOrWhiteSpace(aggregateType) ? InferAggregateType(topic) : aggregateType.Trim();
        AggregateId = string.IsNullOrWhiteSpace(aggregateId) ? key : aggregateId.Trim();
        Topic = topic;
        Key = key;
        PartitionKey = key;
        EventType = eventType;
        Payload = payload;
        Headers = string.IsNullOrWhiteSpace(headers) ? "{}" : headers;
        Status = OutboxMessageStatus.Pending;
        OccurredAtUtc = occurredAtUtc;
        CreatedAtUtc = occurredAtUtc;
        NextAttemptAtUtc = occurredAtUtc;
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public string AggregateType { get; private set; } = string.Empty;
    public string AggregateId { get; private set; } = string.Empty;
    public string Topic { get; private set; } = string.Empty;
    public string Key { get; private set; } = string.Empty;
    public string PartitionKey { get; private set; } = string.Empty;
    public string Payload { get; private set; } = string.Empty;
    public string Headers { get; private set; } = "{}";
    public OutboxMessageStatus Status { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset? PublishedAtUtc { get; private set; }
    public int AttemptCount { get; private set; }
    public DateTimeOffset? NextAttemptAtUtc { get; private set; }
    public DateTimeOffset? LastAttemptAtUtc { get; private set; }
    public string? LastError { get; private set; }

    public static OutboxMessage Create(string topic, string key, string eventType, string payload, DateTimeOffset occurredAtUtc, Guid? eventId = null, int eventVersion = 1, string? aggregateType = null, string? aggregateId = null, string? headers = null)
        => new(topic, key, eventType, payload, occurredAtUtc, eventId, eventVersion, aggregateType, aggregateId, headers);

    public void MarkPublished(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Published;
        PublishedAtUtc = now;
        LastAttemptAtUtc = now;
        NextAttemptAtUtc = null;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset now, int maxAttempts, TimeSpan retryDelay)
    {
        AttemptCount++;
        LastAttemptAtUtc = now;
        LastError = error.Length > 1024 ? error[..1024] : error;
        if (AttemptCount >= maxAttempts)
        {
            Status = OutboxMessageStatus.Failed;
            NextAttemptAtUtc = null;
            return;
        }

        Status = OutboxMessageStatus.Pending;
        NextAttemptAtUtc = now.Add(retryDelay);
    }

    public void ScheduleRetry(DateTimeOffset now)
    {
        Status = OutboxMessageStatus.Pending;
        NextAttemptAtUtc = now;
    }

    private static string InferAggregateType(string topic)
    {
        if (string.IsNullOrWhiteSpace(topic)) return "Unknown";
        var firstSegment = topic.Split('.', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstSegment) ? "Unknown" : firstSegment;
    }
}
public sealed class CustomerReference
{
    private CustomerReference() { }
    private CustomerReference(Guid customerId, string status, string kycStatus, DateTimeOffset updatedAtUtc)
    {
        CustomerId = customerId;
        Status = status;
        KycStatus = kycStatus;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid CustomerId { get; private set; }
    public string Status { get; private set; } = string.Empty;
    public string KycStatus { get; private set; } = string.Empty;
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool CanInitiatePayments => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase) && string.Equals(KycStatus, "BasicVerified", StringComparison.OrdinalIgnoreCase);

    public static CustomerReference Upsert(Guid customerId, string status, string kycStatus, DateTimeOffset now) => new(customerId, status, kycStatus, now);
    public void Update(string status, string kycStatus, DateTimeOffset now) { Status = status; KycStatus = kycStatus; UpdatedAtUtc = now; }
}

public sealed class AccountReference
{
    private AccountReference() { }
    private AccountReference(Guid accountId, Guid customerId, string currency, string accountType, string status, Guid? ledgerAccountId, DateTimeOffset updatedAtUtc)
    {
        AccountId = accountId;
        CustomerId = customerId;
        Currency = Currency.FromCode(currency);
        AccountType = accountType;
        Status = status;
        LedgerAccountId = ledgerAccountId;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid AccountId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Currency Currency { get; private set; } = null!;
    public string AccountType { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;
    public Guid? LedgerAccountId { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }
    public bool IsActive => string.Equals(Status, "Active", StringComparison.OrdinalIgnoreCase);

    public static AccountReference Upsert(Guid accountId, Guid customerId, string currency, string accountType, string status, Guid? ledgerAccountId, DateTimeOffset now) => new(accountId, customerId, currency, accountType, status, ledgerAccountId, now);
    public void Update(string currency, string accountType, string status, DateTimeOffset now) { Currency = Currency.FromCode(currency); AccountType = accountType; Status = status; UpdatedAtUtc = now; }
    public void AttachLedgerAccount(Guid ledgerAccountId, DateTimeOffset now) { LedgerAccountId = ledgerAccountId; UpdatedAtUtc = now; }
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
