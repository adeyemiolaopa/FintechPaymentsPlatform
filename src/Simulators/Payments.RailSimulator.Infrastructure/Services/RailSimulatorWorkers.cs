using System.Net.Mime;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Domain;
using Payments.RailSimulator.Infrastructure.Persistence;

namespace Payments.RailSimulator.Infrastructure.Services;

public sealed class RailDelayedProcessingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<RailSimulatorOptions> _options;
    private readonly ILogger<RailDelayedProcessingWorker> _logger;

    public RailDelayedProcessingWorker(IServiceScopeFactory scopeFactory, IOptions<RailSimulatorOptions> options, ILogger<RailDelayedProcessingWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = (RailSimulatorService)scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();
                await service.CompleteDueTransfersAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Rail delayed processing worker failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(_options.Value.WorkerIntervalMs, 100, 30_000)), stoppingToken).ConfigureAwait(false);
        }
    }
}

public sealed class RailCallbackWorker : BackgroundService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly IClock _clock;
    private readonly IOptions<RailSimulatorOptions> _options;
    private readonly ILogger<RailCallbackWorker> _logger;

    public RailCallbackWorker(IServiceScopeFactory scopeFactory, HttpClient httpClient, IClock clock, IOptions<RailSimulatorOptions> options, ILogger<RailCallbackWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _clock = clock;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DispatchDueCallbacksAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Rail callback worker failed.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(_options.Value.WorkerIntervalMs, 100, 30_000)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task DispatchDueCallbacksAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<RailSimulatorDbContext>();
        var authenticator = scope.ServiceProvider.GetRequiredService<IRailRequestAuthenticator>();
        var callbackConfiguration = await dbContext.CallbackConfigurations.AsNoTracking().FirstOrDefaultAsync(cancellationToken).ConfigureAwait(false);
        if (callbackConfiguration is null || string.IsNullOrWhiteSpace(callbackConfiguration.Url)) return;

        var now = _clock.UtcNow;
        var attempts = await dbContext.CallbackAttempts
            .Where(attempt => attempt.Status != CallbackAttemptStatus.Succeeded && attempt.NextAttemptAtUtc != null && attempt.NextAttemptAtUtc <= now)
            .OrderBy(attempt => attempt.NextAttemptAtUtc)
            .Take(20)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var attempt in attempts)
        {
            var transfer = await dbContext.RailTransfers.FirstOrDefaultAsync(item => item.Id == attempt.RailTransferId, cancellationToken).ConfigureAwait(false);
            if (transfer is null)
            {
                attempt.MarkFailed(null, "Rail transfer was not found for callback.", _clock.UtcNow, null);
                continue;
            }

            attempt.Start(_clock.UtcNow);
            var payload = JsonSerializer.Serialize(new
            {
                eventId = attempt.CallbackEventId,
                providerReference = transfer.ProviderReference,
                clientReference = transfer.ClientReference,
                status = transfer.Status.ToString(),
                amount = transfer.Amount,
                currency = transfer.Currency,
                responseCode = RailResponseCodes.ForStatus(transfer.Status).Code,
                occurredAtUtc = _clock.UtcNow,
            }, JsonOptions);

            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            using var message = new HttpRequestMessage(HttpMethod.Post, attempt.TargetUrl);
            message.Content = new StringContent(payload, Encoding.UTF8, MediaTypeNames.Application.Json);
            message.Headers.Add("X-Rail-Timestamp", timestamp);
            message.Headers.Add("X-Rail-Signature", authenticator.SignCallback(callbackConfiguration.WebhookSecret, timestamp, payload));

            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(_options.Value.CallbackTimeoutSeconds, 1, 30)));
                var response = await _httpClient.SendAsync(message, timeout.Token).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    attempt.MarkSucceeded((int)response.StatusCode, _clock.UtcNow);
                }
                else
                {
                    ScheduleRetry(dbContext, attempt, (int)response.StatusCode, $"Callback endpoint returned {(int)response.StatusCode}.", transfer);
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                ScheduleRetry(dbContext, attempt, null, exception.Message, transfer);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void ScheduleRetry(RailSimulatorDbContext dbContext, CallbackAttempt attempt, int? statusCode, string error, RailTransfer transfer)
    {
        var nextAttempt = attempt.AttemptNumber + 1;
        var scenario = dbContext.ProviderScenarios.AsNoTracking().FirstOrDefault();
        var maxAttempts = scenario?.MaxCallbackAttempts ?? 4;
        var retryDelay = TimeSpan.FromMilliseconds((scenario?.CallbackRetryBaseMs ?? 250) * nextAttempt);
        var retryAt = nextAttempt > maxAttempts ? (DateTimeOffset?)null : _clock.UtcNow.Add(retryDelay);
        attempt.MarkFailed(statusCode, error, _clock.UtcNow, retryAt);

        if (retryAt is not null && !dbContext.CallbackAttempts.Local.Any(item => item.RailTransferId == transfer.Id && item.AttemptNumber == nextAttempt))
        {
            dbContext.CallbackAttempts.Add(CallbackAttempt.Schedule(transfer.Id, transfer.CallbackEventId, nextAttempt, attempt.TargetUrl, retryAt.Value, _clock.UtcNow));
        }
    }
}
