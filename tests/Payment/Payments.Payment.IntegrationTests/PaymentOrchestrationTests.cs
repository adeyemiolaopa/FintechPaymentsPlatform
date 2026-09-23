using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.Payment.Application.Payments;
using Payments.Payment.Domain.Payments;
using Payments.Payment.Infrastructure.Persistence;
using Payments.Payment.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace Payments.Payment.IntegrationTests;

public sealed class PaymentOrchestrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder().WithImage("postgres:18-alpine").WithDatabase("payments_payment_tests").WithUsername("payments").WithPassword("payments").Build();
    private readonly Guid _customerA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private readonly Guid _customerB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private readonly Guid _accountA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private readonly Guid _accountB = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly Guid _ledgerA = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private readonly Guid _ledgerB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private readonly Guid _ledgerClearing = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private TestClock _clock = null!;
    private FakeAccountClient _accountClient = null!;
    private FakeLedgerClient _ledgerClient = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _clock = new TestClock(new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero));
        _accountClient = new FakeAccountClient();
        _ledgerClient = new FakeLedgerClient();
        await using var dbContext = CreateDbContext();
        await dbContext.Database.MigrateAsync();
        await SeedReferencesAsync(dbContext);
    }

    public Task DisposeAsync() => _postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Successful_internal_transfer_completes_with_reservation_commit_ledger_post_timeline_and_outbox()
    {
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 40000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Rent"));

        response.Status.Should().Be("Completed");
        response.FundsReservationId.Should().NotBeNull();
        response.LedgerTransactionId.Should().NotBeNull();
        _accountClient.CommitCount.Should().Be(1);
        _accountClient.ActiveReservations.Should().Be(0);
        _ledgerClient.Transactions.Should().ContainSingle();
        _ledgerClient.Transactions.Single().Postings.Should().Contain(item => item.LedgerAccountId == _ledgerA && item.Side == "Debit" && item.Amount == 40000m);
        _ledgerClient.Transactions.Single().Postings.Should().Contain(item => item.LedgerAccountId == _ledgerB && item.Side == "Credit" && item.Amount == 40000m);

        var timeline = await service.GetTimelineAsync(response.PaymentId);
        timeline.Select(item => item.ToStatus).Should().ContainInOrder("Initiated", "PendingValidation", "FundsReserved", "Processing", "Completed");

        await using var dbContext = CreateDbContext();
        (await dbContext.OutboxMessages.CountAsync()).Should().BeGreaterThan(0);
        (await dbContext.PaymentAuditEvents.CountAsync()).Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Insufficient_funds_rejects_without_ledger_post_or_live_reservation()
    {
        _accountClient.AvailableBalance = 10000m;
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 40000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Too much"));

        response.Status.Should().Be("Rejected");
        response.ReasonCode.Should().Be(PaymentFailureReasonCode.InsufficientFunds.ToString());
        _ledgerClient.Transactions.Should().BeEmpty();
        _accountClient.ActiveReservations.Should().Be(0);
    }

    [Fact]
    public async Task Frozen_source_account_rejects_before_reservation()
    {
        _accountClient.Accounts[_accountA] = _accountClient.Accounts[_accountA] with { Status = "Frozen" };
        await using (var dbContext = CreateDbContext())
        {
            var reference = await dbContext.AccountReferences.SingleAsync(item => item.AccountId == _accountA);
            reference.Update("NGN", "Wallet", "Frozen", _clock.UtcNow);
            await dbContext.SaveChangesAsync();
        }

        var response = await CreatePaymentAsync(CreateService(), new CreatePaymentRequest(_accountA, "InternalTransfer", 1000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), null));
        response.Status.Should().Be("Rejected");
        response.ReasonCode.Should().Be(PaymentFailureReasonCode.SourceAccountFrozen.ToString());
        _accountClient.ReserveCount.Should().Be(0);
        _ledgerClient.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Currency_mismatch_rejects_before_reservation()
    {
        var response = await CreatePaymentAsync(CreateService(), new CreatePaymentRequest(_accountA, "InternalTransfer", 1000m, "USD", new PaymentDestinationRequest(AccountId: _accountB), null));
        response.Status.Should().Be("Rejected");
        response.ReasonCode.Should().Be(PaymentFailureReasonCode.CurrencyMismatch.ToString());
        _accountClient.ReserveCount.Should().Be(0);
    }

    [Fact]
    public async Task External_bank_transfer_is_accepted_but_not_marked_completed_without_external_rail_confirmation()
    {
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "ExternalBankTransfer", 15000m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "External payout"));

        response.Status.Should().Be("Processing");
        response.FundsReservationId.Should().NotBeNull();

        response.LedgerTransactionId.Should().BeNull();
        _accountClient.ActiveReservations.Should().Be(1);
        _accountClient.CommitCount.Should().Be(0);
        _ledgerClient.Transactions.Should().BeEmpty();

        var timeline = await service.GetTimelineAsync(response.PaymentId);
        timeline.Select(item => item.ToStatus).Should().ContainInOrder("Initiated", "PendingValidation", "FundsReserved", "Processing");
        timeline.Select(item => item.ToStatus).Should().NotContain("Completed");
    }
    [Fact]
    public async Task External_bank_transfer_provider_success_completes_with_external_clearing_ledger_post()
    {
        var rail = new FakeRailAdapter { SubmitResult = new RailSubmissionResult(RailSubmissionOutcome.Succeeded, "RAIL-1", "00", "Successful", _clock.UtcNow, null, "Successful") };
        var service = CreateService(rail);

        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "ExternalBankTransfer", 50000m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "External payout"));

        response.Status.Should().Be("Completed");
        response.LedgerTransactionId.Should().NotBeNull();
        _accountClient.ActiveReservations.Should().Be(0);
        _accountClient.CommitCount.Should().Be(1);
        _ledgerClient.Transactions.Should().ContainSingle();
        _ledgerClient.Transactions.Single().TransactionType.Should().Be("ExternalBankTransfer");
        _ledgerClient.Transactions.Single().Postings.Should().Contain(item => item.LedgerAccountId == _ledgerA && item.Side == "Debit" && item.Amount == 50000m);
        _ledgerClient.Transactions.Single().Postings.Should().Contain(item => item.LedgerAccountId == _ledgerClearing && item.Side == "Credit" && item.Amount == 50000m);

        await using var dbContext = CreateDbContext();
        (await dbContext.RailSubmissions.CountAsync()).Should().Be(1);
        (await dbContext.RailSubmissionAttempts.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task External_bank_transfer_definitive_provider_failure_releases_reservation_without_ledger_post()
    {
        var rail = new FakeRailAdapter { SubmitResult = new RailSubmissionResult(RailSubmissionOutcome.Failed, "RAIL-FAIL", "51", "Declined", _clock.UtcNow, null, "Failed") };
        var response = await CreatePaymentAsync(CreateService(rail), new CreatePaymentRequest(_accountA, "ExternalBankTransfer", 50000m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "External payout"));

        response.Status.Should().Be("Failed");
        response.ReasonCode.Should().Be(PaymentFailureReasonCode.ProviderDeclined.ToString());
        _accountClient.ActiveReservations.Should().Be(0);
        _ledgerClient.Transactions.Should().BeEmpty();
    }

    [Fact]
    public async Task Timeout_after_processing_stays_pending_then_status_recovery_completes_once()
    {
        var rail = new FakeRailAdapter
        {
            SubmitResult = new RailSubmissionResult(RailSubmissionOutcome.Ambiguous, "RAIL-TIMEOUT", "TIMEOUT", "Timed out after provider may have processed.", _clock.UtcNow, TimeSpan.FromSeconds(1), "Timeout"),

        };
        var service = CreateService(rail);
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "ExternalBankTransfer", 50000m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "External payout"));

        response.Status.Should().Be("PendingReconciliation");
        rail.StatusResult = new RailStatusResult(RailSubmissionOutcome.Succeeded, "RAIL-TIMEOUT", "00", "Successful", _clock.UtcNow, null, "Successful", null, 50000m, "NGN", response.PaymentId.ToString("D"));
        response.LedgerTransactionId.Should().BeNull();
        _accountClient.ActiveReservations.Should().Be(1);
        _ledgerClient.Transactions.Should().BeEmpty();

        _clock.Advance(TimeSpan.FromSeconds(2));
        var recovered = await service.RecoverAsync(10, TimeSpan.FromSeconds(1));
        recovered.Should().Be(1);
        var completed = await service.GetAsync(response.PaymentId);
        completed.Status.Should().Be("Completed");
        _ledgerClient.Transactions.Should().ContainSingle();
        _accountClient.CommitCount.Should().Be(1);
        rail.SubmitCount.Should().Be(1);
        rail.StatusCount.Should().Be(1);
    }

    [Fact]
    public async Task Duplicate_success_callbacks_create_one_financial_effect()
    {
        var rail = new FakeRailAdapter { SubmitResult = new RailSubmissionResult(RailSubmissionOutcome.Ambiguous, "RAIL-CB", "TIMEOUT", "Timed out", _clock.UtcNow, TimeSpan.FromSeconds(30), "Timeout") };
        var service = CreateService(rail, new AlwaysValidRailCallbackAuthenticator());
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "ExternalBankTransfer", 50000m, "NGN", new PaymentDestinationRequest(BankCode: "058", AccountNumber: "0123456789", AccountName: "Ada Lovelace", CountryCode: "NG"), "External payout"));
        response.Status.Should().Be("PendingReconciliation");

        var raw = JsonSerializer.Serialize(new
        {
            eventId = "evt-1",
            providerReference = "RAIL-CB",
            clientReference = response.PaymentId.ToString("D"),
            status = "Successful",
            amount = 50000m,
            currency = "NGN",
            responseCode = "00",
            occurredAtUtc = new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero)
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var headers = new Dictionary<string, string?> { ["X-Rail-Timestamp"] = "1800000000", ["X-Rail-Signature"] = "signature" };

        var first = await service.ProcessCallbackAsync("SimulatorRail", raw, headers);
        var second = await service.ProcessCallbackAsync("SimulatorRail", raw, headers);

        first.Accepted.Should().BeTrue();
        second.Duplicate.Should().BeTrue();
        var completed = await service.GetAsync(response.PaymentId);
        completed.Status.Should().Be("Completed");
        _ledgerClient.Transactions.Should().ContainSingle();
        _accountClient.CommitCount.Should().Be(1);
    }
    [Fact]
    public async Task Ledger_timeout_after_commit_is_recovered_idempotently_without_duplicate_posting()
    {
        _ledgerClient.ThrowTransientAfterCommitOnce = true;
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 30000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Retry"));
        response.Status.Should().Be("Processing");
        _ledgerClient.Transactions.Should().ContainSingle();
        _accountClient.CommitCount.Should().Be(0);

        _clock.Advance(TimeSpan.FromMinutes(5));
        var recovered = await service.RecoverAsync(10, TimeSpan.FromSeconds(1));
        recovered.Should().Be(1);
        var completed = await service.GetAsync(response.PaymentId);
        completed.Status.Should().Be("Completed");
        _ledgerClient.Transactions.Should().ContainSingle();
        _accountClient.CommitCount.Should().Be(1);
    }

    [Fact]
    public async Task Ledger_unavailable_after_reservation_leaves_payment_recoverable()
    {
        _ledgerClient.AlwaysUnavailable = true;
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 30000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Wait"));
        response.Status.Should().Be("Processing");
        response.FundsReservationId.Should().NotBeNull();
        _accountClient.ActiveReservations.Should().Be(1);

        _ledgerClient.AlwaysUnavailable = false;
        _clock.Advance(TimeSpan.FromMinutes(5));
        await service.RecoverAsync(10, TimeSpan.FromSeconds(1));
        var completed = await service.GetAsync(response.PaymentId);
        completed.Status.Should().Be("Completed");
        _accountClient.ActiveReservations.Should().Be(0);
    }

    [Fact]
    public async Task Concurrent_duplicate_payment_initiation_creates_one_payment_one_reservation_and_one_ledger_effect()
    {
        var key = "idem-concurrent-100";
        var request = new CreatePaymentRequest(_accountA, "InternalTransfer", 1000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Concurrent retry");

        var tasks = Enumerable.Range(0, 100)
            .Select(_ => CreateService().CreateAsync(request, key))
            .ToArray();
        var results = await Task.WhenAll(tasks);

        results.Select(item => item.Payment.PaymentId).Distinct().Should().ContainSingle();
        var allowedStatuses = new[] { "Initiated", "PendingValidation", "FundsReserved", "Processing", "Completed" };
        results.Should().OnlyContain(item => allowedStatuses.Contains(item.Payment.Status));
        _accountClient.ReserveCount.Should().Be(1);
        _accountClient.CommitCount.Should().Be(1);
        _ledgerClient.Transactions.Should().ContainSingle();

        await using var dbContext = CreateDbContext();
        (await dbContext.IdempotencyRecords.CountAsync()).Should().Be(1);
        (await dbContext.Payments.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_key_with_different_payload_returns_conflict_without_second_payment()
    {
        var key = "idem-conflict-amount";
        var first = new CreatePaymentRequest(_accountA, "InternalTransfer", 10000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Original");
        var second = first with { Amount = 20000m };

        var created = await CreateService().CreateAsync(first, key);
        Func<Task> act = () => CreateService().CreateAsync(second, key);

        created.Payment.PaymentId.Should().NotBeEmpty();
        await act.Should().ThrowAsync<IdempotencyKeyConflictException>();
        await using var dbContext = CreateDbContext();
        (await dbContext.IdempotencyRecords.CountAsync()).Should().Be(1);
        (await dbContext.Payments.CountAsync()).Should().Be(1);
        _accountClient.ReserveCount.Should().Be(1);
        _ledgerClient.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Lost_response_or_service_restart_replay_returns_same_payment_from_postgres()
    {
        var key = "idem-response-lost";
        var request = new CreatePaymentRequest(_accountA, "InternalTransfer", 12000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Response lost");

        var original = await CreateService().CreateAsync(request, key);
        var replay = await CreateService().CreateAsync(request, key);

        replay.Replayed.Should().BeTrue();
        replay.Payment.PaymentId.Should().Be(original.Payment.PaymentId);
        _accountClient.ReserveCount.Should().Be(1);
        _ledgerClient.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Duplicate_request_while_original_workflow_is_processing_returns_accepted_without_second_workflow()
    {
        var key = "idem-processing-duplicate";
        var request = new CreatePaymentRequest(_accountA, "InternalTransfer", 13000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Slow rail");
        _ledgerClient.BlockPosting = true;

        var originalTask = CreateService().CreateAsync(request, key);
        await _ledgerClient.PostingBlocked.Task.WaitAsync(TimeSpan.FromSeconds(10));

        var duplicate = await CreateService().CreateAsync(request, key);
        duplicate.Replayed.Should().BeTrue();
        duplicate.StatusCode.Should().Be(202);
        duplicate.Payment.Status.Should().Be("Processing");
        _accountClient.ReserveCount.Should().Be(1);
        _ledgerClient.Transactions.Should().BeEmpty();

        _ledgerClient.AllowPosting.SetResult();
        var original = await originalTask;
        duplicate.Payment.PaymentId.Should().Be(original.Payment.PaymentId);
        _ledgerClient.Transactions.Should().ContainSingle();
    }

    [Fact]
    public async Task Request_idempotency_does_not_depend_on_redis_or_kafka_clients()
    {
        var key = "idem-no-cache-no-broker";
        var request = new CreatePaymentRequest(_accountA, "InternalTransfer", 14000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "No cache");

        var first = await CreateService().CreateAsync(request, key);
        var second = await CreateService().CreateAsync(request, key);

        second.Replayed.Should().BeTrue();
        second.Payment.PaymentId.Should().Be(first.Payment.PaymentId);
        await using var dbContext = CreateDbContext();
        (await dbContext.OutboxMessages.CountAsync()).Should().BeGreaterThan(0);
        (await dbContext.IdempotencyRecords.CountAsync()).Should().Be(1);
    }
    [Fact]
    public async Task Consistency_checker_reports_zero_violations_for_completed_internal_transfer()
    {
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 15000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Consistency"));

        var consistency = await service.VerifyConsistencyAsync(response.PaymentId);

        consistency.IsConsistent.Should().BeTrue();
        consistency.Violations.Should().BeEmpty();
    }

    [Fact]
    public async Task Consistency_checker_detects_uncommitted_reservation_drift()
    {
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 16000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Drift"));
        _accountClient.ForceReservationStatus(response.FundsReservationId!.Value, "Active");

        var consistency = await service.VerifyConsistencyAsync(response.PaymentId);

        consistency.IsConsistent.Should().BeFalse();
        consistency.Violations.Should().Contain(item => item.Code == "payment.reservation.not_committed");
    }

    [Fact]
    public async Task Completed_internal_transfer_can_be_reversed_once_with_stable_reversal_reference()
    {
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 17000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), "Reverse me"));

        var reversed = await service.ReverseAsync(response.PaymentId, new ReversePaymentRequest("Operations correction"));
        var duplicate = await service.ReverseAsync(response.PaymentId, new ReversePaymentRequest("Operations correction"));
        var detail = await service.GetAsync(response.PaymentId);

        reversed.Status.Should().Be("Reversed");
        duplicate.Status.Should().Be("Reversed");
        detail.ReversalLedgerTransactionId.Should().NotBeNull();
        _ledgerClient.ReverseCount.Should().Be(1);
        var timeline = await service.GetTimelineAsync(response.PaymentId);
        timeline.Select(item => item.ToStatus).Should().ContainInOrder("ReversalPending", "Reversed");
    }

    [Fact]
    public async Task Processing_payment_cannot_be_cancelled_without_reversal_semantics()
    {
        _ledgerClient.AlwaysUnavailable = true;
        var service = CreateService();
        var response = await CreatePaymentAsync(service, new CreatePaymentRequest(_accountA, "InternalTransfer", 5000m, "NGN", new PaymentDestinationRequest(AccountId: _accountB), null));
        response.Status.Should().Be("Processing");
        Func<Task> act = () => service.CancelAsync(response.PaymentId, new CancelPaymentRequest("stop"));
        await act.Should().ThrowAsync<Payments.BuildingBlocks.Application.Exceptions.ConflictException>();
        _accountClient.ActiveReservations.Should().Be(1);
    }

    private static async Task<PaymentResponse> CreatePaymentAsync(PaymentService service, CreatePaymentRequest request, string? idempotencyKey = null)
        => (await service.CreateAsync(request, idempotencyKey ?? Guid.NewGuid().ToString("D")).ConfigureAwait(false)).Payment;
    private PaymentService CreateService(IPaymentRailAdapter? railAdapter = null, IRailCallbackAuthenticator? callbackAuthenticator = null)
        => new(CreateDbContext(), _clock, new TestCurrentUser(_customerA), new TestRequestContext(), _accountClient, _ledgerClient, new CreatePaymentRequestValidator(), new PaymentSearchRequestValidator(), null, railAdapter is null ? null : new FakeRailRouter(railAdapter), Options.Create(new ExternalTransferOptions { DefaultClearingLedgerAccountId = _ledgerClearing }), callbackAuthenticator);

    private PaymentDbContext CreateDbContext() => new(new DbContextOptionsBuilder<PaymentDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private async Task SeedReferencesAsync(PaymentDbContext dbContext)
    {
        dbContext.CustomerReferences.Add(CustomerReference.Upsert(_customerA, "Active", "BasicVerified", _clock.UtcNow));
        dbContext.CustomerReferences.Add(CustomerReference.Upsert(_customerB, "Active", "BasicVerified", _clock.UtcNow));
        dbContext.AccountReferences.Add(AccountReference.Upsert(_accountA, _customerA, "NGN", "Wallet", "Active", _ledgerA, _clock.UtcNow));
        dbContext.AccountReferences.Add(AccountReference.Upsert(_accountB, _customerB, "NGN", "Wallet", "Active", _ledgerB, _clock.UtcNow));
        await dbContext.SaveChangesAsync();
        _accountClient.Accounts[_accountA] = new AccountClientResponse(_accountA, _customerA, "NGN", "Wallet", "Active");
        _accountClient.Accounts[_accountB] = new AccountClientResponse(_accountB, _customerB, "NGN", "Wallet", "Active");
    }

    private sealed class TestClock : IClock
    {
        public TestClock(DateTimeOffset now) => UtcNow = now;
        public DateTimeOffset UtcNow { get; private set; }
        public void Advance(TimeSpan timeSpan) => UtcNow = UtcNow.Add(timeSpan);
    }

    private sealed class TestCurrentUser : ICurrentUser
    {
        public TestCurrentUser(Guid customerId) => CustomerId = customerId.ToString("D");
        public string? UserId => "99999999-9999-9999-9999-999999999999";
        public string? CustomerId { get; }
        public IReadOnlyCollection<string> Roles => ["Customer"];
        public IReadOnlyCollection<string> Permissions => ["payment.create", "payment.read.self", "payment.cancel.self", "payment.reverse", "payment.audit.read"];
        public bool IsAuthenticated => true;
        public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TestRequestContext : IRequestContext
    {
        public string CorrelationId => "corr-payment-tests";
        public string? CausationId => null;
        public string TraceId => "trace-payment-tests";
    }


    private sealed class FakeRailRouter : IRailRouter
    {
        private readonly IPaymentRailAdapter _adapter;
        public FakeRailRouter(IPaymentRailAdapter adapter) => _adapter = adapter;
        public Task<RailRoute> RouteAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default)
            => Task.FromResult(new RailRoute(_adapter.ProviderName, instruction.DestinationCountryCode, instruction.Currency, PaymentType.ExternalBankTransfer.ToString(), _adapter));
    }

    private sealed class FakeRailAdapter : IPaymentRailAdapter
    {
        public string ProviderName => "SimulatorRail";
        public RailSubmissionResult SubmitResult { get; set; } = new(RailSubmissionOutcome.Pending, null, "01", "Pending", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), "Processing");
        public RailStatusResult StatusResult { get; set; } = new(RailSubmissionOutcome.Pending, null, "01", "Pending", DateTimeOffset.UtcNow, TimeSpan.FromSeconds(1), "Processing");
        public int SubmitCount { get; private set; }
        public int StatusCount { get; private set; }

        public Task<RailSubmissionResult> SubmitTransferAsync(RailTransferInstruction instruction, CancellationToken cancellationToken = default)
        {
            SubmitCount++;
            return Task.FromResult(SubmitResult);
        }

        public Task<RailStatusResult> GetTransferStatusAsync(string clientReference, string? providerReference, CancellationToken cancellationToken = default)
        {
            StatusCount++;
            return Task.FromResult(StatusResult);
        }

        public Task<bool> ValidateDestinationAsync(PaymentDestinationRequest destination, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class AlwaysValidRailCallbackAuthenticator : IRailCallbackAuthenticator
    {
        public bool Validate(RailCallbackAuthenticationInput input) => true;
    }
    private sealed class FakeAccountClient : IAccountServiceClient
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, ReservationClientResponse> _reservations = [];
        private int _reserveCount;
        private int _commitCount;

        public Dictionary<Guid, AccountClientResponse> Accounts { get; } = [];
        public decimal AvailableBalance { get; set; } = 100000m;
        public int ReserveCount { get { lock (_gate) return _reserveCount; } }
        public int CommitCount { get { lock (_gate) return _commitCount; } }
        public int ActiveReservations { get { lock (_gate) return _reservations.Values.Count(item => item.Status == "Active"); } }

        public Task<AccountClientResponse> GetAccountAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(Accounts.TryGetValue(accountId, out var account) ? account : throw new DownstreamBusinessException(PaymentFailureReasonCode.SourceAccountNotFound, "Account not found"));

        public Task<AccountBalanceClientResponse> GetBalanceAsync(Guid accountId, CancellationToken cancellationToken = default)
            => Task.FromResult(new AccountBalanceClientResponse(accountId, "NGN", AvailableBalance, 0m, AvailableBalance, DateTimeOffset.UtcNow));

        public Task<ReservationClientResponse> ReserveFundsAsync(Guid accountId, CreateFundsReservationCommand command, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_reservations.TryGetValue(command.ReferenceId, out var existing)) return Task.FromResult(existing);
                _reserveCount++;
                if (AvailableBalance < command.Amount) throw new DownstreamBusinessException(PaymentFailureReasonCode.InsufficientFunds, "Insufficient funds");
                AvailableBalance -= command.Amount;
                var reservation = new ReservationClientResponse(Guid.NewGuid(), accountId, command.ReferenceId, command.Amount, command.Currency, "Active", DateTimeOffset.UtcNow, command.ExpiresAtUtc);
                _reservations[command.ReferenceId] = reservation;
                return Task.FromResult(reservation);
            }
        }

        public Task<ReservationClientResponse> GetReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_reservations.Values.Single(item => item.ReservationId == reservationId && item.AccountId == accountId));
            }
        }

        public Task<ReservationClientResponse> CommitReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var reservation = _reservations.Values.Single(item => item.ReservationId == reservationId);
                if (reservation.Status == "Committed") return Task.FromResult(reservation);
                _commitCount++;
                var committed = reservation with { Status = "Committed" };
                _reservations[reservation.ReferenceId] = committed;
                return Task.FromResult(committed);
            }
        }

        public void ForceReservationStatus(Guid reservationId, string status)
        {
            lock (_gate)
            {
                var reservation = _reservations.Values.Single(item => item.ReservationId == reservationId);
                _reservations[reservation.ReferenceId] = reservation with { Status = status };
            }
        }

        public Task<ReservationClientResponse> ReleaseReservationAsync(Guid accountId, Guid reservationId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var reservation = _reservations.Values.Single(item => item.ReservationId == reservationId);
                if (reservation.Status == "Released") return Task.FromResult(reservation);
                AvailableBalance += reservation.Amount;
                var released = reservation with { Status = "Released" };
                _reservations[reservation.ReferenceId] = released;
                return Task.FromResult(released);
            }
        }
    }

    private sealed class FakeLedgerClient : ILedgerServiceClient
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<string, LedgerTransactionClientResponse> _responses = [];
        private readonly Dictionary<Guid, LedgerTransactionClientResponse> _responsesById = [];

        public bool ThrowTransientAfterCommitOnce { get; set; }
        public bool AlwaysUnavailable { get; set; }
        public bool BlockPosting { get; set; }
        public TaskCompletionSource PostingBlocked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowPosting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public List<PostLedgerTransactionCommand> Transactions { get; } = [];
        public int ReverseCount { get; private set; }

        public Task<LedgerTransactionClientResponse> GetTransactionAsync(Guid transactionId, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                return Task.FromResult(_responsesById.TryGetValue(transactionId, out var response) ? response : throw new DownstreamBusinessException(PaymentFailureReasonCode.SystemFailure, "Ledger transaction not found"));
            }
        }

        public Task<LedgerTransactionClientResponse> ReverseTransactionAsync(Guid transactionId, ReverseLedgerTransactionCommand command, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_responses.TryGetValue(command.ExternalReference, out var existing)) return Task.FromResult(existing);
                var original = _responsesById.TryGetValue(transactionId, out var found) ? found : throw new DownstreamBusinessException(PaymentFailureReasonCode.SystemFailure, "Original ledger transaction not found");
                var postings = (original.Postings ?? []).Select(posting => new LedgerPostingClientResponse(Guid.NewGuid(), posting.LedgerAccountId, posting.Side == "Debit" ? "Credit" : "Debit", posting.Amount, posting.Currency, posting.Sequence, posting.Description, DateTimeOffset.UtcNow)).ToArray();
                var reversal = new LedgerTransactionClientResponse(Guid.NewGuid(), command.ExternalReference, "Posted", "Reversal", original.Currency, transactionId, command.Reason, postings);
                ReverseCount++;
                _responses[command.ExternalReference] = reversal;
                _responsesById[reversal.TransactionId] = reversal;
                return Task.FromResult(reversal);
            }
        }

        public async Task<LedgerTransactionClientResponse> PostTransactionAsync(PostLedgerTransactionCommand command, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                if (_responses.TryGetValue(command.ExternalReference, out var existing)) return existing;
                if (AlwaysUnavailable) throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Ledger unavailable");
            }

            if (BlockPosting)
            {
                PostingBlocked.TrySetResult();
                await AllowPosting.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            lock (_gate)
            {
                if (_responses.TryGetValue(command.ExternalReference, out var existing)) return existing;
                if (AlwaysUnavailable) throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Ledger unavailable");
                Transactions.Add(command);
                var postings = command.Postings.Select((posting, index) => new LedgerPostingClientResponse(Guid.NewGuid(), posting.LedgerAccountId, posting.Side, posting.Amount, command.Currency, index + 1, posting.Description, DateTimeOffset.UtcNow)).ToArray();
                var response = new LedgerTransactionClientResponse(Guid.NewGuid(), command.ExternalReference, "Posted", command.TransactionType, command.Currency, null, null, postings);
                _responses[command.ExternalReference] = response;
                _responsesById[response.TransactionId] = response;
                if (ThrowTransientAfterCommitOnce)
                {
                    ThrowTransientAfterCommitOnce = false;
                    throw new DownstreamTransientException(PaymentFailureReasonCode.LedgerServiceUnavailable, "Response timed out after commit");
                }

                return response;
            }
        }
    }
}
