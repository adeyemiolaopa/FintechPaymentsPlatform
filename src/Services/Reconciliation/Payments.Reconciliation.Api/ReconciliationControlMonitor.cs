using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Payments.Reconciliation.Domain;
using Payments.Reconciliation.Infrastructure;

namespace Payments.Reconciliation.Api;

public sealed class ReconciliationControlOptions
{
    public int PollSeconds { get; set; } = 60;
    public decimal MinimumMatchRate { get; set; } = 0.98m;
    public int OldestExceptionMinutes { get; set; } = 60;
    public int BacklogThreshold { get; set; } = 500;
    public List<SettlementDeliverySchedule> SettlementSchedules { get; set; } = [];
}

public sealed class SettlementDeliverySchedule
{
    public string Provider { get; set; } = "";
    public string TimeZoneId { get; set; } = "UTC";
    public int ExpectedHourLocal { get; set; } = 12;
    public bool ExpectOnWeekends { get; set; }
}

public sealed class ReconciliationControlMonitor : BackgroundService
{
    private static readonly Meter Meter = new("Payments.Reconciliation");
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<ReconciliationControlOptions> _options;
    private readonly ILogger<ReconciliationControlMonitor> _logger;
    private long _openExceptions;
    private long _oldestExceptionAgeSeconds;
    private long _activeBacklog;

    public ReconciliationControlMonitor(IServiceScopeFactory scopes, IOptions<ReconciliationControlOptions> options, ILogger<ReconciliationControlMonitor> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
        Meter.CreateObservableGauge("reconciliation_open_exceptions", () => _openExceptions);
        Meter.CreateObservableGauge("reconciliation_oldest_exception_age_seconds", () => _oldestExceptionAgeSeconds);
        Meter.CreateObservableGauge("reconciliation_status_backlog", () => _activeBacklog);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await EvaluateAsync(stoppingToken).ConfigureAwait(false); }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(exception, "Reconciliation control monitor failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.Value.PollSeconds, 10, 3600)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task EvaluateAsync(CancellationToken ct)
    {
        await using var scope = _scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var active = db.Exceptions.AsNoTracking().Where(x => x.Status == ExceptionStatus.Open || x.Status == ExceptionStatus.Investigating);
        _openExceptions = await active.LongCountAsync(ct).ConfigureAwait(false);
        _activeBacklog = await db.ActiveJobs.LongCountAsync(ct).ConfigureAwait(false);
        var oldest = await active.MinAsync(x => (DateTimeOffset?)x.CreatedAtUtc, ct).ConfigureAwait(false);
        _oldestExceptionAgeSeconds = oldest is null ? 0 : Math.Max(0, (long)(now - oldest.Value).TotalSeconds);
        var critical = await active.LongCountAsync(x => x.Severity == Severity.Critical, ct).ConfigureAwait(false);
        var missingLedger = await active.LongCountAsync(x => x.Code == ExceptionCode.PROVIDER_SUCCESS_LEDGER_MISSING, ct).ConfigureAwait(false);
        if (critical > 0) _logger.LogCritical("Reconciliation has {CriticalCount} open critical exceptions", critical);
        if (missingLedger > 0) _logger.LogCritical("Reconciliation has {MissingLedgerCount} provider-success/ledger-missing exceptions", missingLedger);
        if (_oldestExceptionAgeSeconds > _options.Value.OldestExceptionMinutes * 60L) _logger.LogWarning("Oldest reconciliation exception age is {AgeSeconds}s", _oldestExceptionAgeSeconds);
        if (_activeBacklog > _options.Value.BacklogThreshold) _logger.LogWarning("Reconciliation active-status backlog is {BacklogCount}", _activeBacklog);

        var since = now.AddDays(-1);
        var processed = await db.Runs.Where(x => x.StartedAtUtc >= since).SumAsync(x => x.RecordsProcessed, ct).ConfigureAwait(false);
        var matched = await db.Runs.Where(x => x.StartedAtUtc >= since).SumAsync(x => x.Matched + x.Resolved, ct).ConfigureAwait(false);
        if (processed > 0 && decimal.Divide(matched, processed) < _options.Value.MinimumMatchRate)
            _logger.LogWarning("Reconciliation 24h match rate {MatchRate} is below {Threshold}; processed {Processed}, matched/resolved {Matched}", decimal.Divide(matched, processed), _options.Value.MinimumMatchRate, processed, matched);

        foreach (var schedule in _options.Value.SettlementSchedules.Where(x => !string.IsNullOrWhiteSpace(x.Provider)))
        {
            var zone = TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            var localNow = TimeZoneInfo.ConvertTime(now, zone);
            if (!schedule.ExpectOnWeekends && localNow.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
            if (localNow.Hour < Math.Clamp(schedule.ExpectedHourLocal, 0, 23)) continue;
            var businessDate = DateOnly.FromDateTime(localNow.DateTime);
            var received = await db.SettlementFiles.AnyAsync(x => x.Provider == schedule.Provider && x.SettlementDate == businessDate && x.Status != FileStatus.Rejected, ct).ConfigureAwait(false);
            if (!received) _logger.LogCritical("Expected settlement file not received for provider {Provider}, business date {BusinessDate}, timezone {TimeZone}", schedule.Provider, businessDate, schedule.TimeZoneId);
        }
    }
}
