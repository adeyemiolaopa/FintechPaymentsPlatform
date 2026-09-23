using Microsoft.EntityFrameworkCore;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.Api;

public sealed class SettlementProcessingWorker(IServiceScopeFactory scopes, Microsoft.Extensions.Options.IOptions<ReconciliationOptions> options, ILogger<SettlementProcessingWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
                var fileIds = await db.SettlementFiles.AsNoTracking().Where(x => x.Status == FileStatus.Processing && (x.LeaseUntilUtc == null || x.LeaseUntilUtc <= DateTimeOffset.UtcNow)).OrderBy(x => x.ReceivedAtUtc).Select(x => x.Id).Take(10).ToListAsync(stoppingToken).ConfigureAwait(false);
                foreach (var id in fileIds)
                {
                    await using var fileScope = scopes.CreateAsyncScope();
                    await fileScope.ServiceProvider.GetRequiredService<ReconciliationProcessor>().ProcessNextBatchAsync(id, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested) { logger.LogError(exception, "Settlement worker iteration failed"); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.SettlementPollSeconds, 1, 3600)), stoppingToken).ConfigureAwait(false);
        }
    }
}
