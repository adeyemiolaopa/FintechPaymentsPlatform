using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Payments.BuildingBlocks.Messaging.Events;

public enum InboxMessageStatus { Processing = 0, Processed = 1, Failed = 2 }

public sealed class InboxMessage
{
    private InboxMessage() { }

    private InboxMessage(Guid eventId, string eventType, int eventVersion, string consumerName, string topic, int partition, long offset, DateTimeOffset receivedAtUtc, string payloadHash, string? correlationId, string processingInstanceId)
    {
        Id = Guid.NewGuid();
        EventId = eventId;
        EventType = eventType;
        EventVersion = eventVersion;
        ConsumerName = consumerName;
        Topic = topic;
        Partition = partition;
        Offset = offset;
        ReceivedAtUtc = receivedAtUtc;
        ProcessingStartedAtUtc = receivedAtUtc;
        LastUpdatedAtUtc = receivedAtUtc;
        Status = InboxMessageStatus.Processing;
        AttemptCount = 1;
        PayloadHash = payloadHash;
        CorrelationId = correlationId;
        ProcessingInstanceId = processingInstanceId;
    }

    public Guid Id { get; private set; }
    public Guid EventId { get; private set; }
    public string EventType { get; private set; } = string.Empty;
    public int EventVersion { get; private set; }
    public string ConsumerName { get; private set; } = string.Empty;
    public string Topic { get; private set; } = string.Empty;
    public int Partition { get; private set; }
    public long Offset { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    public DateTimeOffset? ProcessingStartedAtUtc { get; private set; }
    public DateTimeOffset? ProcessedAtUtc { get; private set; }
    public InboxMessageStatus Status { get; private set; }
    public int AttemptCount { get; private set; }
    public string? LastError { get; private set; }
    public string PayloadHash { get; private set; } = string.Empty;
    public string? CorrelationId { get; private set; }
    public string ProcessingInstanceId { get; private set; } = string.Empty;
    public DateTimeOffset LastUpdatedAtUtc { get; private set; }

    public static InboxMessage Start(Guid eventId, string eventType, int eventVersion, string consumerName, string topic, int partition, long offset, DateTimeOffset now, string payloadHash, string? correlationId, string processingInstanceId)
        => new(eventId, eventType, eventVersion, consumerName, topic, partition, offset, now, payloadHash, correlationId, processingInstanceId);

    public bool HasDifferentPayload(string payloadHash) => !string.Equals(PayloadHash, payloadHash, StringComparison.Ordinal);

    public bool IsStale(DateTimeOffset now, TimeSpan timeout)
        => Status == InboxMessageStatus.Processing && ProcessingStartedAtUtc.HasValue && now - ProcessingStartedAtUtc.Value >= timeout;

    public void RestartProcessing(string topic, int partition, long offset, DateTimeOffset now, string processingInstanceId)
    {
        Topic = topic;
        Partition = partition;
        Offset = offset;
        Status = InboxMessageStatus.Processing;
        AttemptCount++;
        ProcessingStartedAtUtc = now;
        LastUpdatedAtUtc = now;
        ProcessingInstanceId = processingInstanceId;
        LastError = null;
    }

    public void MarkProcessed(DateTimeOffset now)
    {
        Status = InboxMessageStatus.Processed;
        ProcessedAtUtc = now;
        LastUpdatedAtUtc = now;
        LastError = null;
    }

    public void MarkFailed(string error, DateTimeOffset now)
    {
        Status = InboxMessageStatus.Failed;
        LastError = error.Length > 1024 ? error[..1024] : error;
        LastUpdatedAtUtc = now;
    }
}

public sealed class InboxConsumerOptions
{
    public bool Enabled { get; init; } = true;
    public string ConsumerName { get; init; } = string.Empty;
    public string GroupId { get; init; } = string.Empty;
    public string Topic { get; init; } = string.Empty;
    public string[] RetryTopics { get; init; } = [];
    public string? DeadLetterTopic { get; init; }
    public int ImmediateRetryCount { get; init; } = 3;
    public int ProcessingTimeoutSeconds { get; init; } = 300;
    public int MaxPollIntervalMs { get; init; } = 300000;
    public int MaxPollRecords { get; init; } = 10;
    public int InboxRetentionDays { get; init; } = 90;
}

public sealed record IntegrationEventContext(
    Guid EventId,
    string EventType,
    int EventVersion,
    string ConsumerName,
    string Topic,
    int Partition,
    long Offset,
    string? CorrelationId,
    string? CausationId,
    DateTimeOffset OccurredAtUtc,
    string PayloadHash);

public interface IIntegrationEventHandler<TPayload>
    where TPayload : IIntegrationEvent
{
    Task HandleAsync(IntegrationEventEnvelope<TPayload> envelope, IntegrationEventContext context, CancellationToken cancellationToken = default);
}

public static class InboxModelBuilderExtensions
{
    public static EntityTypeBuilder<InboxMessage> ConfigureInboxMessages(this ModelBuilder modelBuilder)
    {
        var builder = modelBuilder.Entity<InboxMessage>();
        builder.ToTable("inbox_messages");
        builder.HasKey(message => message.Id);
        builder.Property(message => message.EventType).HasMaxLength(128).IsRequired();
        builder.Property(message => message.ConsumerName).HasMaxLength(160).IsRequired();
        builder.Property(message => message.Topic).HasMaxLength(160).IsRequired();
        builder.Property(message => message.Status).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(message => message.PayloadHash).HasMaxLength(128).IsRequired();
        builder.Property(message => message.CorrelationId).HasMaxLength(128);
        builder.Property(message => message.ProcessingInstanceId).HasMaxLength(128).IsRequired();
        builder.Property(message => message.LastError).HasMaxLength(1024);
        builder.HasIndex(message => new { message.ConsumerName, message.EventId }).IsUnique();
        builder.HasIndex(message => new { message.Status, message.ReceivedAtUtc });
        builder.HasIndex(message => message.ProcessedAtUtc);
        builder.HasIndex(message => message.EventType);
        return builder;
    }
}

public abstract class KafkaInboxConsumer<TPayload, TDbContext, THandler> : BackgroundService
    where TPayload : IIntegrationEvent
    where TDbContext : DbContext
    where THandler : class, IIntegrationEventHandler<TPayload>
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly InboxConsumerOptions _options;
    private readonly ILogger _logger;
    private readonly string _processingInstanceId = $"{Environment.MachineName}-{Guid.NewGuid():N}";

    protected KafkaInboxConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, InboxConsumerOptions options, ILogger logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _options = options;
        _logger = logger;
    }

    protected abstract string ExpectedEventType { get; }
    protected abstract int ExpectedEventVersion { get; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        if (string.IsNullOrWhiteSpace(_options.ConsumerName)) throw new InvalidOperationException("ConsumerName is required for inbox consumers.");
        if (string.IsNullOrWhiteSpace(_options.GroupId)) throw new InvalidOperationException("GroupId is required for inbox consumers.");
        if (string.IsNullOrWhiteSpace(_options.Topic)) throw new InvalidOperationException("Topic is required for inbox consumers.");

        using var producer = new ProducerBuilder<string, string>(KafkaProducerConfigFactory.Create(_kafkaOptions)).Build();
        using var consumer = new ConsumerBuilder<string, string>(CreateConsumerConfig())
            .SetPartitionsRevokedHandler((_, partitions) => _logger.LogInformation("Consumer {ConsumerName} partitions revoked: {Partitions}", _options.ConsumerName, string.Join(",", partitions)))
            .SetPartitionsAssignedHandler((_, partitions) => _logger.LogInformation("Consumer {ConsumerName} partitions assigned: {Partitions}", _options.ConsumerName, string.Join(",", partitions)))
            .Build();

        var topics = new[] { _options.Topic }.Concat(_options.RetryTopics ?? []).Distinct(StringComparer.Ordinal).ToArray();
        consumer.Subscribe(topics);
        _logger.LogInformation("Consumer {ConsumerName} subscribed to {Topics} in group {GroupId}", _options.ConsumerName, string.Join(",", topics), _options.GroupId);

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                ConsumeResult<string, string>? result = null;
                try
                {
                    result = consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result is null)
                    {
                        await Task.Yield();
                        continue;
                    }

                    await ProcessAsync(result, producer, stoppingToken).ConfigureAwait(false);
                    consumer.Commit(result);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    _logger.LogWarning(exception, "Consumer {ConsumerName} failed while processing topic {Topic} partition {Partition} offset {Offset}; offset was not committed", _options.ConsumerName, result?.Topic, result?.Partition.Value, result?.Offset.Value);
                }
            }
        }
        finally
        {
            consumer.Close();
        }
    }

    private ConsumerConfig CreateConsumerConfig()
        => new()
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            MaxPollIntervalMs = _options.MaxPollIntervalMs,
            MaxPollRecords = Math.Clamp(_options.MaxPollRecords, 1, 500),
        };

    private async Task ProcessAsync(ConsumeResult<string, string> result, IProducer<string, string> producer, CancellationToken cancellationToken)
    {
        var payloadHash = ComputeHash(result.Message.Value);
        IntegrationEventEnvelope<TPayload>? envelope;
        try
        {
            envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<TPayload>>(result.Message.Value, SerializerOptions);
        }
        catch (Exception exception)
        {
            await PublishDeadLetterAsync(producer, result, null, "Schema", exception.Message, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (envelope is null || !string.Equals(envelope.EventType, ExpectedEventType, StringComparison.Ordinal) || envelope.EventVersion != ExpectedEventVersion)
        {
            await PublishDeadLetterAsync(producer, result, envelope?.EventId, "Contract", "Unexpected event type or version.", cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var scope = _scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<THandler>();
        var now = DateTimeOffset.UtcNow;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(_options.ProcessingTimeoutSeconds, 30, 86400));
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        var inbox = await dbContext.Set<InboxMessage>().SingleOrDefaultAsync(message => message.ConsumerName == _options.ConsumerName && message.EventId == envelope.EventId, cancellationToken).ConfigureAwait(false);

        if (inbox is not null)
        {
            if (inbox.HasDifferentPayload(payloadHash))
            {
                inbox.MarkFailed("Same EventId was received with a different payload hash.", now);
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                await PublishDeadLetterAsync(producer, result, envelope.EventId, "Integrity", "Same EventId was received with a different payload hash.", cancellationToken).ConfigureAwait(false);
                return;
            }

            if (inbox.Status == InboxMessageStatus.Processed)
            {
                _logger.LogInformation("Consumer {ConsumerName} skipped duplicate event {EventId} from {TopicPartitionOffset}", _options.ConsumerName, envelope.EventId, result.TopicPartitionOffset);
                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                return;
            }

            if (inbox.Status == InboxMessageStatus.Processing && !inbox.IsStale(now, timeout))
            {
                throw new InvalidOperationException($"Event {envelope.EventId} is already being processed by {_options.ConsumerName}.");
            }

            inbox.RestartProcessing(result.Topic, result.Partition.Value, result.Offset.Value, now, _processingInstanceId);
        }
        else
        {
            inbox = InboxMessage.Start(envelope.EventId, envelope.EventType, envelope.EventVersion, _options.ConsumerName, result.Topic, result.Partition.Value, result.Offset.Value, now, payloadHash, envelope.CorrelationId, _processingInstanceId);
            dbContext.Set<InboxMessage>().Add(inbox);
        }

        var context = new IntegrationEventContext(envelope.EventId, envelope.EventType, envelope.EventVersion, _options.ConsumerName, result.Topic, result.Partition.Value, result.Offset.Value, envelope.CorrelationId, envelope.CausationId, envelope.OccurredAtUtc, payloadHash);
        try
        {
            await HandleWithImmediateRetryAsync(handler, envelope, context, cancellationToken).ConfigureAwait(false);
            inbox.MarkProcessed(DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            inbox.MarkFailed(exception.Message, DateTimeOffset.UtcNow);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            await PublishRetryOrDeadLetterAsync(producer, result, envelope.EventId, exception, cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task HandleWithImmediateRetryAsync(THandler handler, IntegrationEventEnvelope<TPayload> envelope, IntegrationEventContext context, CancellationToken cancellationToken)
    {
        var attempts = Math.Clamp(_options.ImmediateRetryCount, 1, 10);
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await handler.HandleAsync(envelope, context, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch when (attempt < attempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50 * attempt + Random.Shared.Next(0, 50)), cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PublishRetryOrDeadLetterAsync(IProducer<string, string> producer, ConsumeResult<string, string> result, Guid eventId, Exception exception, CancellationToken cancellationToken)
    {
        var retryTopic = (_options.RetryTopics ?? []).FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(retryTopic) && !string.Equals(result.Topic, retryTopic, StringComparison.Ordinal))
        {
            await producer.ProduceAsync(retryTopic, CreateForwardedMessage(result, eventId, "Transient", exception.Message), cancellationToken).ConfigureAwait(false);
            _logger.LogWarning(exception, "Consumer {ConsumerName} moved event {EventId} to retry topic {RetryTopic}", _options.ConsumerName, eventId, retryTopic);
            return;
        }

        await PublishDeadLetterAsync(producer, result, eventId, "Poison", exception.Message, cancellationToken).ConfigureAwait(false);
    }

    private async Task PublishDeadLetterAsync(IProducer<string, string> producer, ConsumeResult<string, string> result, Guid? eventId, string category, string reason, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_options.DeadLetterTopic)) return;
        await producer.ProduceAsync(_options.DeadLetterTopic, CreateForwardedMessage(result, eventId, category, reason), cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Consumer {ConsumerName} moved event {EventId} from {TopicPartitionOffset} to DLQ {DlqTopic}. Category: {Category}. Reason: {Reason}", _options.ConsumerName, eventId, result.TopicPartitionOffset, _options.DeadLetterTopic, category, reason);
    }

    private Message<string, string> CreateForwardedMessage(ConsumeResult<string, string> result, Guid? eventId, string category, string reason)
    {
        var headers = new Headers();
        if (result.Message.Headers is not null)
        {
            foreach (var header in result.Message.Headers)
            {
                headers.Add(header.Key, header.GetValueBytes());
            }
        }
        headers.Add("retry-count", Encoding.UTF8.GetBytes(ReadRetryCount(headers).ToString(CultureInfo.InvariantCulture)));
        headers.Add("original-topic", Encoding.UTF8.GetBytes(result.Topic));
        headers.Add("original-partition", Encoding.UTF8.GetBytes(result.Partition.Value.ToString(CultureInfo.InvariantCulture)));
        headers.Add("original-offset", Encoding.UTF8.GetBytes(result.Offset.Value.ToString(CultureInfo.InvariantCulture)));
        headers.Add("failure-category", Encoding.UTF8.GetBytes(category));
        headers.Add("last-failure-reason", Encoding.UTF8.GetBytes(reason.Length > 512 ? reason[..512] : reason));
        if (eventId.HasValue) headers.Add("event-id", Encoding.UTF8.GetBytes(eventId.Value.ToString("D")));
        return new Message<string, string> { Key = result.Message.Key, Value = result.Message.Value, Headers = headers };
    }

    private static int ReadRetryCount(Headers headers)
    {
        var existing = headers.LastOrDefault(header => header.Key == "retry-count");
        if (existing is null) return 1;
        return int.TryParse(Encoding.UTF8.GetString(existing.GetValueBytes()), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value + 1 : 1;
    }

    private static string ComputeHash(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
