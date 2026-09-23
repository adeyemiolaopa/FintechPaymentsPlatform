using Microsoft.EntityFrameworkCore;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.Api;

public sealed record ExceptionAction(string ReasonCode, string Comment, string? Assignee = null);

public static class ReconciliationEndpoints
{
    private static readonly System.Diagnostics.Metrics.Meter Meter = new("Payments.Reconciliation");
    private static readonly System.Diagnostics.Metrics.Counter<long> ManualResolved = Meter.CreateCounter<long>("reconciliation_manual_resolved_total");
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reconciliation").RequireAuthorization();
        group.MapPost("/settlement-files", async (string provider, DateOnly settlementDate, string fileName, HttpContext http, ReconciliationProcessor processor, CancellationToken ct) =>
        {
            if (!string.Equals(http.Request.ContentType?.Split(';')[0], "text/csv", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(415);
            try
            {
                var actor = http.User.Identity?.Name ?? http.User.FindFirst("sub")?.Value ?? "unknown";
                var file = await processor.ImportAsync(provider, fileName, settlementDate, actor, http.Request.Body, ct).ConfigureAwait(false);
                return Results.Ok(file);
            }
            catch (InvalidDataException exception) { return Results.BadRequest(new { error = exception.Message }); }
        }).RequireAuthorization("reconciliation.file.upload");
        group.MapGet("/settlement-files/{id:guid}", async (Guid id, ReconciliationDbContext db, CancellationToken ct) =>
            await db.SettlementFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) is { } file ? Results.Ok(file) : Results.NotFound())
            .RequireAuthorization("reconciliation.read");
        group.MapGet("/settlement-files/{id:guid}/summary", async (Guid id, ReconciliationDbContext db, CancellationToken ct) =>
        {
            var file = await db.SettlementFiles.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (file is null) return Results.NotFound();
            var providerTotals = await db.SettlementRecords.AsNoTracking().Where(x => x.SettlementFileId == id)
                .GroupBy(x => new { x.Currency, x.ProviderStatus })
                .Select(group => new { group.Key.Currency, group.Key.ProviderStatus, Count = group.LongCount(), Amount = group.Sum(x => x.Amount) })
                .ToListAsync(ct).ConfigureAwait(false);
            var matchedTotals = await db.SettlementRecords.AsNoTracking().Where(x => x.SettlementFileId == id)
                .Join(db.Matches.AsNoTracking().Where(x => x.Status == MatchStatus.Matched || x.Status == MatchStatus.Resolved), row => (Guid?)row.Id, match => match.SettlementRecordId, (row, match) => row)
                .GroupBy(x => x.Currency)
                .Select(group => new { Currency = group.Key, Count = group.LongCount(), Amount = group.Sum(x => x.Amount) })
                .ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { file.Provider, file.SettlementDate, file.RecordCount, file.ProcessedCount, file.MatchedCount, file.ExceptionCount, providerTotals, matchedTotals });
        }).RequireAuthorization("reconciliation.read"); group.MapPost("/settlement-files/{id:guid}/process", async (Guid id, ReconciliationProcessor processor, CancellationToken ct) =>
            Results.Ok(new { processed = await processor.ProcessNextBatchAsync(id, ct) })).RequireAuthorization("reconciliation.run");
        group.MapPost("/status/{paymentId:guid}", async (Guid paymentId, IReconciliationSources sources, ReconciliationProcessor processor, CancellationToken ct) =>
        {
            var payment = await sources.GetPaymentAsync(paymentId, ct).ConfigureAwait(false);
            return payment is null ? Results.NotFound() : Results.Ok(await processor.ReconcileStatusAsync(payment, ct).ConfigureAwait(false));
        }).RequireAuthorization("reconciliation.run");
        group.MapGet("/exceptions", async (string? provider, ExceptionCode? code, Severity? severity, ExceptionStatus? status, DateTimeOffset? fromUtc, string? paymentReference, string? providerReference, int page, int pageSize, ReconciliationDbContext db, CancellationToken ct) =>
        {
            var query = db.Exceptions.AsNoTracking().AsQueryable();
            if (provider is not null) query = query.Where(x => x.Provider == provider);
            if (code is not null) query = query.Where(x => x.Code == code);
            if (severity is not null) query = query.Where(x => x.Severity == severity);
            if (status is not null) query = query.Where(x => x.Status == status);
            if (fromUtc is not null) query = query.Where(x => x.CreatedAtUtc >= fromUtc);
            if (paymentReference is not null) query = query.Where(x => x.PaymentReference == paymentReference);
            if (providerReference is not null) query = query.Where(x => x.ProviderReference == providerReference);
            var total = await query.CountAsync(ct).ConfigureAwait(false);
            var items = await query.OrderByDescending(x => x.CreatedAtUtc).Skip((Math.Max(1, page) - 1) * Math.Clamp(pageSize, 1, 100)).Take(Math.Clamp(pageSize, 1, 100)).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { items, total, page = Math.Max(1, page), pageSize = Math.Clamp(pageSize, 1, 100) });
        }).RequireAuthorization("reconciliation.read");
        group.MapGet("/exceptions/{id:guid}", async (Guid id, HttpContext http, ReconciliationDbContext db, IReconciliationSources sources, CancellationToken ct) =>
        {
            var item = await db.Exceptions.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (item is null) return Results.NotFound();
            db.AuditEvents.Add(new ReconciliationAuditEvent { ExceptionId = id, Action = "ExceptionViewed", Actor = Actor(http) });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var timeline = await db.AuditEvents.AsNoTracking().Where(x => x.ExceptionId == id).OrderBy(x => x.OccurredAtUtc).ToListAsync(ct).ConfigureAwait(false);
            PaymentEvidence? payment = null;
            LedgerEvidence? ledger = null;
            try
            {
                if (item.PaymentId is { } paymentId) payment = await sources.GetPaymentAsync(paymentId, ct).ConfigureAwait(false);
                if (payment is not null) ledger = await sources.FindLedgerAsync(payment, ct).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException) { }
            var observations = item.PaymentId is { } observedPaymentId
                ? await db.ProviderObservations.AsNoTracking().Where(x => x.PaymentId == observedPaymentId).OrderByDescending(x => x.ObservedAtUtc).Take(20).ToListAsync(ct).ConfigureAwait(false)
                : [];
            var settlement = await db.SettlementRecords.AsNoTracking()
                .Where(x => x.ProviderReference == item.ProviderReference)
                .OrderByDescending(x => x.CreatedAtUtc).Take(20).ToListAsync(ct).ConfigureAwait(false);
            return Results.Ok(new { exception = item, payment, ledger, providerObservations = observations, settlementRecords = settlement, timeline });
        }).RequireAuthorization("reconciliation.read");
        group.MapPost("/exceptions/{id:guid}/assign", async (Guid id, ExceptionAction action, HttpContext http, ReconciliationDbContext db, CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(action.Assignee) || !ValidReason(action)) return Results.BadRequest();
            var item = await db.Exceptions.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (item is null) return Results.NotFound();
            item.AssignedTo = action.Assignee; item.AssignedAtUtc = DateTimeOffset.UtcNow; item.Status = ExceptionStatus.Investigating; item.UpdatedAtUtc = item.AssignedAtUtc.Value;
            db.AuditEvents.Add(Audit(id, "ExceptionAssigned", action, http));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(item);
        }).RequireAuthorization("reconciliation.exception.assign");
        group.MapPost("/exceptions/{id:guid}/resolve", async (Guid id, ExceptionAction action, HttpContext http, ReconciliationExceptionWorkflow workflow, CancellationToken ct) =>
        {
            if (!ValidReason(action)) return Results.BadRequest();
            ReconciliationException? item;
            try { item = await workflow.ResolveAsync(id, Actor(http), action.ReasonCode, action.Comment, ct).ConfigureAwait(false); }
            catch (InvalidOperationException) { return Results.Conflict(); }
            if (item is null) return Results.NotFound();
            ManualResolved.Add(1, new KeyValuePair<string, object?>("provider", item.Provider));
            return Results.Ok(item);
        }).RequireAuthorization("reconciliation.exception.resolve");
        group.MapPost("/exceptions/{id:guid}/ignore", async (Guid id, ExceptionAction action, HttpContext http, ReconciliationDbContext db, CancellationToken ct) =>
        {
            if (!ValidReason(action)) return Results.BadRequest();
            var item = await db.Exceptions.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (item is null) return Results.NotFound();
            if (item.Status is ExceptionStatus.Resolved or ExceptionStatus.Ignored) return Results.Conflict();
            item.Status = ExceptionStatus.Ignored; item.UpdatedAtUtc = DateTimeOffset.UtcNow;
            db.AuditEvents.Add(Audit(id, "ExceptionIgnored", action, http));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Results.Ok(item);
        }).RequireAuthorization("reconciliation.exception.resolve");
        group.MapPost("/exceptions/{id:guid}/requery", async (Guid id, ExceptionAction action, HttpContext http, ReconciliationDbContext db, IReconciliationSources sources, ReconciliationProcessor processor, CancellationToken ct) =>
        {
            if (!ValidReason(action)) return Results.BadRequest();
            var item = await db.Exceptions.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (item is null) return Results.NotFound();
            if (item.PaymentId is not { } paymentId) return Results.Conflict(new { error = "No internal Payment reference is available." });
            db.AuditEvents.Add(new ReconciliationAuditEvent { ExceptionId = id, Action = "ManualActionRequested", Actor = Actor(http), ReasonCode = action.ReasonCode, Comment = action.Comment, DownstreamCommand = "ProviderStatus.Query" });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var payment = await sources.GetPaymentAsync(paymentId, ct).ConfigureAwait(false);
            return payment is null ? Results.NotFound() : Results.Ok(await processor.ReconcileStatusAsync(payment, ct).ConfigureAwait(false));
        }).RequireAuthorization("reconciliation.exception.resolve");
        group.MapPost("/exceptions/{id:guid}/verify-ledger", async (Guid id, ExceptionAction action, HttpContext http, ReconciliationDbContext db, IReconciliationSources sources, CancellationToken ct) =>
        {
            if (!ValidReason(action)) return Results.BadRequest();
            var item = await db.Exceptions.SingleOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
            if (item is null) return Results.NotFound();
            if (item.PaymentId is not { } paymentId) return Results.Conflict(new { error = "No internal Payment reference is available." });
            db.AuditEvents.Add(new ReconciliationAuditEvent { ExceptionId = id, Action = "ManualActionRequested", Actor = Actor(http), ReasonCode = action.ReasonCode, Comment = action.Comment, DownstreamCommand = "Ledger.Verify" });
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var payment = await sources.GetPaymentAsync(paymentId, ct).ConfigureAwait(false);
            return payment is null ? Results.NotFound() : Results.Ok(await sources.FindLedgerAsync(payment, ct).ConfigureAwait(false));
        }).RequireAuthorization("reconciliation.exception.resolve"); group.MapGet("/dashboard", async (ReconciliationDbContext db, CancellationToken ct) =>
        {
            var today = DateTimeOffset.UtcNow.Date;
            var open = await db.Exceptions.CountAsync(x => x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating, ct).ConfigureAwait(false);
            var critical = await db.Exceptions.CountAsync(x => (x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating) && x.Severity == Severity.Critical, ct).ConfigureAwait(false);
            var runs = await db.Runs.CountAsync(x => x.StartedAtUtc >= new DateTimeOffset(today, TimeSpan.Zero), ct).ConfigureAwait(false);
            var processed = await db.Runs.SumAsync(x => x.RecordsProcessed, ct).ConfigureAwait(false);
            var matched = await db.Runs.SumAsync(x => x.Matched, ct).ConfigureAwait(false);
            var pendingPayments = await db.ActiveJobs.CountAsync(ct).ConfigureAwait(false);
            var oldest = await db.Exceptions.Where(x => x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating).MinAsync(x => (DateTimeOffset?)x.CreatedAtUtc, ct).ConfigureAwait(false);
            return Results.Ok(new { openExceptions = open, criticalExceptions = critical, runsToday = runs, matchRate = processed == 0 ? 0m : decimal.Divide(matched, processed), processed, matched, pendingReconciliationPayments = pendingPayments, oldestOpenExceptionAtUtc = oldest });
        }).RequireAuthorization("reconciliation.read");
        return app;
    }

    private static bool ValidReason(ExceptionAction action) => !string.IsNullOrWhiteSpace(action.ReasonCode) && action.ReasonCode.Length <= 80 && !string.IsNullOrWhiteSpace(action.Comment) && action.Comment.Length <= 1000;
    private static string Actor(HttpContext http) => http.User.Identity?.Name ?? http.User.FindFirst("sub")?.Value ?? "unknown";
    private static ReconciliationAuditEvent Audit(Guid id, string actionName, ExceptionAction action, HttpContext http) => new() { ExceptionId = id, Action = actionName, Actor = Actor(http), ReasonCode = action.ReasonCode, Comment = action.Comment };
}
