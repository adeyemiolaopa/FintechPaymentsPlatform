using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Payment.Infrastructure.Persistence;

namespace Payments.Payment.Infrastructure.Messaging;

public sealed class PaymentIdempotencyCleanupWorker : BackgroundService
{
    private static readonly Meter Meter = new("Payments.Payment");
    private static readonly Counter<long> IdempotencyCleanup = Meter.CreateCounter<long>("idempotency_cleanup_total");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PaymentIdempotencyOptions _options;
    private readonly ILogger<PaymentIdempotencyCleanupWorker> _logger;

    public PaymentIdempotencyCleanupWorker(IServiceScopeFactory scopeFactory, IOptions<PaymentIdempotencyOptions> options, ILogger<PaymentIdempotencyCleanupWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.CleanupEnabled)
        {
            _logger.LogInformation("Payment idempotency cleanup worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
                var batchSize = Math.Clamp(_options.CleanupBatchSize, 1, 1000);
                var now = DateTimeOffset.UtcNow;
                var cleaned = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
UPDATE payment.payment_idempotency_records
SET ""ResponseBody"" = NULL, ""UpdatedAtUtc"" = {now}
WHERE ""Id"" IN (
    SELECT ""Id""
    FROM payment.payment_idempotency_records
    WHERE ""Status"" = 'Completed'
      AND ""ResponseBody"" IS NOT NULL
      AND ""ExpiresAtUtc"" <= {now}
    ORDER BY ""ExpiresAtUtc""
    LIMIT {batchSize}
    FOR UPDATE SKIP LOCKED
)", stoppingToken).ConfigureAwait(false);

                if (cleaned > 0)
                {
                    IdempotencyCleanup.Add(cleaned, new KeyValuePair<string, object?>("operation", "CreatePayment"));
                    _logger.LogInformation("Pruned {RecordCount} payment idempotency response snapshots.", cleaned);
                }
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Payment idempotency cleanup cycle failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(Math.Clamp(_options.CleanupIntervalMinutes, 1, 1440)), stoppingToken).ConfigureAwait(false);
        }
    }
}
