using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Reconciliation.Application;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.Api;

public sealed class ActiveReconciliationWorker(
    IServiceScopeFactory scopes,
    IOptions<ReconciliationOptions> options,
    ILogger<ActiveReconciliationWorker> logger) : BackgroundService
{
    private int _scanOffset;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SeedPendingAsync(stoppingToken).ConfigureAwait(false);
                for (var i = 0; i < Math.Clamp(options.Value.ActiveBatchSize, 1, 500); i++)
                    if (!await ProcessDueAsync(stoppingToken).ConfigureAwait(false)) break;
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(exception, "Active reconciliation iteration failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.ActivePollSeconds, 5, 3600)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task SeedPendingAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var sources = scope.ServiceProvider.GetRequiredService<IReconciliationSources>();
        var db = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
        var pageSize = Math.Clamp(options.Value.ActiveBatchSize, 1, 500);
        var page = await sources.PendingPaymentsAsync(_scanOffset, pageSize, ct).ConfigureAwait(false);
        _scanOffset = page.Count < pageSize ? 0 : _scanOffset + page.Count;
        foreach (var payment in page)
            await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO reconciliation.active_reconciliation_jobs (\"PaymentId\", \"Provider\", \"NextReconciliationAtUtc\", \"AttemptCount\") VALUES ({payment.PaymentId}, {payment.Provider}, {DateTimeOffset.UtcNow}, {0}) ON CONFLICT (\"PaymentId\") DO NOTHING", ct).ConfigureAwait(false);
    }

    private async Task<bool> ProcessDueAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
        var sources = scope.ServiceProvider.GetRequiredService<IReconciliationSources>();
        var processor = scope.ServiceProvider.GetRequiredService<ReconciliationProcessor>();
        var now = DateTimeOffset.UtcNow;
        var strategy = db.Database.CreateExecutionStrategy();
        var job = await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var candidate = await db.ActiveJobs.FromSqlInterpolated($"SELECT * FROM reconciliation.active_reconciliation_jobs WHERE \"NextReconciliationAtUtc\" <= {now} AND (\"LeaseUntilUtc\" IS NULL OR \"LeaseUntilUtc\" <= {now}) ORDER BY \"NextReconciliationAtUtc\" LIMIT 1 FOR UPDATE SKIP LOCKED").SingleOrDefaultAsync(ct).ConfigureAwait(false);
            if (candidate is null) return null;
            candidate.LeaseUntilUtc = now.AddMinutes(2);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
            return candidate;
        }).ConfigureAwait(false);
        if (job is null) return false;

        var payment = await sources.GetPaymentAsync(job.PaymentId, ct).ConfigureAwait(false);
        if (payment is null || payment.Status is not ("Processing" or "SubmittedToRail" or "PendingReconciliation"))
        {
            db.ActiveJobs.Remove(job);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }

        await processor.ReconcileStatusAsync(payment, ct).ConfigureAwait(false);
        var refreshed = await sources.GetPaymentAsync(job.PaymentId, ct).ConfigureAwait(false);
        if (refreshed is null || refreshed.Status is not ("Processing" or "SubmittedToRail" or "PendingReconciliation"))
            db.ActiveJobs.Remove(job);
        else
        {
            job.AttemptCount++;
            job.LastCheckedAtUtc = DateTimeOffset.UtcNow;
            job.NextReconciliationAtUtc = job.LastCheckedAtUtc.Value.AddMinutes(Math.Min(60, Math.Pow(2, Math.Min(job.AttemptCount, 6))));
            job.LeaseUntilUtc = null;
        }
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }
}
