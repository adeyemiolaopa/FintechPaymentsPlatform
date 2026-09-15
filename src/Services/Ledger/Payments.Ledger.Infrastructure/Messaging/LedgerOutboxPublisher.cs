using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Ledger.Infrastructure.Persistence;

namespace Payments.Ledger.Infrastructure.Messaging;

public sealed class LedgerOutboxOptions
{
    public const string SectionName = "Outbox";
    public bool PublisherEnabled { get; init; } = true;
    public int BatchSize { get; init; } = 20;
    public int PollSeconds { get; init; } = 5;
}

public sealed class LedgerOutboxPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LedgerOutboxOptions _options;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<LedgerOutboxPublisher> _logger;

    public LedgerOutboxPublisher(IServiceScopeFactory scopeFactory, IOptions<LedgerOutboxOptions> options, IOptions<KafkaOptions> kafkaOptions, ILogger<LedgerOutboxPublisher> logger)
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
            return;
        }

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            EnableIdempotence = true,
            Acks = Acks.All,
        }).Build();

        while (!stoppingToken.IsCancellationRequested)
        {
            await PublishBatchAsync(producer, stoppingToken).ConfigureAwait(false);
            await Task.Delay(TimeSpan.FromSeconds(_options.PollSeconds), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishBatchAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var pending = await dbContext.OutboxMessages.FromSqlRaw("SELECT * FROM ledger.outbox_messages WHERE \"PublishedAtUtc\" IS NULL ORDER BY \"OccurredAtUtc\" FOR UPDATE SKIP LOCKED").Take(_options.BatchSize).ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var message in pending)
        {
            try
            {
                await producer.ProduceAsync(message.Topic, new Message<string, string> { Key = message.Key, Value = message.Payload }, cancellationToken).ConfigureAwait(false);
                message.MarkPublished(clock.UtcNow);
                _logger.LogInformation("Published ledger outbox message {OutboxMessageId} of type {EventType}", message.Id, message.EventType);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                message.MarkFailed(exception.Message);
                _logger.LogWarning(exception, "Failed to publish ledger outbox message {OutboxMessageId}", message.Id);
            }
        }

        if (pending.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
