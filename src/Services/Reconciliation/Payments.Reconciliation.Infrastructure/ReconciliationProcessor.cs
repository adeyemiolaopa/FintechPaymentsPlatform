using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationOptions
{
    public string ConnectionString { get; set; } = "Host=127.0.0.1;Port=15432;Database=payments_reconciliation;Username=payments;Password=change-me-local-only";
    public string FileStorePath { get; set; } = "settlement-files";
    public int BatchSize { get; set; } = 100;
    public int ActiveBatchSize { get; set; } = 100;
    public int ActivePollSeconds { get; set; } = 120;
    public int SettlementPollSeconds { get; set; } = 10;
    public long MaxFileBytes { get; set; } = 150_000_000;
}

public sealed class ReconciliationProcessor(ReconciliationDbContext db, ISettlementFileStore store, IReconciliationSources sources, IProviderReconciliationPolicy policy, Microsoft.Extensions.Options.IOptions<ReconciliationOptions> options)
{
    private static readonly ActivitySource Tracing = new("Payments.Reconciliation");
    private static readonly Meter Meter = new("Payments.Reconciliation");
    private static readonly Counter<long> FilesReceived = Meter.CreateCounter<long>("settlement_files_received_total");
    private static readonly Counter<long> RecordsProcessed = Meter.CreateCounter<long>("reconciliation_records_processed_total");
    private static readonly Counter<long> Matches = Meter.CreateCounter<long>("reconciliation_matches_total");
    private static readonly Counter<long> Exceptions = Meter.CreateCounter<long>("reconciliation_exceptions_total");
    private static readonly Counter<long> Runs = Meter.CreateCounter<long>("reconciliation_runs_total");
    private static readonly Counter<long> AutoResolved = Meter.CreateCounter<long>("reconciliation_auto_resolved_total");
    private static readonly Counter<long> FileFailures = Meter.CreateCounter<long>("settlement_file_failures_total");
    private static readonly Histogram<double> RunDuration = Meter.CreateHistogram<double>("reconciliation_run_duration_seconds");
    private static readonly Histogram<double> FileDuration = Meter.CreateHistogram<double>("settlement_file_processing_duration_seconds");
    private readonly ReconciliationOptions _options = options.Value;

    public async Task<SettlementFile> ImportAsync(string provider, string fileName, DateOnly settlementDate, string actor, Stream content, CancellationToken ct)
    {
        using var activity = Tracing.StartActivity("settlement.file.ingest");
        if (provider.Length is < 1 or > 64 || provider.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new InvalidDataException("Invalid provider.");
        if (fileName.Length is < 1 or > 255 || fileName != Path.GetFileName(fileName) || !fileName.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Invalid CSV filename.");
        var staged = await store.StageAsync(content, _options.MaxFileBytes, ct).ConfigureAwait(false);
        try
        {
            var existing = await db.SettlementFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Provider == provider && x.FileHash == staged.Hash, ct).ConfigureAwait(false);
            if (existing is not null) return existing;
            var file = new SettlementFile { Provider = provider, FileName = fileName, FileHash = staged.Hash, SettlementDate = settlementDate, UploadedBy = actor, Status = FileStatus.Validating };
            long count = 0;
            try
            {
                {
                    using var stream = new FileStream(staged.TemporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
                foreach (var record in SettlementCsvParser.Parse(stream, file.Id))
                {
                    if (record.SettlementDate != settlementDate) throw new InvalidDataException($"Settlement date mismatch at line {record.SourceLineNumber}.");
                    count++;
                }
                }
                if (count == 0) throw new InvalidDataException("Settlement file contains no records.");
                file.RecordCount = count;
                file.Status = FileStatus.Processing;
                await store.CommitAsync(staged, provider, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is InvalidDataException or DecoderFallbackException or Microsoft.VisualBasic.FileIO.MalformedLineException)
            {
                file.Status = FileStatus.Rejected;
                file.RejectionReason = exception.Message.Length > 500 ? exception.Message[..500] : exception.Message;
            }
            db.SettlementFiles.Add(file);
            db.AuditEvents.Add(new ReconciliationAuditEvent { SettlementFileId = file.Id, Action = file.Status == FileStatus.Rejected ? "FileRejected" : "FileReceived", Actor = actor, Comment = $"SHA-256 {file.FileHash}; records {file.RecordCount}" });
            try { await db.SaveChangesAsync(ct).ConfigureAwait(false); }
            catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                db.ChangeTracker.Clear();
                return await db.SettlementFiles.AsNoTracking().SingleAsync(x => x.Provider == provider && x.FileHash == staged.Hash, ct).ConfigureAwait(false);
            }
            if (file.Status == FileStatus.Rejected) FileFailures.Add(1, new KeyValuePair<string, object?>("provider", provider));
            FilesReceived.Add(1, new KeyValuePair<string, object?>("provider", provider));
            return file;
        }
        finally { store.Discard(staged); }
    }

    public async Task<bool> ProcessNextBatchAsync(Guid fileId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        var claimed = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var candidate = await db.SettlementFiles.FromSqlInterpolated($"SELECT * FROM reconciliation.settlement_files WHERE \"Id\" = {fileId} AND \"Status\" = 'Processing' AND (\"LeaseUntilUtc\" IS NULL OR \"LeaseUntilUtc\" <= {now}) FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (candidate is null) return false;
            candidate.LeaseOwner = Environment.MachineName;
            candidate.LeaseUntilUtc = now.AddMinutes(30);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return true;
        }).ConfigureAwait(false);
        if (!claimed) return false;
        db.ChangeTracker.Clear();

        var file = await db.SettlementFiles.SingleAsync(x => x.Id == fileId, ct).ConfigureAwait(false);
        var run = await db.Runs.SingleOrDefaultAsync(x => x.SettlementFileId == fileId, ct).ConfigureAwait(false);
        if (run is null)
        {
            run = new ReconciliationRun { Provider = file.Provider, SettlementDate = file.SettlementDate, SettlementFileId = file.Id, Mode = ReconciliationMode.SettlementFile };
            db.Runs.Add(run);
            db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = run.Id, SettlementFileId = file.Id, Action = "RunStarted", Actor = "reconciliation-worker" });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
        await SettlementRecordStager.StageAsync(db, store, file, 1000, ct).ConfigureAwait(false);
        var batch = await db.SettlementRecords.AsNoTracking()
            .Where(x => x.SettlementFileId == file.Id && x.SourceLineNumber > file.LastProcessedLine)
            .OrderBy(x => x.SourceLineNumber)
            .Take(Math.Clamp(_options.BatchSize, 1, 1000))
            .ToListAsync(ct).ConfigureAwait(false);
        var references = batch.Select(x => x.ProviderReference).Distinct(StringComparer.Ordinal).ToArray();
        var priorReferences = await db.SettlementRecords.AsNoTracking()
            .Where(x => x.SettlementFileId == file.Id && x.SourceLineNumber <= file.LastProcessedLine && references.Contains(x.ProviderReference))
            .Select(x => x.ProviderReference).Distinct().ToListAsync(ct).ConfigureAwait(false);
        var seen = new HashSet<string>(priorReferences, StringComparer.Ordinal);
        var processed = 0;
        var matchedInBatch = 0;
        foreach (var record in batch)
        {
            var duplicate = !seen.Add(record.ProviderReference);
            var payment = duplicate ? null : await sources.FindPaymentAsync(file.Provider, record.ProviderReference, record.ClientReference, ct).ConfigureAwait(false);
            if (!duplicate && payment is null && DateTimeOffset.UtcNow < file.ReceivedAtUtc.Add(policy.MissingReferenceGrace(file.Provider))) break;
            var ledger = payment is null ? null : await sources.FindLedgerAsync(payment, ct).ConfigureAwait(false);
            var evidence = new SettlementEvidence(record.ProviderReference, record.ClientReference, record.Amount, record.Currency, record.ProviderStatus, record.SettlementDate);
            var decision = duplicate
                ? new MatchDecision(MatchStatus.Exception, ExceptionCode.DUPLICATE_PROVIDER_RECORD, Severity.High, false, "Provider reference occurs more than once in settlement file.")
                : ReconciliationMatcher.Match(payment, ledger, null, evidence, policy);
            if (!duplicate && payment is null)
                decision = new MatchDecision(MatchStatus.Exception, ExceptionCode.UNKNOWN_PROVIDER_REFERENCE, Severity.High, false, "No internal payment matched after the event-lag grace period.");
            if (decision.RequestPaymentRecovery && payment is not null)
            {
                db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = run.Id, SettlementFileId = file.Id, Action = "AutoResolutionRequested", Actor = "reconciliation-worker", DownstreamCommand = "Payment.RecoverOne", Comment = $"PaymentId {payment.PaymentId:D}" });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                try
                {
                    var refreshed = await sources.RequestPaymentRecoveryAsync(payment.PaymentId, ct).ConfigureAwait(false);
                    if (refreshed is not null)
                    {
                        var refreshedLedger = await sources.FindLedgerAsync(refreshed, ct).ConfigureAwait(false);
                        var after = ReconciliationMatcher.Match(refreshed, refreshedLedger, null, evidence, policy);
                        decision = after.Status == MatchStatus.Matched ? after with { Status = MatchStatus.Resolved, Description = "Payment recovery completed and financial evidence agrees." } : after;
                        payment = refreshed;
                        ledger = refreshedLedger;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    break;
                }
            }
            db.Matches.Add(new ReconciliationMatch
            {
                RunId = run.Id, SettlementRecordId = record.Id, PaymentId = payment?.PaymentId,
                PaymentReference = payment?.PaymentReference, ProviderReference = record.ProviderReference,
                LedgerTransactionId = ledger?.TransactionId, Status = decision.Status, ExceptionCode = decision.Code
            });
            if (decision.Status is MatchStatus.Matched or MatchStatus.Resolved) { file.MatchedCount++; if (decision.Status == MatchStatus.Resolved) run.Resolved++; else run.Matched++; matchedInBatch++; }
            else if (decision.Status == MatchStatus.Exception)
            {
                await RecordExceptionAsync(file.Provider, payment, record.ProviderReference, decision, run.Id, ct).ConfigureAwait(false);
                file.ExceptionCount++; run.Exceptions++;
            }
            file.ProcessedCount++;
            file.LastProcessedLine = record.SourceLineNumber;
            run.RecordsProcessed++;
            processed++;
        }
        if (file.ProcessedCount >= file.RecordCount && DateTimeOffset.UtcNow >= new DateTimeOffset(file.SettlementDate.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).Add(policy.SettlementDelay(file.Provider)))
        {
            await ReverseMatchAsync(file, run, ct).ConfigureAwait(false);
            file.Status = file.ExceptionCount > 0 ? FileStatus.CompletedWithExceptions : FileStatus.Completed;
            file.ProcessedAtUtc = DateTimeOffset.UtcNow;
            run.Status = file.ExceptionCount > 0 ? RunStatus.CompletedWithExceptions : RunStatus.Completed;
            run.CompletedAtUtc = file.ProcessedAtUtc;
            db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = run.Id, SettlementFileId = file.Id, Action = "FileCompleted", Actor = "reconciliation-worker", Comment = $"Matched {file.MatchedCount}; exceptions {file.ExceptionCount}" });
            db.OutboxMessages.Add(Outbox("reconciliation.run.completed", run.Id, new { RunId = run.Id, file.Provider, Mode = run.Mode.ToString(), file.SettlementDate, run.RecordsProcessed, run.Matched, run.Exceptions, run.Resolved, run.CompletedAtUtc }));
        }
        file.LeaseOwner = null; file.LeaseUntilUtc = null;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        if (file.ProcessedAtUtc is { } fileCompletedAt)
        {
            Runs.Add(1, new KeyValuePair<string, object?>("provider", file.Provider), new KeyValuePair<string, object?>("mode", "SettlementFile"));
            RunDuration.Record((fileCompletedAt - run.StartedAtUtc).TotalSeconds, new KeyValuePair<string, object?>("provider", file.Provider));
            FileDuration.Record((fileCompletedAt - file.ReceivedAtUtc).TotalSeconds, new KeyValuePair<string, object?>("provider", file.Provider));
        }
        RecordsProcessed.Add(processed, new KeyValuePair<string, object?>("provider", file.Provider));
        Matches.Add(matchedInBatch, new KeyValuePair<string, object?>("provider", file.Provider));
        return processed > 0;
    }

    public async Task<MatchDecision> ReconcileStatusAsync(PaymentEvidence payment, CancellationToken ct)
    {
        using var activity = Tracing.StartActivity("reconciliation.run");
        var run = new ReconciliationRun { Provider = payment.Provider, Mode = ReconciliationMode.ProviderStatus };
        db.Runs.Add(run);
        db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = run.Id, Action = "RunStarted", Actor = "reconciliation-worker" });
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        ProviderEvidence? provider = null;
        MatchDecision decision;
        var recovered = false;
        try
        {
            provider = await sources.QueryProviderAsync(payment, ct).ConfigureAwait(false);
            if (provider is not null)
            {
                db.ProviderObservations.Add(new ProviderStatusObservation
                {
                    PaymentId = payment.PaymentId, Provider = payment.Provider, ProviderReference = provider.ProviderReference,
                    ObservedStatus = provider.Status, ResponseCode = provider.ResponseCode, ObservedAtUtc = provider.ObservedAtUtc
                });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            var ledger = await sources.FindLedgerAsync(payment, ct).ConfigureAwait(false);
            decision = ReconciliationMatcher.Match(payment, ledger, provider, null, policy);
            if (decision.RequestPaymentRecovery)
            {
                db.AuditEvents.Add(new ReconciliationAuditEvent
                {
                    RunId = run.Id, Action = "AutoResolutionRequested", Actor = "reconciliation-worker",
                    DownstreamCommand = "Payment.RecoverOne", Comment = $"PaymentId {payment.PaymentId:D}"
                });
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                var refreshed = await sources.RequestPaymentRecoveryAsync(payment.PaymentId, ct).ConfigureAwait(false);
                if (refreshed is not null)
                {
                    var newLedger = await sources.FindLedgerAsync(refreshed, ct).ConfigureAwait(false);
                    var after = ReconciliationMatcher.Match(refreshed, newLedger, provider, null, policy);
                    recovered = after.Status == MatchStatus.Matched;
                    decision = recovered ? after with { Status = MatchStatus.Resolved, Description = "Owning Payment Service recovered and financial evidence now agrees." } : after;
                    payment = refreshed;
                    ledger = newLedger;
                }
            }
            db.Matches.Add(new ReconciliationMatch
            {
                RunId = run.Id, PaymentId = payment.PaymentId, PaymentReference = payment.PaymentReference,
                ProviderReference = provider?.ProviderReference ?? payment.ProviderReference,
                LedgerTransactionId = ledger?.TransactionId, Status = decision.Status, ExceptionCode = decision.Code,
                ResolvedAtUtc = recovered ? DateTimeOffset.UtcNow : null,
                ResolutionType = recovered ? "PaymentRecovery" : null
            });
            if (decision.Status == MatchStatus.Exception)
            {
                await RecordExceptionAsync(payment.Provider, payment, provider?.ProviderReference, decision, run.Id, ct).ConfigureAwait(false);
                run.Exceptions++;
            }
            else if (decision.Status == MatchStatus.Matched) run.Matched++;
            else if (decision.Status == MatchStatus.Resolved) run.Resolved++;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            decision = new MatchDecision(MatchStatus.Pending, null, null, false, "Provider or owning service is unavailable; retry later.");
            db.Matches.Add(new ReconciliationMatch
            {
                RunId = run.Id, PaymentId = payment.PaymentId, PaymentReference = payment.PaymentReference,
                ProviderReference = payment.ProviderReference, Status = MatchStatus.Pending
            });
            run.Failed++;
        }

        run.RecordsProcessed = 1;
        run.Status = run.Exceptions > 0 || run.Failed > 0 ? RunStatus.CompletedWithExceptions : RunStatus.Completed;
        run.CompletedAtUtc = DateTimeOffset.UtcNow;
        db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = run.Id, Action = "RunCompleted", Actor = "reconciliation-worker", ReasonCode = decision.Code?.ToString() });
        db.OutboxMessages.Add(Outbox("reconciliation.run.completed", run.Id, new { RunId = run.Id, payment.Provider, Mode = run.Mode.ToString(), run.RecordsProcessed, run.Matched, run.Exceptions, run.Resolved, run.Failed, run.CompletedAtUtc }));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        Runs.Add(1, new KeyValuePair<string, object?>("provider", payment.Provider), new KeyValuePair<string, object?>("mode", "ProviderStatus"));
        RunDuration.Record((run.CompletedAtUtc!.Value - run.StartedAtUtc).TotalSeconds, new KeyValuePair<string, object?>("provider", payment.Provider));
        if (recovered) AutoResolved.Add(1, new KeyValuePair<string, object?>("provider", payment.Provider));        return decision;
    }
    private async Task ReverseMatchAsync(SettlementFile file, ReconciliationRun run, CancellationToken ct)
    {
        var expected = await sources.ExpectedSettlementsAsync(file.Provider, file.SettlementDate, ct).ConfigureAwait(false);
        foreach (var page in expected.Where(x => !string.IsNullOrWhiteSpace(x.ProviderReference)).Chunk(500))
        {
            var references = page.Select(x => x.ProviderReference!).ToArray();
            var present = await db.SettlementRecords.AsNoTracking()
                .Where(x => x.SettlementFileId == file.Id && references.Contains(x.ProviderReference))
                .Select(x => x.ProviderReference).ToListAsync(ct).ConfigureAwait(false);
            var seen = new HashSet<string>(present, StringComparer.Ordinal);
            foreach (var payment in page)
            {
                if (seen.Contains(payment.ProviderReference!)) continue;
                var decision = new MatchDecision(MatchStatus.Exception, ExceptionCode.SETTLEMENT_RECORD_MISSING, Severity.Medium, false, "Provider success is absent from settlement file after delay window.");
                await RecordExceptionAsync(file.Provider, payment, payment.ProviderReference, decision, run.Id, ct).ConfigureAwait(false);
                file.ExceptionCount++;
                run.Exceptions++;
            }
        }
    }
    private static ReconciliationOutboxMessage Outbox(string eventType, Guid partitionId, object payload)
        => new()
        {
            EventType = eventType,
            PartitionKey = partitionId.ToString("D"),
            Payload = JsonSerializer.Serialize(payload),
            OccurredAtUtc = DateTimeOffset.UtcNow,
            NextAttemptAtUtc = DateTimeOffset.UtcNow
        };
    private async Task RecordExceptionAsync(string provider, PaymentEvidence? payment, string? providerReference, MatchDecision decision, Guid? runId, CancellationToken ct)
    {
        if (decision.Code is null || decision.Severity is null) return;
        var paymentId = payment?.PaymentId;
        if (db.Exceptions.Local.Any(x => x.Provider == provider && x.PaymentId == paymentId && x.ProviderReference == providerReference && x.Code == decision.Code && (x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating))) return;
        var active = await db.Exceptions.SingleOrDefaultAsync(x => x.Provider == provider && x.PaymentId == paymentId && x.ProviderReference == providerReference && x.Code == decision.Code && (x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating), ct).ConfigureAwait(false);
        if (active is not null) return;
        var item = new ReconciliationException { Provider = provider, PaymentId = payment?.PaymentId, PaymentReference = payment?.PaymentReference, ProviderReference = providerReference, Code = decision.Code.Value, Severity = decision.Severity.Value, Description = decision.Description, EvidenceJson = JsonSerializer.Serialize(new { payment?.Status, payment?.Amount, payment?.Currency, providerReference }) };
        db.Exceptions.Add(item);
        db.AuditEvents.Add(new ReconciliationAuditEvent { RunId = runId, ExceptionId = item.Id, Action = "ExceptionCreated", Actor = "reconciliation-worker", ReasonCode = item.Code.ToString(), Comment = decision.Description });
        db.OutboxMessages.Add(Outbox("reconciliation.exception.created", item.Id, new { ReconciliationExceptionId = item.Id, item.Provider, item.PaymentId, ExceptionCode = item.Code.ToString(), Severity = item.Severity.ToString(), item.CreatedAtUtc }));
        Exceptions.Add(1, new KeyValuePair<string, object?>("provider", provider), new KeyValuePair<string, object?>("exception_code", item.Code.ToString()), new KeyValuePair<string, object?>("severity", item.Severity.ToString()));
    }
}
