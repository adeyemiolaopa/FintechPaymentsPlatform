using System.Globalization;
using System.Text;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Reconciliation.Domain;

namespace Payments.Reconciliation.Infrastructure;

public sealed class ReconciliationOutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int BatchSize { get; set; } = 100;
    public int PollSeconds { get; set; } = 1;
    public int MaxAttempts { get; set; } = 10;
}

public sealed class ReconciliationOutboxPublisher(
    IServiceScopeFactory scopes,
    IOptions<ReconciliationOutboxOptions> options,
    IOptions<KafkaOptions> kafkaOptions,
    ILogger<ReconciliationOutboxPublisher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        using var producer = new ProducerBuilder<string, string>(KafkaProducerConfigFactory.Create(kafkaOptions.Value)).Build();
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await PublishAsync(producer, stoppingToken).ConfigureAwait(false); }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Reconciliation outbox publish cycle failed");
            }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(options.Value.PollSeconds, 1, 60)), stoppingToken).ConfigureAwait(false);
        }
    }

    private async Task PublishAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ReconciliationDbContext>();
        var strategy = db.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            var now = DateTimeOffset.UtcNow;
            await using var transaction = await db.Database.BeginTransactionAsync(ct).ConfigureAwait(false);
            var batchSize = Math.Clamp(options.Value.BatchSize, 1, 500);
            var messages = await db.OutboxMessages.FromSqlInterpolated($"SELECT * FROM reconciliation.outbox_messages WHERE \"Status\" = 'Pending' AND (\"NextAttemptAtUtc\" IS NULL OR \"NextAttemptAtUtc\" <= {now}) ORDER BY \"OccurredAtUtc\" LIMIT {batchSize} FOR UPDATE SKIP LOCKED").ToListAsync(ct).ConfigureAwait(false);
            foreach (var message in messages)
            {
                try
                {
                    var headers = new Headers
                    {
                        { "event-id", Encoding.UTF8.GetBytes(message.EventId.ToString("D")) },
                        { "event-type", Encoding.UTF8.GetBytes(message.EventType) },
                        { "event-version", Encoding.UTF8.GetBytes(message.EventVersion.ToString(CultureInfo.InvariantCulture)) },
                        { "content-type", Encoding.UTF8.GetBytes("application/json") }
                    };
                    await producer.ProduceAsync(message.Topic, new Message<string, string> { Key = message.PartitionKey, Value = message.Payload, Headers = headers }, ct).ConfigureAwait(false);
                    message.Status = ReconciliationOutboxStatus.Published;
                    message.PublishedAtUtc = DateTimeOffset.UtcNow;
                    message.NextAttemptAtUtc = null;
                    message.LastError = null;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    message.AttemptCount++;
                    message.LastError = exception.Message.Length > 1024 ? exception.Message[..1024] : exception.Message;
                    if (message.AttemptCount >= Math.Clamp(options.Value.MaxAttempts, 1, 100))
                    {
                        message.Status = ReconciliationOutboxStatus.Failed;
                        message.NextAttemptAtUtc = null;
                    }
                    else message.NextAttemptAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Min(300, Math.Pow(2, Math.Min(message.AttemptCount, 8))));
                }
            }
            if (messages.Count > 0) await db.SaveChangesAsync(ct).ConfigureAwait(false);
            await transaction.CommitAsync(ct).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }
}