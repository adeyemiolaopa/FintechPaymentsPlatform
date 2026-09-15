using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Account.Infrastructure.Messaging;

public sealed class AccountConsumerOptions
{
    public const string SectionName = "AccountConsumer";
    public bool Enabled { get; init; } = true;
    public string GroupId { get; init; } = "account-service";
    public string CustomerLifecycleTopic { get; init; } = "customer.lifecycle.v1";
}

public sealed class CustomerLifecycleConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly AccountConsumerOptions _options;
    private readonly ILogger<CustomerLifecycleConsumer> _logger;

    public CustomerLifecycleConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<AccountConsumerOptions> options, ILogger<CustomerLifecycleConsumer> logger)
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
        consumer.Subscribe(_options.CustomerLifecycleTopic);
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

                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<CustomerLifecycleIntegrationEvent>>(result.Message.Value, SerializerOptions);
                if (envelope is not null && envelope.EventType == CustomerLifecycleIntegrationEvent.EventType)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<CustomerLifecycleHandler>().HandleAsync(envelope, stoppingToken).ConfigureAwait(false);
                }

                consumer.Commit(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Account customer lifecycle consumer failed while processing message");
            }
        }
    }
}
