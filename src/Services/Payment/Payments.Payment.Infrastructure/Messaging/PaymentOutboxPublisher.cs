using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Payment.Infrastructure.Persistence;

namespace Payments.Payment.Infrastructure.Messaging;

public sealed class PaymentOutboxOptions
{
    public const string SectionName = "Outbox";
    public bool PublisherEnabled { get; init; } = true;
    public int BatchSize { get; init; } = 50;
    public int PollSeconds { get; init; } = 5;
}

public sealed class PaymentOutboxPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PaymentOutboxOptions _options;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<PaymentOutboxPublisher> _logger;

    public PaymentOutboxPublisher(IServiceScopeFactory scopeFactory, IOptions<PaymentOutboxOptions> options, IOptions<KafkaOptions> kafkaOptions, ILogger<PaymentOutboxPublisher> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _kafkaOptions = kafkaOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.PublisherEnabled)
        {
            _logger.LogInformation("Payment outbox publisher is disabled.");
            return;
        }

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = _kafkaOptions.BootstrapServers, EnableIdempotence = true, Acks = Acks.All }).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(producer, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception, "Payment outbox publish cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 60)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishBatchAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PaymentDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var pending = await dbContext.OutboxMessages.FromSqlRaw("SELECT * FROM payment.outbox_messages WHERE \"PublishedAtUtc\" IS NULL ORDER BY \"OccurredAtUtc\" FOR UPDATE SKIP LOCKED").Take(Math.Clamp(_options.BatchSize, 1, 250)).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in pending)
        {
            try
            {
                await producer.ProduceAsync(message.Topic, new Message<string, string> { Key = message.Key, Value = message.Payload, Headers = new Headers { { "event-type", System.Text.Encoding.UTF8.GetBytes(message.EventType) } } }, cancellationToken).ConfigureAwait(false);
                message.MarkPublished(clock.UtcNow);
            }
            catch (Exception exception)
            {
                message.MarkFailed(exception.Message);
                _logger.LogWarning(exception, "Failed to publish payment outbox message {OutboxMessageId}", message.Id);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
