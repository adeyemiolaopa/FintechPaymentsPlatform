using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Account.Infrastructure.Persistence;

namespace Payments.Account.Infrastructure.Messaging;

public sealed class AccountOutboxOptions
{
    public const string SectionName = "Outbox";
    public bool PublisherEnabled { get; init; } = true;
    public int BatchSize { get; init; } = 100;
    public int PollSeconds { get; init; } = 1;
    public int MaxAttempts { get; init; } = 10;
    public int MaxBackoffSeconds { get; init; } = 300;
    public bool CleanupEnabled { get; init; } = true;
    public int PublishedRetentionDays { get; init; } = 14;
    public int CleanupBatchSize { get; init; } = 500;
}

public sealed class AccountOutboxPublisher : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AccountOutboxOptions _options;
    private readonly KafkaOptions _kafkaOptions;
    private readonly ILogger<AccountOutboxPublisher> _logger;

    public AccountOutboxPublisher(IServiceScopeFactory scopeFactory, IOptions<AccountOutboxOptions> options, IOptions<KafkaOptions> kafkaOptions, ILogger<AccountOutboxPublisher> logger)
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
            _logger.LogInformation("account outbox publisher is disabled.");
            return;
        }

        using var producer = new ProducerBuilder<string, string>(KafkaProducerConfigFactory.Create(_kafkaOptions)).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PublishBatchAsync(producer, stoppingToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "account outbox publish cycle failed.");
            }

            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.PollSeconds, 1, 60)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishBatchAsync(IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AccountDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var now = clock.UtcNow;
        var batchSize = Math.Clamp(_options.BatchSize, 1, 500);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var pending = await dbContext.OutboxMessages.FromSqlInterpolated($@"
SELECT * FROM account.outbox_messages
WHERE ""Status"" = 'Pending'
  AND (""NextAttemptAtUtc"" IS NULL OR ""NextAttemptAtUtc"" <= {now})
ORDER BY ""OccurredAtUtc""
LIMIT {batchSize}
FOR UPDATE SKIP LOCKED").ToListAsync(cancellationToken).ConfigureAwait(false);

        foreach (var message in pending)
        {
            try
            {
                var result = await producer.ProduceAsync(message.Topic, new Message<string, string> { Key = message.PartitionKey, Value = message.Payload, Headers = BuildHeaders(message) }, cancellationToken).ConfigureAwait(false);
                message.MarkPublished(clock.UtcNow);
                _logger.LogInformation("Published account outbox message {OutboxMessageId} event {EventId} type {EventType} to {TopicPartitionOffset}", message.Id, message.EventId, message.EventType, result.TopicPartitionOffset);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var retryDelay = CalculateBackoff(message.AttemptCount + 1);
                message.MarkFailed(exception.Message, clock.UtcNow, Math.Clamp(_options.MaxAttempts, 1, 100), retryDelay);
                _logger.LogWarning(exception, "Failed to publish account outbox message {OutboxMessageId} event {EventId} attempt {AttemptCount}", message.Id, message.EventId, message.AttemptCount);
            }
        }

        if (pending.Count > 0) await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await DeletePublishedMessagesAsync(dbContext, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task DeletePublishedMessagesAsync(AccountDbContext dbContext, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (!_options.CleanupEnabled || _options.PublishedRetentionDays <= 0) return;

        var cutoff = now.AddDays(-Math.Clamp(_options.PublishedRetentionDays, 1, 3650));
        var batchSize = Math.Clamp(_options.CleanupBatchSize, 1, 5000);
        var deleted = await dbContext.Database.ExecuteSqlInterpolatedAsync($@"
DELETE FROM account.outbox_messages
WHERE ""Id"" IN (
    SELECT ""Id""
    FROM account.outbox_messages
    WHERE ""Status"" = 'Published'
      AND ""PublishedAtUtc"" IS NOT NULL
      AND ""PublishedAtUtc"" < {cutoff}
    ORDER BY ""PublishedAtUtc""
    LIMIT {batchSize}
    FOR UPDATE SKIP LOCKED
)", cancellationToken).ConfigureAwait(false);

        if (deleted > 0) _logger.LogInformation("Deleted {DeletedCount} published account outbox messages older than {Cutoff}", deleted, cutoff);
    }
    private TimeSpan CalculateBackoff(int attempt)
    {
        var cappedPower = Math.Min(attempt, 8);
        var seconds = Math.Min(Math.Pow(2, cappedPower - 1), Math.Clamp(_options.MaxBackoffSeconds, 1, 3600));
        return TimeSpan.FromSeconds(seconds + Random.Shared.NextDouble());
    }

    private static Headers BuildHeaders(dynamic message)
        => new()
        {
            { "event-id", Encoding.UTF8.GetBytes(((Guid)message.EventId).ToString("D")) },
            { "event-type", Encoding.UTF8.GetBytes((string)message.EventType) },
            { "event-version", Encoding.UTF8.GetBytes(((int)message.EventVersion).ToString(CultureInfo.InvariantCulture)) },
            { "partition-key", Encoding.UTF8.GetBytes((string)message.PartitionKey) },
            { "content-type", Encoding.UTF8.GetBytes("application/json") },
        };
}
