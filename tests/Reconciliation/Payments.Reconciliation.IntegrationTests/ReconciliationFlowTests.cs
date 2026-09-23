using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Payments.Reconciliation.Api;
using Microsoft.Extensions.Options;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;
using Testcontainers.PostgreSql;

namespace Payments.Reconciliation.IntegrationTests;

public sealed class ReconciliationDatabaseFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine").WithDatabase("payments_reconciliation_tests").WithUsername("payments").WithPassword("payments").Build();
    public string FileRoot { get; } = Path.Combine(Path.GetTempPath(), "payments-reconciliation-tests-" + Guid.NewGuid().ToString("N"));
    public string ConnectionString => _postgres.GetConnectionString();
    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var db = CreateContext();
        await db.Database.MigrateAsync();
    }
    public async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        if (Directory.Exists(FileRoot)) Directory.Delete(FileRoot, recursive: true);
    }
    public ReconciliationDbContext CreateContext() => new(new DbContextOptionsBuilder<ReconciliationDbContext>().UseNpgsql(ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)).Options);
}

public sealed class ReconciliationFlowTests(ReconciliationDatabaseFixture fixture) : IClassFixture<ReconciliationDatabaseFixture>
{
    private static readonly DateOnly SettlementDate = new(2026, 9, 1);
    private readonly LocalSettlementFileStore _store = new(fixture.FileRoot);
    private readonly ImmediatePolicy _policy = new();

    [Fact]
    public async Task Perfect_match_has_one_match_and_no_exception()
    {
        var source = new FakeSources();
        var payment = source.Add("Perfect", "RAIL-PERFECT", 50000m);
        var csv = Csv(Row(payment));
        var file = await ImportAndFinishAsync(source, "Perfect", csv);
        await using var db = fixture.CreateContext();
        Assert.Equal(FileStatus.Completed, file.Status);
        Assert.Equal(1, file.MatchedCount);
        Assert.Equal(0, file.ExceptionCount);
        Assert.Single(await db.Matches.Where(x => x.SettlementRecordId != null && x.PaymentId == payment.PaymentId).ToListAsync());
        Assert.Single(await db.OutboxMessages.Where(x => x.EventType == "reconciliation.run.completed" && x.PartitionKey == db.Runs.Where(r => r.SettlementFileId == file.Id).Select(r => r.Id.ToString()).Single()).ToListAsync());
    }

    [Fact]
    public async Task Duplicate_file_is_same_import_but_same_filename_with_new_content_is_new_import()
    {
        var source = new FakeSources();
        var firstPayment = source.Add("DuplicateFile", "RAIL-FIRST", 1m);
        var secondPayment = source.Add("DuplicateFile", "RAIL-SECOND", 2m);
        var firstCsv = Csv(Row(firstPayment));
        await using var db = fixture.CreateContext();
        var processor = Processor(db, source);
        var first = await ImportAsync(processor, "DuplicateFile", firstCsv, "settlement.csv");
        var replay = await ImportAsync(processor, "DuplicateFile", firstCsv, "settlement.csv");
        var second = await ImportAsync(processor, "DuplicateFile", Csv(Row(secondPayment)), "settlement.csv");
        Assert.Equal(first.Id, replay.Id);
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, await db.SettlementFiles.CountAsync(x => x.Provider == "DuplicateFile"));
    }

    [Fact]
    public async Task Mixed_file_classifies_duplicate_unknown_amount_currency_and_failed_payment()
    {
        var source = new FakeSources();
        var normal = source.Add("Mixed", "RAIL-NORMAL", 100m);
        var amount = source.Add("Mixed", "RAIL-AMOUNT", 200m);
        var currency = source.Add("Mixed", "RAIL-CURRENCY", 300m);
        var failed = source.Add("Mixed", "RAIL-FAILED", 400m, status: "Failed", ledger: false);
        var csv = Csv(Row(normal), Row(normal), "RAIL-UNKNOWN,UNKNOWN,1.00,NGN,SUCCESS,2026-09-01", Row(amount, amount: 250m), Row(currency, currency: "USD"), Row(failed));
        var file = await ImportAndFinishAsync(source, "Mixed", csv);
        await using var db = fixture.CreateContext();
        var codes = await db.Matches.Where(x => x.RunId == db.Runs.Where(r => r.SettlementFileId == file.Id).Select(r => r.Id).Single()).Select(x => x.ExceptionCode).ToListAsync();
        Assert.Contains(ExceptionCode.DUPLICATE_PROVIDER_RECORD, codes);
        Assert.Contains(ExceptionCode.UNKNOWN_PROVIDER_REFERENCE, codes);
        Assert.Contains(ExceptionCode.AMOUNT_MISMATCH, codes);
        Assert.Contains(ExceptionCode.CURRENCY_MISMATCH, codes);
        Assert.Contains(ExceptionCode.PAYMENT_FAILED_PROVIDER_SUCCESS, codes);
        Assert.Equal(1, file.MatchedCount);
        Assert.Equal(5, file.ExceptionCount);
        Assert.Equal(0, source.RecoveryCalls);
        Assert.Equal(5, await db.OutboxMessages.CountAsync(x => x.EventType == "reconciliation.exception.created"));
    }

    [Fact]
    public async Task Missing_expected_provider_success_is_found_by_reverse_matching()
    {
        var source = new FakeSources();
        var present = source.Add("Reverse", "RAIL-PRESENT", 10m);
        source.Add("Reverse", "RAIL-MISSING", 20m);
        var file = await ImportAndFinishAsync(source, "Reverse", Csv(Row(present)));
        await using var db = fixture.CreateContext();
        Assert.Equal(FileStatus.CompletedWithExceptions, file.Status);
        Assert.Contains(await db.Exceptions.Where(x => x.Provider == "Reverse").ToListAsync(), x => x.Code == ExceptionCode.SETTLEMENT_RECORD_MISSING);
    }

    [Fact]
    public async Task Restart_after_first_batch_resumes_without_duplicate_records_or_matches()
    {
        var source = new FakeSources();
        var rows = Enumerable.Range(1, 5).Select(i => Row(source.Add("Restart", "RAIL-RESTART-" + i, i))).ToArray();
        Guid fileId;
        await using (var firstDb = fixture.CreateContext())
        {
            var firstProcessor = Processor(firstDb, source, 2);
            var file = await ImportAsync(firstProcessor, "Restart", Csv(rows));
            fileId = file.Id;
            Assert.True(await firstProcessor.ProcessNextBatchAsync(fileId, CancellationToken.None));
        }
        await using (var resumedDb = fixture.CreateContext())
        {
            var resumed = Processor(resumedDb, source, 2);
            for (var i = 0; i < 4; i++) await resumed.ProcessNextBatchAsync(fileId, CancellationToken.None);
        }
        await using var check = fixture.CreateContext();
        Assert.Equal(5, await check.SettlementRecords.CountAsync(x => x.SettlementFileId == fileId));
        Assert.Equal(5, await check.Matches.CountAsync(x => x.RunId == check.Runs.Where(r => r.SettlementFileId == fileId).Select(r => r.Id).Single()));
        Assert.Equal(5, (await check.SettlementFiles.SingleAsync(x => x.Id == fileId)).MatchedCount);
    }

    [Fact]
    public async Task Settlement_success_requests_owner_recovery_and_rechecks_ledger()
    {
        var source = new FakeSources();
        var payment = source.Add("SettlementRecovery", "RAIL-SETTLEMENT-RECOVERY", 100m, status: "PendingReconciliation", ledger: false);
        source.Provider[payment.PaymentId] = new ProviderEvidence(payment.Provider, payment.ProviderReference, payment.ClientReference, "Successful", payment.Amount, payment.Currency, payment.ClientReference, "00", DateTimeOffset.UtcNow);
        var file = await ImportAndFinishAsync(source, "SettlementRecovery", Csv(Row(payment)));
        await using var db = fixture.CreateContext();
        Assert.Equal(FileStatus.Completed, file.Status);
        Assert.Equal(1, file.MatchedCount);
        Assert.Equal(1, source.RecoveryCalls);
        Assert.Equal("Completed", (await source.GetPaymentAsync(payment.PaymentId, CancellationToken.None))!.Status);
        Assert.Contains(await db.Matches.Where(x => x.SettlementRecordId != null).ToListAsync(), x => x.PaymentId == payment.PaymentId && x.Status == MatchStatus.Resolved);
    }
    [Fact]
    public async Task Active_worker_claims_durable_job_and_recovers_once()
    {
        var source = new FakeSources();
        var payment = source.Add("ActiveWorker", "RAIL-WORKER", 100m, status: "PendingReconciliation", ledger: false);
        source.Provider[payment.PaymentId] = new ProviderEvidence(payment.Provider, payment.ProviderReference, payment.ClientReference, "Successful", payment.Amount, payment.Currency, payment.ClientReference, "00", DateTimeOffset.UtcNow);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ReconciliationDbContext>(options => options.UseNpgsql(fixture.ConnectionString, npgsql => npgsql.EnableRetryOnFailure(3)));
        services.AddSingleton<ISettlementFileStore>(_store);
        services.AddSingleton<IReconciliationSources>(source);
        services.AddSingleton<IProviderReconciliationPolicy>(_policy);
        services.AddSingleton(Options.Create(new ReconciliationOptions { ConnectionString = fixture.ConnectionString, FileStorePath = fixture.FileRoot, ActiveBatchSize = 10, ActivePollSeconds = 5 }));
        services.AddScoped<ReconciliationProcessor>();
        await using var provider = services.BuildServiceProvider();
        var worker = ActivatorUtilities.CreateInstance<ActiveReconciliationWorker>(provider);
        await worker.StartAsync(CancellationToken.None);
        try
        {
            var deadline = DateTimeOffset.UtcNow.AddSeconds(20);
            while (DateTimeOffset.UtcNow < deadline)
            {
                await using var poll = fixture.CreateContext();
                if (source.RecoveryCalls == 1 && !await poll.ActiveJobs.AnyAsync(x => x.PaymentId == payment.PaymentId)) break;
                await Task.Delay(100);
            }
            Assert.Equal(1, source.RecoveryCalls);
        }
        finally { await worker.StopAsync(CancellationToken.None); worker.Dispose(); }
        await using var db = fixture.CreateContext();
        Assert.Equal(0, await db.ActiveJobs.CountAsync(x => x.PaymentId == payment.PaymentId));
        Assert.Contains(await db.Matches.Where(x => x.PaymentId == payment.PaymentId).ToListAsync(), x => x.Status == MatchStatus.Resolved);
    }
    [Fact]
    public async Task Crash_mid_batch_resumes_after_lease_without_duplicate_effects()
    {
        var source = new FakeSources { FailFindPaymentAfter = 3 };
        var rows = Enumerable.Range(1, 10).Select(i => Row(source.Add("Crash", $"RAIL-CRASH-{i}", i))).ToArray();
        Guid fileId;
        await using (var crashedDb = fixture.CreateContext())
        {
            var processor = Processor(crashedDb, source, 10);
            var file = await ImportAsync(processor, "Crash", Csv(rows));
            fileId = file.Id;
            await Assert.ThrowsAsync<HttpRequestException>(() => processor.ProcessNextBatchAsync(fileId, CancellationToken.None));
        }
        source.FailFindPaymentAfter = null;
        await using (var leaseDb = fixture.CreateContext())
        {
            var file = await leaseDb.SettlementFiles.SingleAsync(x => x.Id == fileId);
            file.LeaseUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1);
            await leaseDb.SaveChangesAsync();
        }
        await using (var resumedDb = fixture.CreateContext())
        {
            var resumed = Processor(resumedDb, source, 10);
            Assert.True(await resumed.ProcessNextBatchAsync(fileId, CancellationToken.None));
        }
        await using var check = fixture.CreateContext();
        var completed = await check.SettlementFiles.SingleAsync(x => x.Id == fileId);
        Assert.Equal(FileStatus.Completed, completed.Status);
        Assert.Equal(10, completed.MatchedCount);
        Assert.Equal(10, await check.Matches.CountAsync(x => x.RunId == check.Runs.Where(r => r.SettlementFileId == fileId).Select(r => r.Id).Single()));
        Assert.Equal(10, await check.SettlementRecords.CountAsync(x => x.SettlementFileId == fileId));
    }
    [Fact]
    public async Task Pending_success_recovery_is_idempotent_and_records_resolved_match()
    {
        var source = new FakeSources();
        var payment = source.Add("ActiveSuccess", "RAIL-ACTIVE-SUCCESS", 100m, status: "PendingReconciliation", ledger: false);
        source.Provider[payment.PaymentId] = new ProviderEvidence(payment.Provider, payment.ProviderReference, payment.ClientReference, "Successful", payment.Amount, payment.Currency, payment.ClientReference, "00", DateTimeOffset.UtcNow);
        await using var db = fixture.CreateContext();
        var processor = Processor(db, source);
        var first = await processor.ReconcileStatusAsync(payment, CancellationToken.None);
        var second = await processor.ReconcileStatusAsync(await source.GetPaymentAsync(payment.PaymentId, CancellationToken.None) ?? throw new Exception(), CancellationToken.None);
        Assert.Equal(MatchStatus.Resolved, first.Status);
        Assert.Equal(MatchStatus.Matched, second.Status);
        Assert.Equal(1, source.RecoveryCalls);
        Assert.Equal(2, await db.ProviderObservations.CountAsync(x => x.PaymentId == payment.PaymentId));
        Assert.Equal(2, await db.Runs.CountAsync(x => x.Provider == "ActiveSuccess"));
    }

    [Fact]
    public async Task Provider_failure_releases_through_owner_and_provider_outage_stays_pending()
    {
        var source = new FakeSources();
        var payment = source.Add("ActiveFailure", "RAIL-ACTIVE-FAIL", 100m, status: "PendingReconciliation", ledger: false);
        source.Provider[payment.PaymentId] = new ProviderEvidence(payment.Provider, payment.ProviderReference, payment.ClientReference, "Failed", payment.Amount, payment.Currency, payment.ClientReference, "51", DateTimeOffset.UtcNow);
        await using var db = fixture.CreateContext();
        var processor = Processor(db, source);
        Assert.Equal(MatchStatus.Resolved, (await processor.ReconcileStatusAsync(payment, CancellationToken.None)).Status);
        Assert.Equal("Failed", (await source.GetPaymentAsync(payment.PaymentId, CancellationToken.None))!.Status);
        var outage = source.Add("Outage", "RAIL-OUTAGE", 50m, status: "PendingReconciliation", ledger: false);
        source.ProviderDown = true;
        Assert.Equal(MatchStatus.Pending, (await processor.ReconcileStatusAsync(outage, CancellationToken.None)).Status);
        Assert.Equal("PendingReconciliation", (await source.GetPaymentAsync(outage.PaymentId, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task Delayed_payment_event_does_not_create_premature_unknown_exception()
    {
        var source = new FakeSources();
        var policy = new DelayedPolicy();
        var csv = Csv("RAIL-LAG,CLIENT-LAG,1.00,NGN,SUCCESS,2026-09-01");
        await using var db = fixture.CreateContext();
        var processor = Processor(db, source, policy: policy);
        var file = await ImportAsync(processor, "Lag", csv);
        Assert.False(await processor.ProcessNextBatchAsync(file.Id, CancellationToken.None));
        Assert.Equal(0, await db.Exceptions.CountAsync(x => x.Provider == "Lag"));
        source.Add("Lag", "RAIL-LAG", 1m, clientReference: "CLIENT-LAG");
        Assert.True(await processor.ProcessNextBatchAsync(file.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Manual_resolution_persists_actor_reason_audit_and_outbox_atomically()
    {
        await using var db = fixture.CreateContext();
        var item = new ReconciliationException
        {
            Provider = "ManualResolution",
            PaymentId = Guid.NewGuid(),
            ProviderReference = "RAIL-MANUAL",
            Code = ExceptionCode.AMOUNT_MISMATCH,
            Severity = Severity.Critical,
            Description = "Amounts disagree."
        };
        db.Exceptions.Add(item);
        await db.SaveChangesAsync();

        var workflow = new ReconciliationExceptionWorkflow(db);
        var resolved = await workflow.ResolveAsync(item.Id, "ops-user-42", "EVIDENCE_VERIFIED", "Provider evidence reviewed.", CancellationToken.None);

        Assert.NotNull(resolved);
        Assert.Equal(ExceptionStatus.Resolved, resolved.Status);
        Assert.NotNull(resolved.ResolvedAtUtc);
        var audit = await db.AuditEvents.SingleAsync(x => x.ExceptionId == item.Id && x.Action == "ExceptionResolved");
        Assert.Equal("ops-user-42", audit.Actor);
        Assert.Equal("EVIDENCE_VERIFIED", audit.ReasonCode);
        Assert.Equal("Provider evidence reviewed.", audit.Comment);
        var message = await db.OutboxMessages.SingleAsync(x => x.EventType == "reconciliation.exception.resolved" && x.PartitionKey == item.Id.ToString("D"));
        Assert.Contains("ops-user-42", message.Payload, StringComparison.Ordinal);
        await Assert.ThrowsAsync<InvalidOperationException>(() => workflow.ResolveAsync(item.Id, "other-user", "RETRY", "Should not duplicate.", CancellationToken.None));
        Assert.Equal(1, await db.AuditEvents.CountAsync(x => x.ExceptionId == item.Id && x.Action == "ExceptionResolved"));
        Assert.Equal(1, await db.OutboxMessages.CountAsync(x => x.EventType == "reconciliation.exception.resolved" && x.PartitionKey == item.Id.ToString("D")));
    }
    [Theory]
    [InlineData(10_000)]
    [InlineData(100_000)]
    public async Task Correct_settlement_rows_match_without_exceptions(int rowCount)
    {
        var source = new FakeSources();
        var builder = new StringBuilder("provider_reference,client_reference,amount,currency,status,settlement_date\n");
        for (var i = 0; i < rowCount; i++) builder.Append(Row(source.Add("TenThousand", $"RAIL-{i:D6}", 100m))).Append('\n');
        await using var db = fixture.CreateContext();
        var processor = Processor(db, source, 1000);
        var file = await ImportAsync(processor, "TenThousand", builder.ToString());
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        for (var i = 0; i < rowCount / 1000 + 2 && file.Status == FileStatus.Processing; i++)
        {
            await processor.ProcessNextBatchAsync(file.Id, CancellationToken.None);
            db.ChangeTracker.Clear();
            file = await db.SettlementFiles.SingleAsync(x => x.Id == file.Id);
        }
        Assert.Equal(FileStatus.Completed, file.Status);
        Assert.Equal(rowCount, file.MatchedCount);
        Assert.Equal(0, file.ExceptionCount);
        Assert.Equal(rowCount, await db.Matches.CountAsync(x => x.RunId == db.Runs.Where(r => r.SettlementFileId == file.Id).Select(r => r.Id).Single()));
        Console.WriteLine($"Local settlement baseline: {rowCount} rows matched in {stopwatch.Elapsed.TotalSeconds:0.00}s; {GC.GetTotalMemory(false) / (1024 * 1024)} MiB managed heap.");
    }
    private ReconciliationProcessor Processor(ReconciliationDbContext db, FakeSources sources, int batch = 100, IProviderReconciliationPolicy? policy = null)
        => new(db, _store, sources, policy ?? _policy, Options.Create(new ReconciliationOptions { ConnectionString = fixture.ConnectionString, FileStorePath = fixture.FileRoot, BatchSize = batch }));

    private static async Task<SettlementFile> ImportAsync(ReconciliationProcessor processor, string provider, string csv, string name = "settlement.csv")
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        return await processor.ImportAsync(provider, name, SettlementDate, "test-operator", stream, CancellationToken.None);
    }

    private async Task<SettlementFile> ImportAndFinishAsync(FakeSources sources, string provider, string csv)
    {
        await using var db = fixture.CreateContext();
        var processor = Processor(db, sources);
        var file = await ImportAsync(processor, provider, csv);
        for (var i = 0; i < 20 && file.Status == FileStatus.Processing; i++)
        {
            await processor.ProcessNextBatchAsync(file.Id, CancellationToken.None);
            db.ChangeTracker.Clear();
            file = await db.SettlementFiles.SingleAsync(x => x.Id == file.Id);
        }
        return file;
    }

    private static string Csv(params string[] rows) => "provider_reference,client_reference,amount,currency,status,settlement_date\n" + string.Join("\n", rows) + "\n";
    private static string Row(PaymentEvidence payment, decimal? amount = null, string? currency = null, string status = "SUCCESS")
        => $"{payment.ProviderReference},{payment.ClientReference},{amount ?? payment.Amount:0.00},{currency ?? payment.Currency},{status},2026-09-01";

    private class ImmediatePolicy : IProviderReconciliationPolicy
    {
        public bool IsSuccess(string provider, string status) => status is "SUCCESS" or "Successful";
        public bool IsFailure(string provider, string status) => status is "FAILED" or "Failed";
        public virtual TimeSpan MissingReferenceGrace(string provider) => TimeSpan.Zero;
        public TimeSpan SettlementDelay(string provider) => TimeSpan.Zero;
    }
    private sealed class DelayedPolicy : ImmediatePolicy
    {
        public override TimeSpan MissingReferenceGrace(string provider) => TimeSpan.FromMinutes(5);
    }

    private sealed class FakeSources : IReconciliationSources
    {
        private readonly Dictionary<Guid, PaymentEvidence> _payments = [];
        private readonly Dictionary<string, PaymentEvidence> _byProviderReference = new(StringComparer.Ordinal);
        private readonly Dictionary<string, PaymentEvidence> _byClientReference = new(StringComparer.Ordinal);
        private readonly Dictionary<Guid, LedgerEvidence> _ledgers = [];
        public Dictionary<Guid, ProviderEvidence> Provider { get; } = [];
        public bool ProviderDown { get; set; }
        public int? FailFindPaymentAfter { get; set; }
        private int _findPaymentCalls;
        public int RecoveryCalls { get; private set; }
        public PaymentEvidence Add(string provider, string providerRef, decimal amount, string status = "Completed", bool ledger = true, string? clientReference = null)
        {
            var id = Guid.NewGuid();
            var ledgerId = ledger ? Guid.NewGuid() : (Guid?)null;
            var payment = new PaymentEvidence(id, "PAY-" + id.ToString("N")[..12], provider, providerRef, clientReference ?? id.ToString("D"), amount, "NGN", status, ledgerId, DateTimeOffset.UtcNow);
            _payments[id] = payment;
            _byProviderReference[provider + ":" + providerRef] = payment;
            _byClientReference[provider + ":" + payment.ClientReference] = payment;
            if (ledgerId is { } transactionId) _ledgers[id] = new LedgerEvidence(transactionId, id.ToString("D"), "Posted", amount, "NGN", DateTimeOffset.UtcNow);
            return payment;
        }
        public Task<PaymentEvidence?> FindPaymentAsync(string provider, string? providerReference, string? clientReference, CancellationToken ct)
        {
            if (FailFindPaymentAfter is { } failAfter && ++_findPaymentCalls > failAfter) throw new HttpRequestException("Simulated process failure.");
            return Task.FromResult(providerReference is not null && _byProviderReference.TryGetValue(provider + ":" + providerReference, out var byProvider) ? byProvider : clientReference is not null && _byClientReference.TryGetValue(provider + ":" + clientReference, out var byClient) ? byClient : null);
        }
        public Task<PaymentEvidence?> GetPaymentAsync(Guid paymentId, CancellationToken ct) => Task.FromResult(_payments.GetValueOrDefault(paymentId));
        public Task<IReadOnlyList<PaymentEvidence>> PendingPaymentsAsync(int offset, int limit, CancellationToken ct) => Task.FromResult<IReadOnlyList<PaymentEvidence>>(_payments.Values.Where(x => x.Status is "Processing" or "SubmittedToRail" or "PendingReconciliation").OrderBy(x => x.PaymentId).Skip(offset).Take(limit).ToArray());
        public Task<LedgerEvidence?> FindLedgerAsync(PaymentEvidence payment, CancellationToken ct) => Task.FromResult(_ledgers.GetValueOrDefault(payment.PaymentId));
        public Task<ProviderEvidence?> QueryProviderAsync(PaymentEvidence payment, CancellationToken ct)
            => ProviderDown ? Task.FromException<ProviderEvidence?>(new HttpRequestException("Provider unavailable")) : Task.FromResult(Provider.GetValueOrDefault(payment.PaymentId));
        public Task<IReadOnlyList<PaymentEvidence>> ExpectedSettlementsAsync(string provider, DateOnly settlementDate, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<PaymentEvidence>>(_payments.Values.Where(x => x.Provider == provider && x.Status == "Completed").ToArray());
        public Task<PaymentEvidence?> RequestPaymentRecoveryAsync(Guid paymentId, CancellationToken ct)
        {
            var current = _payments[paymentId];
            if (current.Status is not ("PendingReconciliation" or "SubmittedToRail" or "Processing")) return Task.FromResult<PaymentEvidence?>(current);
            RecoveryCalls++;
            var status = Provider[paymentId].Status is "Failed" or "FAILED" ? "Failed" : "Completed";
            var ledgerId = status == "Completed" ? Guid.NewGuid() : (Guid?)null;
            var next = current with { Status = status, LedgerTransactionId = ledgerId };
            _payments[paymentId] = next;
            if (ledgerId is { } id) _ledgers[paymentId] = new LedgerEvidence(id, paymentId.ToString("D"), "Posted", current.Amount, current.Currency, DateTimeOffset.UtcNow);
            return Task.FromResult<PaymentEvidence?>(next);
        }
    }
}
