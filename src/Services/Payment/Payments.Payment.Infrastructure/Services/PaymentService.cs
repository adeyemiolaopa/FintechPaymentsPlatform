using System.Data;
using System.Diagnostics.Metrics;
using System.Security.Cryptography;
using System.Text;
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

public sealed class PaymentService : IPaymentService, IPaymentRailCallbackService
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
    private readonly IRailRouter? _railRouter;
    private readonly ExternalTransferOptions _externalTransferOptions;
    private readonly IRailCallbackAuthenticator? _callbackAuthenticator;

    public PaymentService(PaymentDbContext dbContext, IClock clock, ICurrentUser currentUser, IRequestContext requestContext, IAccountServiceClient accountClient, ILedgerServiceClient ledgerClient, IValidator<CreatePaymentRequest> createValidator, IValidator<PaymentSearchRequest> searchValidator, IOptions<PaymentIdempotencyOptions>? idempotencyOptions = null, IRailRouter? railRouter = null, IOptions<ExternalTransferOptions>? externalTransferOptions = null, IRailCallbackAuthenticator? callbackAuthenticator = null)
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
        _railRouter = railRouter;
        _externalTransferOptions = externalTransferOptions?.Value ?? new ExternalTransferOptions();
        _callbackAuthenticator = callbackAuthenticator;
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

    public async Task<PaymentResponse> ReverseAsync(Guid paymentId, ReversePaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!_currentUser.HasPermission("payment.reverse")) throw new ForbiddenApplicationException();
        if (string.IsNullOrWhiteSpace(request.Reason)) throw new ConflictException("Reversal reason is required.");

        var payment = await LoadPaymentAsync(paymentId, true, cancellationToken).ConfigureAwait(false);
        if (payment.Status == PaymentStatus.Reversed) return Map(payment);
        if (payment.PaymentType != PaymentType.InternalTransfer) throw new ConflictException("Only completed internal transfers can be reversed in Week 9.");
        if (payment.Status is not PaymentStatus.Completed and not PaymentStatus.ReversalPending) throw new ConflictException("Only completed payments can be reversed.");
        if (payment.LedgerTransactionId is null) throw new ConflictException("Payment cannot be reversed without an original ledger transaction.");

        var actorType = ResolveActorType(true);
        var actorId = ActorId(true);
        if (payment.Status == PaymentStatus.Completed)
        {
            var destinationAccountId = payment.Destination.AccountId ?? throw new ConflictException("Internal transfer destination is missing.");
            var destinationBalance = await _accountClient.GetBalanceAsync(destinationAccountId, cancellationToken).ConfigureAwait(false);
            if (destinationBalance.AvailableBalance < payment.Amount)
            {
                throw new ConflictException("Destination account does not have sufficient available funds for conservative reversal.");
            }

            var now = _clock.UtcNow;
            payment.StartReversal(request.Reason, now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentReversalStarted", now, request.Reason);
            AddLifecycleOutbox(payment, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
        }

        await ResumeReversalAsync(payment.Id, actorType, actorId, cancellationToken).ConfigureAwait(false);
        return Map(await LoadPaymentAsync(payment.Id, false, cancellationToken).ConfigureAwait(false));
    }

    public async Task<PaymentConsistencyResponse> VerifyConsistencyAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, false, cancellationToken).ConfigureAwait(false);
        EnsureCanRead(payment);
        return await BuildConsistencyResponseAsync(payment, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyCollection<PaymentConsistencyResponse>> VerifyRecentConsistencyAsync(int batchSize, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(batchSize <= 0 ? 25 : batchSize, 1, 100);
        var query = _dbContext.Payments.AsNoTracking().Where(payment => payment.PaymentType == PaymentType.InternalTransfer).OrderByDescending(payment => payment.UpdatedAtUtc).Take(take);
        if (!_currentUser.HasPermission("payment.audit.read") && !_currentUser.HasPermission("payment.read.any"))
        {
            var customerId = RequireCustomerId();
            query = query.Where(payment => payment.CustomerId == customerId);
        }

        var payments = await query.ToListAsync(cancellationToken).ConfigureAwait(false);
        var responses = new List<PaymentConsistencyResponse>(payments.Count);
        foreach (var payment in payments)
        {
            responses.Add(await BuildConsistencyResponseAsync(payment, cancellationToken).ConfigureAwait(false));
        }

        return responses;
    }
    public async Task<int> RecoverAsync(int batchSize, TimeSpan minAge, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(batchSize <= 0 ? 25 : batchSize, 1, 100);
        var cutoff = _clock.UtcNow.Subtract(minAge <= TimeSpan.Zero ? TimeSpan.FromSeconds(30) : minAge);
        var payments = await _dbContext.Payments.FromSqlInterpolated($"SELECT * FROM payment.payments WHERE \"Status\" IN ('PendingValidation','FundsReserved','Processing','SubmittedToRail','PendingReconciliation','ReversalPending') AND \"UpdatedAtUtc\" <= {cutoff} ORDER BY \"UpdatedAtUtc\" LIMIT {take} FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken).ConfigureAwait(false);
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

    public async Task RecoverOneAsync(Guid paymentId, CancellationToken cancellationToken = default)
    {
        var payment = await LoadPaymentAsync(paymentId, true, cancellationToken).ConfigureAwait(false);
        if (payment.PaymentType != PaymentType.ExternalBankTransfer || payment.Status is not (PaymentStatus.Processing or PaymentStatus.SubmittedToRail or PaymentStatus.PendingReconciliation))
            return;
        await ResumePaymentAsync(paymentId, PaymentActorType.SystemWorker, "reconciliation-service", cancellationToken).ConfigureAwait(false);
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

    private static bool IsTerminal(PaymentStatus status) => status is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Rejected or PaymentStatus.Cancelled or PaymentStatus.Reversed;

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
                PaymentStatus.SubmittedToRail or PaymentStatus.PendingReconciliation => await RecoverExternalRailAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false),
                PaymentStatus.ReversalPending => await ResumeReversalAsync(payment.Id, actorType, actorId, cancellationToken).ConfigureAwait(false),
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

    private async Task<bool> ResumeReversalAsync(Guid paymentId, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        var payment = await LoadPaymentAsync(paymentId, true, cancellationToken).ConfigureAwait(false);
        if (payment.Status == PaymentStatus.Reversed) return false;
        if (payment.Status != PaymentStatus.ReversalPending) return false;
        if (payment.LedgerTransactionId is null) throw new ConflictException("Payment cannot be reversed without an original ledger transaction.");

        try
        {
            var reason = payment.ReversalReason ?? "Internal transfer reversal";
            var ledger = await _ledgerClient.ReverseTransactionAsync(payment.LedgerTransactionId.Value, new ReverseLedgerTransactionCommand($"REV-{payment.Id:D}", reason), cancellationToken).ConfigureAwait(false);
            var now = _clock.UtcNow;
            payment.MarkReversed(ledger.TransactionId, now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentReversed", now, reason, JsonSerializer.Serialize(new { ledger.TransactionId }, SerializerOptions));
            AddLifecycleOutbox(payment, now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
        catch (DownstreamTransientException exception)
        {
            AddAudit(payment, "PaymentReversalDeferred", _clock.UtcNow, exception.Message);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    private async Task<PaymentConsistencyResponse> BuildConsistencyResponseAsync(Domain.Payments.Payment payment, CancellationToken cancellationToken)
    {
        var violations = new List<PaymentConsistencyViolationResponse>();
        LedgerTransactionClientResponse? ledger = null;
        ReservationClientResponse? reservation = null;

        if (payment.Status is PaymentStatus.Completed or PaymentStatus.Reversed)
        {
            if (payment.LedgerTransactionId is null)
            {
                violations.Add(new PaymentConsistencyViolationResponse("payment.completed.ledger_missing", "Critical", "Completed internal payment has no ledger transaction id."));
            }
            else
            {
                try
                {
                    ledger = await _ledgerClient.GetTransactionAsync(payment.LedgerTransactionId.Value, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception exception) when (exception is DownstreamBusinessException or DownstreamTransientException)
                {
                    violations.Add(new PaymentConsistencyViolationResponse("payment.ledger.lookup_failed", "Critical", exception.Message));
                }
            }

            if (payment.FundsReservationId is null)
            {
                violations.Add(new PaymentConsistencyViolationResponse("payment.completed.reservation_missing", "Critical", "Completed internal payment has no source reservation id."));
            }
            else
            {
                try
                {
                    reservation = await _accountClient.GetReservationAsync(payment.SourceAccountId, payment.FundsReservationId.Value, cancellationToken).ConfigureAwait(false);
                    if (!string.Equals(reservation.Status, "Committed", StringComparison.OrdinalIgnoreCase))
                    {
                        violations.Add(new PaymentConsistencyViolationResponse("payment.reservation.not_committed", "Critical", $"Reservation is {reservation.Status}, expected Committed."));
                    }
                }
                catch (Exception exception) when (exception is DownstreamBusinessException or DownstreamTransientException)
                {
                    violations.Add(new PaymentConsistencyViolationResponse("payment.reservation.lookup_failed", "Critical", exception.Message));
                }
            }
        }

        if ((payment.Status is PaymentStatus.Rejected or PaymentStatus.Failed or PaymentStatus.Cancelled) && payment.LedgerTransactionId is not null)
        {
            violations.Add(new PaymentConsistencyViolationResponse("payment.terminal.ledger_present", "Critical", "Non-completed terminal payment has a ledger transaction id."));
        }

        if (ledger is not null)
        {
            if (!string.Equals(ledger.ExternalReference, payment.Id.ToString("D"), StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new PaymentConsistencyViolationResponse("payment.ledger.reference_mismatch", "Critical", "Ledger external reference does not match PaymentId."));
            }

            if (!string.IsNullOrWhiteSpace(ledger.Currency) && !string.Equals(ledger.Currency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(new PaymentConsistencyViolationResponse("payment.ledger.currency_mismatch", "Critical", "Ledger currency does not match payment currency."));
            }

            if (ledger.Postings is { Count: > 0 } postings)
            {
                var debit = postings.Where(posting => string.Equals(posting.Side, "Debit", StringComparison.OrdinalIgnoreCase)).Sum(posting => posting.Amount);
                var credit = postings.Where(posting => string.Equals(posting.Side, "Credit", StringComparison.OrdinalIgnoreCase)).Sum(posting => posting.Amount);
                if (debit != credit) violations.Add(new PaymentConsistencyViolationResponse("payment.ledger.unbalanced", "Critical", "Ledger debit total does not equal credit total."));
                if (debit != payment.Amount) violations.Add(new PaymentConsistencyViolationResponse("payment.ledger.amount_mismatch", "Critical", "Ledger transfer amount does not match payment amount."));
            }
        }

        if (reservation is not null)
        {
            if (reservation.Amount != payment.Amount) violations.Add(new PaymentConsistencyViolationResponse("payment.reservation.amount_mismatch", "Critical", "Reservation amount does not match payment amount."));
            if (!string.Equals(reservation.Currency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase)) violations.Add(new PaymentConsistencyViolationResponse("payment.reservation.currency_mismatch", "Critical", "Reservation currency does not match payment currency."));
            if (!string.Equals(reservation.ReferenceId, payment.Id.ToString("D"), StringComparison.OrdinalIgnoreCase)) violations.Add(new PaymentConsistencyViolationResponse("payment.reservation.reference_mismatch", "Critical", "Reservation reference does not match PaymentId."));
        }

        if (payment.Status == PaymentStatus.Reversed && payment.ReversalLedgerTransactionId is null)
        {
            violations.Add(new PaymentConsistencyViolationResponse("payment.reversal.ledger_missing", "Critical", "Reversed payment has no reversal ledger transaction id."));
        }

        return new PaymentConsistencyResponse(payment.Id, payment.Status.ToString(), violations.Count == 0, violations, _clock.UtcNow);
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
        return true;
    }

    private async Task<bool> ProcessAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (payment.PaymentType == PaymentType.ExternalBankTransfer)
        {
            return await ProcessExternalRailAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false);
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

    private async Task<bool> ProcessExternalRailAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (_railRouter is null)
        {
            AddAudit(payment, "ExternalRailDeferred", _clock.UtcNow, "Rail router is not configured.");
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        var instruction = CreateRailInstruction(payment);
        var route = await _railRouter.RouteAsync(instruction, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var instructionHash = ComputeRailInstructionHash(instruction, route.ProviderName);
        var submission = await _dbContext.RailSubmissions.SingleOrDefaultAsync(item => item.PaymentId == payment.Id, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            submission = RailSubmission.Create(payment.Id, route.ProviderName, route.Market, payment.Currency.Code, instruction.ClientReference, instructionHash, now);
            _dbContext.RailSubmissions.Add(submission);
        }
        else if (!string.Equals(submission.InstructionHash, instructionHash, StringComparison.Ordinal))
        {
            payment.MarkPendingReconciliation(PaymentFailureReasonCode.ProviderResponseAmbiguous, "Rail instruction hash mismatch requires manual investigation.", now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "RailInstructionHashMismatch", now, "Existing rail submission hash differs from current payment instruction.");
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        if (payment.Status == PaymentStatus.Processing)
        {
            payment.MarkSubmittedToRail(now, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddLifecycleOutbox(payment, now);
        }

        AddAudit(payment, "RailSubmissionPrepared", now, null, JsonSerializer.Serialize(new { route.ProviderName, route.Market, instruction.ClientReference }, SerializerOptions));
        await SaveAsync(cancellationToken).ConfigureAwait(false);

        payment = await LoadPaymentAsync(payment.Id, true, cancellationToken).ConfigureAwait(false);
        submission = await _dbContext.RailSubmissions.SingleAsync(item => item.PaymentId == payment.Id, cancellationToken).ConfigureAwait(false);
        var attemptNumber = submission.StartAttempt(_clock.UtcNow);
        var attempt = RailSubmissionAttempt.Create(submission.Id, payment.Id, submission.Provider, attemptNumber, submission.ClientReference, _clock.UtcNow);
        _dbContext.RailSubmissionAttempts.Add(attempt);
        AddAudit(payment, "RailSubmissionAttemptStarted", _clock.UtcNow, null, JsonSerializer.Serialize(new { attemptNumber, submission.Provider, submission.ClientReference }, SerializerOptions));
        await SaveAsync(cancellationToken).ConfigureAwait(false);

        var result = await route.Adapter.SubmitTransferAsync(instruction, cancellationToken).ConfigureAwait(false);
        payment = await LoadPaymentAsync(payment.Id, true, cancellationToken).ConfigureAwait(false);
        submission = await _dbContext.RailSubmissions.SingleAsync(item => item.PaymentId == payment.Id, cancellationToken).ConfigureAwait(false);
        attempt = await _dbContext.RailSubmissionAttempts.SingleAsync(item => item.RailSubmissionId == submission.Id && item.AttemptNumber == attemptNumber, cancellationToken).ConfigureAwait(false);
        var respondedAt = _clock.UtcNow;
        submission.RecordOutcome(result.Outcome, result.ProviderReference, result.ProviderResponseCode, result.ProviderMessage, result.RawStatus, respondedAt, result.RetryAfter);
        attempt.Complete(result.Outcome, result.ProviderReference, result.ProviderResponseCode, result.ProviderMessage, result.RawStatus, result.Error, respondedAt);
        AddAudit(payment, "RailSubmissionResultClassified", respondedAt, result.ProviderMessage, JsonSerializer.Serialize(new { result.Outcome, result.ProviderReference, result.ProviderResponseCode, result.RawStatus }, SerializerOptions));

        return result.Outcome switch
        {
            RailSubmissionOutcome.Succeeded => await CompleteExternalProviderSuccessAsync(payment, submission, actorType, actorId, cancellationToken).ConfigureAwait(false),
            RailSubmissionOutcome.Failed => await FailExternalProviderFailureAsync(payment, result.ProviderMessage ?? "External provider returned a definitive failure.", actorType, actorId, cancellationToken).ConfigureAwait(false),
            RailSubmissionOutcome.Pending => await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ProviderResponseAmbiguous, result.ProviderMessage ?? "External provider outcome is pending.", actorType, actorId, cancellationToken).ConfigureAwait(false),
            RailSubmissionOutcome.Ambiguous => await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ProviderResponseAmbiguous, result.ProviderMessage ?? "External provider outcome is ambiguous.", actorType, actorId, cancellationToken).ConfigureAwait(false),
            _ => false,
        };
    }

    private async Task<bool> RecoverExternalRailAsync(Domain.Payments.Payment payment, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (_railRouter is null) return false;
        var submission = await _dbContext.RailSubmissions.SingleOrDefaultAsync(item => item.PaymentId == payment.Id, cancellationToken).ConfigureAwait(false);
        if (submission is null) return await ProcessExternalRailAsync(payment, actorType, actorId, cancellationToken).ConfigureAwait(false);
        if (submission.NextStatusCheckAtUtc is { } next && next > _clock.UtcNow) return false;

        var instruction = CreateRailInstruction(payment);
        var route = await _railRouter.RouteAsync(instruction, cancellationToken).ConfigureAwait(false);
        var status = await route.Adapter.GetTransferStatusAsync(submission.ClientReference, submission.ProviderReference, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        if ((status.Outcome is RailSubmissionOutcome.Succeeded or RailSubmissionOutcome.Failed) &&
            (status.ProviderAmount != payment.Amount || !string.Equals(status.ProviderCurrency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase) || !string.Equals(status.ProviderClientReference, submission.ClientReference, StringComparison.Ordinal)))
        {
            AddAudit(payment, "RailStatusEvidenceMismatch", _clock.UtcNow, "Provider status financial evidence conflicts with payment.");
            return await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ProviderStatusConflict, "Provider status evidence requires manual investigation.", actorType, actorId, cancellationToken).ConfigureAwait(false);
        }
        submission.RecordStatusCheck(now, status.RetryAfter ?? TimeSpan.FromSeconds(_externalTransferOptions.PendingStatusBackoffSeconds));
        submission.RecordOutcome(status.Outcome, status.ProviderReference, status.ProviderResponseCode, status.ProviderMessage, status.RawStatus, now, status.RetryAfter);
        AddAudit(payment, "RailStatusChecked", now, status.ProviderMessage, JsonSerializer.Serialize(new { status.Outcome, status.ProviderReference, status.ProviderResponseCode, status.RawStatus }, SerializerOptions));

        return status.Outcome switch
        {
            RailSubmissionOutcome.Succeeded => await CompleteExternalProviderSuccessAsync(payment, submission, actorType, actorId, cancellationToken).ConfigureAwait(false),
            RailSubmissionOutcome.Failed => await FailExternalProviderFailureAsync(payment, status.ProviderMessage ?? "External provider returned a definitive failure.", actorType, actorId, cancellationToken).ConfigureAwait(false),
            _ => await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ProviderResponseAmbiguous, status.ProviderMessage ?? "External provider outcome is still unresolved.", actorType, actorId, cancellationToken).ConfigureAwait(false),
        };
    }

    private async Task<bool> CompleteExternalProviderSuccessAsync(Domain.Payments.Payment payment, RailSubmission submission, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Completed) return false;
        var source = await _dbContext.AccountReferences.AsNoTracking().SingleAsync(reference => reference.AccountId == payment.SourceAccountId, cancellationToken).ConfigureAwait(false);
        if (source.LedgerAccountId is null || _externalTransferOptions.DefaultClearingLedgerAccountId == Guid.Empty)
        {
            return await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ExternalLedgerClearingMissing, "External transfer clearing ledger account is not configured.", actorType, actorId, cancellationToken).ConfigureAwait(false);
        }

        try
        {
            var now = _clock.UtcNow;
            var ledger = await _ledgerClient.PostTransactionAsync(new PostLedgerTransactionCommand(payment.Id.ToString("D"), "ExternalBankTransfer", payment.Currency.Code, $"External transfer {payment.Reference}", [new LedgerPostingCommand(source.LedgerAccountId.Value, "Debit", payment.Amount, $"Debit external transfer {payment.Reference}"), new LedgerPostingCommand(_externalTransferOptions.DefaultClearingLedgerAccountId, "Credit", payment.Amount, $"Credit external clearing {submission.Provider}")], now), cancellationToken).ConfigureAwait(false);
            AddAudit(payment, "ExternalLedgerPostingConfirmed", _clock.UtcNow, null, JsonSerializer.Serialize(new { ledger.TransactionId, submission.Provider, submission.ProviderReference }, SerializerOptions));
            if (payment.FundsReservationId is { } reservationId)
            {
                await _accountClient.CommitReservationAsync(payment.SourceAccountId, reservationId, cancellationToken).ConfigureAwait(false);
                AddAudit(payment, "ReservationCommitted", _clock.UtcNow, null, JsonSerializer.Serialize(new { reservationId }, SerializerOptions));
            }

            payment.MarkCompleted(ledger.TransactionId, _clock.UtcNow, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddAudit(payment, "PaymentCompleted", _clock.UtcNow, "External provider success finalized.");
            AddLifecycleOutbox(payment, _clock.UtcNow);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            PaymentsCompleted.Add(1, new KeyValuePair<string, object?>("payment_type", payment.PaymentType.ToString()), new KeyValuePair<string, object?>("currency", payment.Currency.Code));
            return false;
        }
        catch (DownstreamTransientException exception)
        {
            return await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.LedgerServiceUnavailable, exception.Message, actorType, actorId, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<bool> FailExternalProviderFailureAsync(Domain.Payments.Payment payment, string description, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (payment.Status == PaymentStatus.Completed)
        {
            AddAudit(payment, "RailFailureAfterCompletionIgnored", _clock.UtcNow, description);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }

        return await FailAfterReservationAsync(payment, PaymentFailureReasonCode.ProviderDeclined, description, actorType, actorId, cancellationToken).ConfigureAwait(false);
    }

    private async Task<bool> MarkExternalPendingAsync(Domain.Payments.Payment payment, PaymentFailureReasonCode reasonCode, string description, PaymentActorType actorType, string actorId, CancellationToken cancellationToken)
    {
        if (payment.Status is PaymentStatus.Processing or PaymentStatus.SubmittedToRail)
        {
            payment.MarkPendingReconciliation(reasonCode, description, _clock.UtcNow, actorType, actorId, _requestContext.CorrelationId);
            AddLatestTransition(payment);
            AddLifecycleOutbox(payment, _clock.UtcNow);
        }

        AddAudit(payment, "ExternalTransferPendingReconciliation", _clock.UtcNow, description);
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return false;
    }

    private RailTransferInstruction CreateRailInstruction(Domain.Payments.Payment payment)
        => new(payment.Id, payment.Id.ToString("D"), payment.SourceAccountId.ToString("D"), payment.Destination.BankCode ?? string.Empty, payment.Destination.AccountNumber ?? string.Empty, payment.Destination.AccountName ?? string.Empty, payment.Destination.CountryCode ?? string.Empty, payment.Amount, payment.Currency.Code, payment.Description);

    private static string ComputeRailInstructionHash(RailTransferInstruction instruction, string provider)
    {
        var canonical = JsonSerializer.Serialize(new { provider, instruction.ClientReference, instruction.DestinationBankCode, instruction.DestinationAccountNumber, instruction.DestinationAccountName, instruction.DestinationCountryCode, instruction.Amount, instruction.Currency }, SerializerOptions);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
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

    public async Task<RailCallbackProcessResult> ProcessCallbackAsync(string provider, string rawBody, IReadOnlyDictionary<string, string?> headers, CancellationToken cancellationToken = default)
    {
        if (_callbackAuthenticator is null)
        {
            return new RailCallbackProcessResult(false, false, "Rejected", "Callback authenticator is not configured.");
        }

        headers.TryGetValue("X-Rail-Timestamp", out var timestamp);
        headers.TryGetValue("X-Rail-Signature", out var signature);
        if (!_callbackAuthenticator.Validate(new RailCallbackAuthenticationInput(provider, timestamp, signature, rawBody)))
        {
            return new RailCallbackProcessResult(false, false, "Rejected", "Invalid callback signature.");
        }

        RailCallbackPayload payload;
        try
        {
            payload = JsonSerializer.Deserialize<RailCallbackPayload>(rawBody, SerializerOptions) ?? throw new JsonException("Callback body was empty.");
        }
        catch (JsonException exception)
        {
            return new RailCallbackProcessResult(false, false, "Rejected", exception.Message);
        }

        var now = _clock.UtcNow;
        var existing = await _dbContext.RailCallbackInboxes.AsNoTracking().SingleOrDefaultAsync(item => item.Provider == provider && item.CallbackEventId == payload.EventId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            return new RailCallbackProcessResult(true, true, existing.Status.ToString(), "Duplicate callback ignored.");
        }

        var inbox = RailCallbackInbox.Receive(provider, payload.EventId, rawBody, now);
        _dbContext.RailCallbackInboxes.Add(inbox);

        if (!Guid.TryParse(payload.ClientReference, out var paymentId))
        {
            inbox.AttachReferences(null, payload.ProviderReference, payload.ClientReference);
            inbox.MarkUnmatched("Callback client reference is not a payment id.", now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        var payment = await _dbContext.Payments.SingleOrDefaultAsync(item => item.Id == paymentId, cancellationToken).ConfigureAwait(false);
        if (payment is null)
        {
            inbox.AttachReferences(null, payload.ProviderReference, payload.ClientReference);
            inbox.MarkUnmatched("Callback references an unknown payment.", now);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        inbox.AttachReferences(payment.Id, payload.ProviderReference, payload.ClientReference);
        var submission = await _dbContext.RailSubmissions.SingleOrDefaultAsync(item => item.PaymentId == payment.Id, cancellationToken).ConfigureAwait(false);
        if (submission is null)
        {
            inbox.MarkUnmatched("Callback arrived before a rail submission record was available.", now);
            AddAudit(payment, "RailCallbackUnmatched", now, inbox.FailureReason);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        if (!string.IsNullOrWhiteSpace(submission.ProviderReference) && !string.Equals(submission.ProviderReference, payload.ProviderReference, StringComparison.OrdinalIgnoreCase))
        {
            inbox.MarkConflict("Callback provider reference does not match stored submission.", now);
            AddAudit(payment, "RailCallbackConflict", now, inbox.FailureReason);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        if (payload.Amount != payment.Amount || !string.Equals(payload.Currency, payment.Currency.Code, StringComparison.OrdinalIgnoreCase))
        {
            inbox.MarkConflict("Callback amount or currency does not match payment.", now);
            AddAudit(payment, "RailCallbackIntegrityViolation", now, inbox.FailureReason);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        var outcome = payload.Status switch
        {
            "Successful" => RailSubmissionOutcome.Succeeded,
            "Failed" => RailSubmissionOutcome.Failed,
            "Processing" or "Received" => RailSubmissionOutcome.Pending,
            _ => RailSubmissionOutcome.Ambiguous,
        };
        submission.RecordOutcome(outcome, payload.ProviderReference, payload.ResponseCode, payload.Status, payload.Status, now, TimeSpan.FromSeconds(_externalTransferOptions.PendingStatusBackoffSeconds));

        if (payment.Status == PaymentStatus.Completed && outcome != RailSubmissionOutcome.Succeeded)
        {
            inbox.MarkConflict("Callback conflicts with completed payment state.", now);
            AddAudit(payment, "RailCallbackConflictAfterCompletion", now, inbox.FailureReason);
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), inbox.FailureReason);
        }

        if (payment.Status is PaymentStatus.Completed or PaymentStatus.Failed or PaymentStatus.Rejected or PaymentStatus.Cancelled or PaymentStatus.Reversed)
        {
            inbox.MarkProcessed(now);
            AddAudit(payment, "RailCallbackStaleIgnored", now, $"Terminal payment ignored callback status {payload.Status}.");
            await SaveAsync(cancellationToken).ConfigureAwait(false);
            return new RailCallbackProcessResult(true, false, inbox.Status.ToString(), "Terminal payment state was not changed.");
        }

        inbox.MarkProcessed(now);
        var result = outcome switch
        {
            RailSubmissionOutcome.Succeeded => await CompleteExternalProviderSuccessAsync(payment, submission, PaymentActorType.SystemWorker, "rail-callback", cancellationToken).ConfigureAwait(false),
            RailSubmissionOutcome.Failed => await FailExternalProviderFailureAsync(payment, "External provider callback reported definitive failure.", PaymentActorType.SystemWorker, "rail-callback", cancellationToken).ConfigureAwait(false),
            _ => await MarkExternalPendingAsync(payment, PaymentFailureReasonCode.ProviderResponseAmbiguous, "External provider callback did not provide a final outcome.", PaymentActorType.SystemWorker, "rail-callback", cancellationToken).ConfigureAwait(false),
        };
        _ = result;
        await SaveAsync(cancellationToken).ConfigureAwait(false);
        return new RailCallbackProcessResult(true, false, inbox.Status.ToString());
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
        _dbContext.OutboxMessages.Add(OutboxMessage.Create(PaymentsTopic, payment.Id.ToString("D"), envelope.EventType, JsonSerializer.Serialize(envelope, SerializerOptions), now, envelope.EventId, envelope.EventVersion, "Payment", payment.Id.ToString("D")));
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
    private static PaymentDetailResponse MapDetail(Domain.Payments.Payment payment) => new(payment.Id, payment.CustomerId, payment.SourceAccountId, payment.Reference, payment.Status.ToString(), payment.PaymentType.ToString(), payment.Amount, payment.Currency.Code, MapDestination(payment.Destination), payment.Description, payment.CreatedAtUtc, payment.UpdatedAtUtc, payment.CompletedAtUtc, payment.FailedAtUtc, payment.ReversedAtUtc, payment.FundsReservationId, payment.LedgerTransactionId, payment.ReversalLedgerTransactionId, payment.ReversalReason, payment.ReasonCode?.ToString(), payment.ReasonDescription);
    private static PaymentDestinationResponse MapDestination(PaymentDestination destination) => new(destination.DestinationType.ToString(), destination.AccountId, destination.BankCode, Mask(destination.AccountNumber), destination.AccountName, destination.CountryCode);
    private static string? Mask(string? accountNumber) => string.IsNullOrWhiteSpace(accountNumber) ? null : accountNumber.Length <= 4 ? "****" : $"******{accountNumber[^4..]}";
}
