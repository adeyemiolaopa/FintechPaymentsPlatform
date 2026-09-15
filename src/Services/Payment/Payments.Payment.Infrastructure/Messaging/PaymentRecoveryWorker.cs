using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.Payment.Application.Payments;

namespace Payments.Payment.Infrastructure.Messaging;

public sealed class PaymentRecoveryOptions
{
    public const string SectionName = "PaymentRecovery";
    public bool Enabled { get; init; } = true;
    public int BatchSize { get; init; } = 25;
    public int PollSeconds { get; init; } = 15;
    public int MinimumAgeSeconds { get; init; } = 30;
}

public sealed class PaymentRecoveryWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PaymentRecoveryOptions _options;
    private readonly ILogger<PaymentRecoveryWorker> _logger;

    public PaymentRecoveryWorker(IServiceScopeFactory scopeFactory, IOptions<PaymentRecoveryOptions> options, ILogger<PaymentRecoveryWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Payment recovery worker is disabled.");
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPaymentService>();
                var count = await service.RecoverAsync(_options.BatchSize, TimeSpan.FromSeconds(Math.Clamp(_options.MinimumAgeSeconds, 1, 3600)), stoppingToken).ConfigureAwait(false);
                if (count > 0) _logger.LogInformation("Recovered {PaymentCount} payments.", count);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Payment recovery cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 300)), stoppingToken).ConfigureAwait(false);
        }
    }
}
