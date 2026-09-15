using System.Data;
using System.Diagnostics.Metrics;
using System.Text.Json;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Application.Exceptions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Payment.Application.Payments;
using Payments.Payment.Domain.Payments;
using Payments.Payment.Infrastructure;
using Payments.Payment.Infrastructure.Persistence;
using DomainException = Payments.BuildingBlocks.Domain.Primitives.DomainException;

namespace Payments.Payment.Infrastructure.Services;

public sealed class PaymentService : IPaymentService
{
    private const string PaymentsTopic = "payments.lifecycle.v1";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly Meter Meter = new("Payments.Payment");
    private static readonly Counter<long> PaymentsInitiated = Meter.CreateCounter<long>("payments_initiated_total");
    private static readonly Counter<long> PaymentsCompleted = Meter.CreateCounter<long>("payments_completed_total");
    private static readonly Counter<long> PaymentsRejected = Meter.CreateCounter<long>("payments_rejected_total");
    private static readonly Counter<long> PaymentsFailed = Meter.CreateCounter<long>("payments_failed_total");
    private static readonly Counter<long> PaymentsCancelled = Meter.CreateCounter<long>("payments_cancelled_total");
    private static readonly Counter<long> IdempotencyRequests = Meter.CreateCounter<long>("idempotency_requests_total");
    private static readonly Counter<long> IdempotencyNew = Meter.CreateCounter<long>("idempotency_new_total");
    private static readonly Counter<long> IdempotencyReplayed = Meter.CreateCounter<long>("idempotency_replayed_total");
    private static readonly Counter<long> IdempotencyConflicts = Meter.CreateCounter<long>("idempotency_conflicts_total");
    private static readonly Counter<long> IdempotencyProcessing = Meter.CreateCounter<long>("idempotency_processing_total");
    private static readonly Counter<long> RecoveryAttempts = Meter.CreateCounter<long>("payment_recovery_attempt_total");
    private static readonly Counter<long> RecoverySuccesses = Meter.CreateCounter<long>("payment_recovery_success_total");
    private static readonly Counter<long> RecoveryFailures = Meter.CreateCounter<long>("payment_recovery_failure_total");

    private readonly PaymentDbContext _dbContext;
    private readonly IClock _clock;
    private readonly ICurrentUser _currentUser;
    private readonly IRequestContext _requestContext;
    private readonly IAccountServiceClient _accountClient;
    private readonly ILedgerServiceClient _ledgerClient;
    private readonly IValidator<CreatePaymentRequest> _createValidator;
    private readonly IValidator<PaymentSearchRequest> _searchValidator;
    private readonly PaymentIdempotencyOptions _idempotencyOptions;

    public PaymentService(PaymentDbContext dbContext, IClock clock, ICurrentUser currentUser, IRequestContext requestContext, IAccountServiceClient accountClient, ILedgerServiceClient ledgerClient, IValidator<CreatePaymentRequest> createValidator, IValidator<PaymentSearchRequest> searchValidator, IOptions<PaymentIdempotencyOptions>? idempotencyOptions = null)
    {
        _dbContext = dbContext;
        _clock = clock;
        _currentUser = currentUser;
        _requestContext = requestContext;
        _accountClient = accountClient;
        _ledgerClient = ledgerClient;
        _createValidator = createValidator;
        _searchValidator = searchValidator;
        _idempotencyOptions = idempotencyOptions?.Value ?? new PaymentIdempotencyOptions();
    }

    public async Task<PaymentCreationResult> CreateAsync(CreatePaymentRequest request, string idempotencyKey, CancellationToken cancellationToken = default)
    {
        await _createValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var normalizedKey = IdempotencyKeyRules.Normalize(idempotencyKey);
        var customerId = RequireCustomerId();
        var requestHash = PaymentRequestHasher.ComputeHash(request);
        IdempotencyRequests.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));

        var claim = await TryClaimAndCreatePaymentAsync(customerId, normalizedKey, requestHash, request, cancellationToken).ConfigureAwait(false);
        if (!claim.Created)
        {
            return await ResolveExistingIdempotencyAsync(customerId, normalizedKey, requestHash, cancellationToken).ConfigureAwait(false);
        }

        IdempotencyNew.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));
        PaymentsInitiated.Add(1);
        await ResumePaymentAsync(claim.PaymentId, ResolveActorType(false), ActorId(false), cancellationToken).ConfigureAwait(false);
        var payment = await LoadPaymentAsync(claim.PaymentId, true, cancellationToken).ConfigureAwait(false);
        var response = Map(payment);
        await CompleteIdempotencyAsync(claim.IdempotencyRecordId, response, 201, cancellationToken).ConfigureAwait(false);
        return new PaymentCreationResult(response, false, 201);
    }

    public async Task<PaymentDetailResponse> GetAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, false, cancellationToken).ConfigureAwait(false);
        EnsureCanRead(payment);
        return MapDetail(payment);
    }

    public async Task<IReadOnlyCollection<PaymentTimelineEntryResponse>> GetTimelineAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, false, cancellationToken).ConfigureAwait(false);
        EnsureCanRead(payment);
        var transitions = await _dbContext.PaymentStateTransitions.AsNoTracking()
            .Where(transition => transition.PaymentId == paymentId)
            .OrderBy(transition => transition.OccurredAtUtc)
            .ToArrayAsync(cancellationToken)
            .ConfigureAwait(false);

        return transitions.Select(transition => new PaymentTimelineEntryResponse(transition.FromStatus?.ToString(), transition.ToStatus.ToString(), transition.ReasonCode?.ToString(), transition.ReasonDescription, transition.OccurredAtUtc, transition.ActorType.ToString(), transition.ActorId, transition.CorrelationId)).ToArray();
    }

    public async Task<PaymentPageResponse> ListAsync(PaymentSearchRequest request, CancellationToken cancellationToken = default)
    {
        await _searchValidator.ValidateAndThrowAsync(request, cancellationToken).ConfigureAwait(false);
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 100);
        var query = _dbContext.Payments.AsNoTracking().AsQueryable();
        if (!_currentUser.HasPermission("payment.read.any"))
        {
            query = query.Where(payment => payment.CustomerId == RequireCustomerId());
        }

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            var status = Enum.Parse<PaymentStatus>(request.Status, true);
            query = query.Where(payment => payment.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(request.Type))
        {
            var type = Enum.Parse<PaymentType>(request.Type, true);
            query = query.Where(payment => payment.PaymentType == type);
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            var currency = Currency.FromCode(request.Currency);
            query = query.Where(payment => payment.Currency.Equals(currency));
        }

        if (request.FromUtc is not null) query = query.Where(payment => payment.CreatedAtUtc >= request.FromUtc.Value);
        if (request.ToUtc is not null) query = query.Where(payment => payment.CreatedAtUtc <= request.ToUtc.Value);

        var total = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query.OrderByDescending(payment => payment.CreatedAtUtc).Skip((page - 1) * pageSize).Take(pageSize).Select(payment => Map(payment)).ToListAsync(cancellationToken).ConfigureAwait(false);
        return new PaymentPageResponse(items, page, pageSize, total);
    }

    public async Task<PaymentResponse> CancelAsync(Guid paymentId, CancelPaymentRequest request, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, true, cancellationToken).ConfigureAwait(false);
        EnsureCanCancel(payment);
        var now = _clock.UtcNow;
        var actorType = ResolveActorType(_currentUser.HasPermission("payment.cancel.any"));
        var actorId = ActorId(_currentUser.HasPermission("payment.cancel.any"));

        if (payment.Status == PaymentStatus.FundsReserved && payment.FundsReservationId is { } reservationId)
        {
            await _accountClient.ReleaseReservationAsync(payment.SourceAccountId, reservationId, cancellationToken).ConfigureAwait(false);
            AddAudit(payment, "ReservationReleased", now, "Cancellation released held funds");
        }

        payment.Cancel(actorType == PaymentActorType.Customer ? PaymentFailureReasonCode.CancelledByCustomer : PaymentFailureReasonCode.CancelledByOperations, request.Reason ?? "Payment cancelled", now, actorType, actorId, _requestContext.CorrelationId);
        AddLatestTransition(payment);
        AddAudit(payment, "PaymentCancelled", now, request.Reason);
        AddLifecycleOutbox(payment, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        PaymentsCancelled.Add(1, new KeyValuePair<string, object?>("payment_type", payment.PaymentType.ToString()), new KeyValuePair<string, object?>("currency", payment.Currency.Code));
        return Map(payment);
    }

    public async Task<int> RecoverAsync(int batchSize, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(batchSize <= 0 ? 25 : batchSize, 1, 100);
        var cutoff = _clock.UtcNow.Subtract(minAge <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : minAge);
        var payments = await _dbContext.Payments.FromSqlInterpolated($"SELECT * FROM payment.payments WHERE \"Status\" IN ('PendingValidation','FundsReserved','Processing') AND \"UpdatedAtUtc\" <= {cutoff} ORDER BY \"UpdatedAtUtc\" LIMIT {take} FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken).ConfigureAwait(false);
        var processed = 0;
        foreach (var payment in payments)
        {
            RecoveryAttempts.Add(1, new KeyValuePair<string, object?>("status", payment.Status.ToString()));
            try
            {
                await ResumePaymentAsync(payment.Id, PaymentActorType.SystemWorker, "payment-recovery-worker", cancellationToken).ConfigureAwait(false);
                RecoverySuccesses.Add(1);
                processed++;
            }
            catch (Exception)
            {
                RecoveryFailures.Add(1);
            }
        }

        return processed;
    }

    private async Task<IdempotencyClaimResult> TryClaimAndCreatePaymentAsync(Guid customerId, string idempotencyKey, string requestHash, CreatePaymentRequest request, CancellationToken cancellationToken)
    {
        var strategy = _dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);
            try
            {
                var now = _clock.UtcNow;
                var type = Enum.Parse<PaymentType>(request.Type, true);
                var currency = Currency.FromCode(request.Currency);
                var destination = CreateDestination(type, request.Destination);
                var reference = await GenerateReferenceAsync(now, cancellationToken).ConfigureAwait(false);
                var expiresAtUtc = now.AddDays(Math.Clamp(_idempotencyOptions.RetentionDays, 1, 90));
                var idempotency = IdempotencyRecord.Create(customerId, PaymentOperationTypes.CreatePayment, idempotencyKey, requestHash, now, expiresAtUtc);
                var payment = Domain.Payments.Payment.Create(customerId, request.SourceAccountId, type, Money.Of(request.Amount, currency), destination, reference, request.Description, reference, now, _requestContext.CorrelationId, ResolveActorType(false), ActorId(false));

                _dbContext.IdempotencyRecords.Add(idempotency);
                _dbContext.Payments.Add(payment);
                AddLatestTransition(payment);
                AddAudit(payment, "PaymentCreated", now);
                AddAudit(payment, "PaymentInitiationAccepted", now);
                AddLifecycleOutbox(payment, now);
                idempotency.AttachResource("Payment", payment.Id, now);

                await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return new IdempotencyClaimResult(payment.Id, idempotency.Id, true);
            }
            catch (DbUpdateException exception) when (IsUniqueViolation(exception))
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                _dbContext.ChangeTracker.Clear();
                return new IdempotencyClaimResult(Guid.Empty, Guid.Empty, false);
            }
        }).ConfigureAwait(false);
    }

    private async Task<PaymentCreationResult> ResolveExistingIdempotencyAsync(Guid customerId, string idempotencyKey, string requestHash, CancellationToken cancellationToken)
    {
        var record = await _dbContext.IdempotencyRecords.AsNoTracking()
            .SingleAsync(item => item.CustomerId == customerId && item.OperationType == PaymentOperationTypes.CreatePayment && item.IdempotencyKey == idempotencyKey, cancellationToken)
            .ConfigureAwait(false);

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            IdempotencyConflicts.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));
            if (record.ResourceId is { } conflictPaymentId)
            {
                await AddIdempotencyAuditIfPaymentExistsAsync(conflictPaymentId, "IdempotencyConflictDetected", "Same key was reused with a different request hash.", cancellationToken).ConfigureAwait(false);
            }

            throw new IdempotencyKeyConflictException();
        }

        if (record.ResourceId is null)
        {
            IdempotencyProcessing.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));
            throw new ConflictException("The idempotent payment request is still being initialized. Retry shortly with the same key.");
        }

        var payment = await LoadPaymentAsync(record.ResourceId.Value, false, cancellationToken).ConfigureAwait(false);
        var response = Map(payment);
        if (record.IsProcessing)
        {
            if (IsTerminal(payment.Status))
            {
                await CompleteIdempotencyAsync(record.Id, response, 201, cancellationToken).ConfigureAwait(false);
                IdempotencyReplayed.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment), new KeyValuePair<string, object?>("result", "completed_after_recovery"));
                await AddIdempotencyAuditIfPaymentExistsAsync(payment.Id, "DuplicateRequestReplayed", "Completed idempotency record was reconstructed from the existing payment.", cancellationToken).ConfigureAwait(false);
                return new PaymentCreationResult(response, true, 201);
            }

            IdempotencyProcessing.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));
            await AddIdempotencyAuditIfPaymentExistsAsync(payment.Id, "DuplicateRequestReplayed", "Duplicate request observed while original initiation is processing.", cancellationToken).ConfigureAwait(false);
            return new PaymentCreationResult(response, true, 202);
        }

        IdempotencyReplayed.Add(1, new KeyValuePair<string, object?>("operation", PaymentOperationTypes.CreatePayment));
        await AddIdempotencyAuditIfPaymentExistsAsync(payment.Id, "DuplicateRequestReplayed", "Duplicate request returned the existing payment resource.", cancellationToken).ConfigureAwait(false);
        return new PaymentCreationResult(response, true, record.ResponseStatusCode ?? 201);
    }

    private async Task CompleteIdempotencyAsync(Guid idempotencyRecordId, PaymentResponse response, int statusCode, CancellationToken cancellationToken)
    {
        var record = await _dbContext.IdempotencyRecords.SingleAsync(item => item.Id == idempotencyRecordId, cancellationToken).ConfigureAwait(false);
        if (record.IsCompleted) return;
        record.Complete(statusCode, JsonSerializer.Serialize(response, SerializerOptions), _clock.UtcNow);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task AddIdempotencyAuditIfPaymentExistsAsync(Guid paymentId, string eventType, string reason, CancellationToken cancellationToken)
    {
        var payment = await _dbContext.Payments.SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null) return;
        AddAudit(payment, eventType, _clock.UtcNow, reason);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
    }

    private static bool IsTerminal(PaymentStatus status) => status is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Rejected or PaymentStatus.Cancelled;

    private static bool IsUniqueViolation(DbUpdateException exception) => exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private sealed record IdempotencyClaimResult(Guid PaymentId, Guid IdempotencyRecordId, bool Created);
    private async Task ResumePaymentAsync(Guid paymentId, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var keepGoing = true;
        while (keepGoing)
        {
            var payment = await LoadPaymentAsync(paymentId, true, cancellationToken).ConfigureAwait(false);
            keepGoing = payment.Status switch
            {
                PaymentStatus.Initiated => await MoveToPendingValidationAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false),
                PaymentStatus.PendingValidation => await ValidateAndReserveAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false),
                PaymentStatus.FundsReserved => await MoveToProcessingAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false),
                PaymentStatus.Processing => await ProcessAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false),
                _ => false,
            };
        }
    }

    private async Task<bool> MoveToPendingValidationAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        payment.StartValidation(now, actorType, actorId, _requestContext.CorrelationId);
        AddLatestTransition(payment);
        AddAudit(payment, "ValidationStarted", now);
        AddLifecycleOutbox(payment, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }

    private async Task<bool> ValidateAndReserveAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var validation = await ValidatePaymentReferencesAsync(payment, cancellationToken).ConfigureAwait(false);
        if (validation is not null)
        {
            payment.MarkRejected(validation.Value.Code, validation.Value.Description, now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentRejected", now, validation.Value.Description);
            AddLifecycleOutbox(payment, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            PaymentsRejected.Add(1, new KeyValuePair<string, object?>("reason_code", validation.Value.Code.ToString()));
            return false;
        }

        try
        {
            var reservation = await _accountClient.ReserveFundsAsync(payment.SourceAccountId, new CreateFundsReservationCommand(payment.Id.ToString("D"), payment.Amount, payment.Currency.Code, now.AddMinutes(15)), cancellationToken).ConfigureAwait(false);
            payment.MarkFundsReserved(reservation.ReservationId, now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "FundsReserved", now, null, JsonSerializer.Serialize(new { reservation.ReservationId }, SerializerOptions));
            AddLifecycleOutbox(payment, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (DownstreamBusinessException exception)
        {
            payment.MarkRejected(exception.ReasonCode, exception.Message, now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentRejected", now, exception.Message);
            AddLifecycleOutbox(payment, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            PaymentsRejected.Add(1, new KeyValuePair<string, object?>("reason_code", exception.ReasonCode.ToString()));
            return false;
        }
        catch (DownstreamTransientException exception)
        {
            AddAudit(payment, "FundsReservationDeferred", now, exception.Message);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<bool> MoveToProcessingAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        payment.StartProcessing(now, actorType, actorId, _requestContext.CorrelationId);
        AddLatestTransition(payment);
        AddAudit(payment, "ProcessingStarted", now);
        AddLifecycleOutbox(payment, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return payment.PaymentType == PaymentType.InternalTransfer;
    }

    private async Task<bool> ProcessAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (payment.PaymentType == PaymentType.ExternalBankTransfer)
        {
            AddAudit(payment, "ExternalRailDeferred", _clock.UtcNow, "External rail processing is not implemented in Week 5.");
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var now = _clock.UtcNow;
        var source = await _dbContext.AccountReferences.AsNoTracking().SingleAsync(reference => reference.AccountId == payment.SourceAccountId, cancellationToken).ConfigureAwait(false);
        var destination = await _dbContext.AccountReferences.AsNoTracking().SingleAsync(reference => reference.AccountId == payment.Destination.AccountId!.Value, cancellationToken).ConfigureAwait(false);
        if (source.LedgerAccountId is null || destination.LedgerAccountId is null)
        {
            return await FailAfterReservationAsync(payment, PaymentFailureReasonCode.SystemFailure, "Ledger account mapping is not available for one or more payment accounts.", actorType, actorId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            AddAudit(payment, "LedgerPostingRequested", now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            payment = await LoadPaymentAsync(payment.Id, false, cancellationToken).ConfigureAwait(false);
            var ledger = await _ledgerClient.PostTransactionAsync(new PostLedgerTransactionCommand(payment.Id.ToString("D"), "InternalTransfer", payment.Currency.Code, $"Payment {payment.Reference}", [new LedgerPostingCommand(source.LedgerAccountId.Value, "Debit", payment.Amount, $"Debit {payment.Reference}"), new LedgerPostingCommand(destination.LedgerAccountId.Value, "Credit", payment.Amount, $"Credit {payment.Reference}")], now), cancellationToken).ConfigureAwait(false);
            AddAudit(payment, "LedgerPostingConfirmed", _clock.UtcNow, null, JsonSerializer.Serialize(new { ledger.TransactionId }, SerializerOptions));
            if (payment.FundsReservationId is { } reservationId)
            {
                await _accountClient.CommitReservationAsync(payment.SourceAccountId, reservationId, cancellationToken).ConfigureAwait(false);
                AddAudit(payment, "ReservationCommitted", _clock.UtcNow, null, JsonSerializer.Serialize(new { reservationId }, SerializerOptions));
            }

            payment.MarkCompleted(ledger.TransactionId, _clock.UtcNow, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentCompleted", _clock.UtcNow);
            AddLifecycleOutbox(payment, _clock.UtcNow);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            PaymentsCompleted.Add(1, new KeyValuePair<string, object?>("payment_type", payment.PaymentType.ToString()), new KeyValuePair<string, object?>("currency", payment.Currency.Code));
            return false;
        }
        catch (DownstreamBusinessException exception)
        {
            return await FailAfterReservationAsync(payment, exception.ReasonCode, exception.Message, actorType, actorId, cancellationToken).ConfigureAwait(false);
        }
        catch (DownstreamTransientException exception)
        {
            AddAudit(payment, "ProcessingDeferred", _clock.UtcNow, exception.Message);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<bool> FailAfterReservationAsync(Domain.Payments.Payment payment, PaymentFailureReasonCode reasonCode, string description, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        if (payment.FundsReservationId is { } reservationId)
        {
            try
            {
                await _accountClient.ReleaseReservationAsync(payment.SourceAccountId, reservationId, cancellationToken).ConfigureAwait(false);
                AddAudit(payment, "ReservationReleased", now, "Failure released held funds");
            }
            catch (DownstreamTransientException)
            {
                AddAudit(payment, "ReservationReleaseDeferred", now, "Reservation release will be retried by recovery.");
                await SaveAsync(cancellationToken).ConfigureAwait(false);
                return false;
            }
        }

        payment.MarkFailed(reasonCode, description, now, actorType, actorId, _requestContext.CorrelationId);
        AddLatestTransition(payment);
        AddAudit(payment, "PaymentFailed", now, description);
        AddLifecycleOutbox(payment, now);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        PaymentsFailed.Add(1, new KeyValuePair<string, object?>("reason_code", reasonCode.ToString()));
        return false;
    }

    private async Task<(PaymentFailureReasonCode Code, string Description)?> ValidatePaymentReferencesAsync(Domain.Payments.Payment payment, CancellationToken cancellationToken)
    {
        var customer = await _dbContext.CustomerReferences.AsNoTracking().SingleOrDefaultAsync(reference => reference.CustomerId == payment.CustomerId, cancellationToken).ConfigureAwait(false);
        if (customer is null || !string.Equals(customer.Status, "Active", StringComparison.OrdinalIgnoreCase)) return (PaymentFailureReasonCode.CustomerNotActive, "Customer is not active.");
        if (!customer.CanInitiatePayments) return (PaymentFailureReasonCode.CustomerKycInsufficient, "Customer KYC is insufficient for payments.");

        var sourceRef = await _dbContext.AccountReferences.AsNoTracking().SingleOrDefaultAsync(reference => reference.AccountId == payment.SourceAccountId, cancellationToken).ConfigureAwait(false);
        if (sourceRef is null) return (PaymentFailureReasonCode.SourceAccountNotFound, "Source account reference is not available.");
        if (sourceRef.CustomerId != payment.CustomerId) return (PaymentFailureReasonCode.SourceAccountNotFound, "Source account does not belong to customer.");
        if (!sourceRef.IsActive) return (PaymentFailureReasonCode.SourceAccountFrozen, "Source account is not active.");
        if (!sourceRef.Currency.Equals(payment.Currency)) return (PaymentFailureReasonCode.CurrencyMismatch, "Source account currency does not match payment currency.");

        try
        {
            var source = await _accountClient.GetAccountAsync(payment.SourceAccountId, cancellationToken).ConfigureAwait(false);
            if (source.CustomerId != payment.CustomerId) return (PaymentFailureReasonCode.SourceAccountNotFound, "Source account does not belong to customer.");
            if (!string.Equals(source.Status, "Active", StringComparison.OrdinalIgnoreCase)) return (PaymentFailureReasonCode.SourceAccountFrozen, "Source account is not active.");
            if (!string.Equals(source.Currency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase)) return (PaymentFailureReasonCode.CurrencyMismatch, "Source account currency does not match payment currency.");
        }
        catch (DownstreamBusinessException exception)
        {
            return (exception.ReasonCode, exception.Message);
        }
        catch (DownstreamTransientException)
        {
            return null;
        }

        if (payment.PaymentType == PaymentType.InternalTransfer)
        {
            var destinationAccountId = payment.Destination.AccountId!.Value;
            var destinationRef = await _dbContext.AccountReferences.AsNoTracking().SingleOrDefaultAsync(reference => reference.AccountId == destinationAccountId, cancellationToken).ConfigureAwait(false);
            if (destinationRef is null) return (PaymentFailureReasonCode.DestinationAccountNotFound, "Destination account reference is not available.");
            if (!destinationRef.IsActive) return (PaymentFailureReasonCode.DestinationAccountNotFound, "Destination account is not active.");
            if (!destinationRef.Currency.Equals(payment.Currency)) return (PaymentFailureReasonCode.CurrencyMismatch, "Destination account currency does not match payment currency.");
            try
            {
                var destination = await _accountClient.GetAccountAsync(destinationAccountId, cancellationToken).ConfigureAwait(false);
                if (!string.Equals(destination.Status, "Active", StringComparison.OrdinalIgnoreCase)) return (PaymentFailureReasonCode.DestinationAccountNotFound, "Destination account is not active.");
                if (!string.Equals(destination.Currency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase)) return (PaymentFailureReasonCode.CurrencyMismatch, "Destination account currency does not match payment currency.");
            }
            catch (DownstreamBusinessException exception)
            {
                return (exception.ReasonCode, exception.Message);
            }
            catch (DownstreamTransientException)
            {
                return null;
            }
        }

        return null;
    }

    private static PaymentDestination CreateDestination(PaymentType type, PaymentDestinationRequest request)
        => type == PaymentType.InternalTransfer
            ? PaymentDestination.InternalAccount(request.AccountId ?? Guid.Empty)
            : PaymentDestination.ExternalBank(request.BankCode ?? string.Empty, request.AccountNumber ?? string.Empty, request.AccountName ?? string.Empty, request.CountryCode ?? string.Empty);

    private async Task<string> GenerateReferenceAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var reference = $"PAY-{now:yyyyMMdd}-{Guid.NewGuid():N}"[..22].ToUpperInvariant();
            if (!await _dbContext.Payments.AnyAsync(payment => payment.Reference == reference, cancellationToken).ConfigureAwait(false))
            {
                return reference;
            }
        }

        throw new ConflictException("Could not generate a unique payment reference.");
    }

    private async Task<Domain.Payments.Payment> LoadPaymentAsync(Guid paymentId, bool includeTimeline, CancellationToken cancellationToken)
    {
        _ = includeTimeline;
        IQueryable<Domain.Payments.Payment> query = _dbContext.Payments;
        return await query.SingleOrDefaultAsync(payment => payment.Id == paymentId, cancellationToken).ConfigureAwait(false)
            ?? throw new NotFoundException("Payment", paymentId.ToString("D"));
    }

    private void EnsureCanRead(Domain.Payments.Payment payment)
    {
        if (_currentUser.HasPermission("payment.read.any")) return;
        if (!_currentUser.HasPermission("payment.read.self")) throw new ForbiddenApplicationException();
        if (payment.CustomerId != RequireCustomerId()) throw new ForbiddenApplicationException();
    }

    private void EnsureCanCancel(Domain.Payments.Payment payment)
    {
        var canCancelAny = _currentUser.HasPermission("payment.cancel.any");
        var canCancelSelf = _currentUser.HasPermission("payment.cancel.self") && payment.CustomerId == RequireCustomerId();
        if (!canCancelAny && !canCancelSelf) throw new ForbiddenApplicationException();
        if (payment.Status is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Rejected or PaymentStatus.Cancelled) throw new ConflictException("Payment cannot be cancelled in its current state.");
        if (payment.Status == PaymentStatus.Processing) throw new ConflictException("Payment is already processing and cannot be cancelled without reversal semantics.");
    }

    private void AddLatestTransition(Domain.Payments.Payment payment)
    {
        var transition = payment.StateTransitions.OrderBy(item => item.OccurredAtUtc).Last();
        _dbContext.PaymentStateTransitions.Add(transition);
    }

    private void AddAudit(Domain.Payments.Payment payment, string eventType, DateTimeOffset now, string? reason = null, string metadata = "{}")
        => _dbContext.PaymentAuditEvents.Add(PaymentAuditEvent.Create(payment.Id, eventType, ResolveActorType(false), ActorId(false), now, _requestContext.CorrelationId, reason, metadata));

    private void AddLifecycleOutbox(Domain.Payments.Payment payment, DateTimeOffset now)
    {
        var payload = new PaymentLifecycleIntegrationEvent(payment.Id, payment.CustomerId, payment.Reference, payment.PaymentType.ToString(), payment.Status.ToString(), payment.Currency.Code, payment.ReasonCode?.ToString());
        var envelope = new IntegrationEventEnvelope<PaymentLifecycleIntegrationEvent>(Guid.NewGuid(), PaymentLifecycleIntegrationEvent.EventType, PaymentLifecycleIntegrationEvent.EventVersion, now, _requestContext.CorrelationId, _requestContext.CausationId, "payment-service", payload);
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(PaymentsTopic, payment.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now));
    }

    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            _dbContext.ChangeTracker.Clear();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            var entries = string.Join(", ", exception.Entries.Select(entry => $"{entry.Entity.GetType().FullName}:{entry.State}"));
            throw new ConflictException($"Payment concurrency conflict while saving: {entries}");
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ConflictException("A payment with the same reference already exists.");
        }
    }

    private Guid RequireCustomerId() => Guid.TryParse(_currentUser.CustomerId, out var customerId) ? customerId : throw new UnauthorizedApplicationException();
    private PaymentActorType ResolveActorType(bool privileged) => privileged ? PaymentActorType.OperationsUser : _currentUser.IsAuthenticated ? PaymentActorType.Customer : PaymentActorType.Service;
    private string ActorId(bool privileged) => _currentUser.UserId ?? (privileged ? "operations-user" : "payment-service");

    private static PaymentResponse Map(Domain.Payments.Payment payment) => new(payment.Id, payment.Reference, payment.Status.ToString(), payment.PaymentType.ToString(), payment.Amount, payment.Currency.Code, payment.CreatedAtUtc, payment.UpdatedAtUtc, payment.FundsReservationId, payment.LedgerTransactionId, payment.ReasonCode?.ToString(), payment.ReasonDescription);
    private static PaymentDetailResponse MapDetail(Domain.Payments.Payment payment) => new(payment.Id, payment.CustomerId, payment.SourceAccountId, payment.Reference, payment.Status.ToString(), payment.PaymentType.ToString(), payment.Amount, payment.Currency.Code, MapDestination(payment.Destination), payment.Description, payment.CreatedAtUtc, payment.UpdatedAtUtc, payment.CompletedAtUtc, payment.FailedAtUtc, payment.FundsReservationId, payment.LedgerTransactionId, payment.ReasonCode?.ToString(), payment.ReasonDescription);
    private static PaymentDestinationResponse MapDestination(PaymentDestination destination) => new(destination.DestinationType.ToString(), destination.AccountId, destination.BankCode, Mask(destination.AccountNumber), destination.AccountName, destination.CountryCode);
    private static string? Mask(string? accountNumber) => string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Length <= 4 ? "****" : $"******{accountNumber[^4..]}";
}
