using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Ledger.Infrastructure.Messaging;

public sealed class AccountLifecycleConsumerOptions
{
    public const string SectionName = "LedgerConsumer";
    public bool Enabled { get; init; } = true;
    public string GroupId { get; init; } = "ledger-service";
    public string AccountLifecycleTopic { get; init; } = "account.lifecycle.v1";
}

public sealed class AccountLifecycleConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly AccountLifecycleConsumerOptions _options;
    private readonly ILogger<AccountLifecycleConsumer> _logger;

    public AccountLifecycleConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<AccountLifecycleConsumerOptions> options, ILogger<AccountLifecycleConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _kafkaOptions = kafkaOptions.Value;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            return;
        }

        using var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();
        consumer.Subscribe(_options.AccountLifecycleTopic);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = consumer.Consume(TimeSpan.FromSeconds(1));
                if (result is null)
                {
                    await Task.Yield();
                    continue;
                }

                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<AccountLifecycleIntegrationEvent>>(result.Message.Value, SerializerOptions);
                if (envelope is not null && envelope.EventType == AccountLifecycleIntegrationEvent.EventType)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<AccountLifecycleHandler>().HandleAsync(envelope, stoppingToken).ConfigureAwait(false);
                }

                consumer.Commit(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Ledger account lifecycle consumer failed while processing message");
            }
        }
    }
}
