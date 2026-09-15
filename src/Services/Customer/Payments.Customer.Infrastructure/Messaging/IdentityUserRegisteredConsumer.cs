using System.Text.Json;
using Confluent.Kafka;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.Customer.Infrastructure.Messaging;

public sealed class CustomerConsumerOptions
{
    public const string SectionName = "CustomerConsumer";

    public bool Enabled { get; init; } = true;
    public string GroupId { get; init; } = "customer-service";
    public string Topic { get; init; } = "identity.lifecycle.v1";
}

public sealed class IdentityUserRegisteredConsumer : BackgroundService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KafkaOptions _kafkaOptions;
    private readonly CustomerConsumerOptions _options;
    private readonly ILogger<IdentityUserRegisteredConsumer> _logger;

    public IdentityUserRegisteredConsumer(IServiceScopeFactory scopeFactory, IOptions<KafkaOptions> kafkaOptions, IOptions<CustomerConsumerOptions> options, ILogger<IdentityUserRegisteredConsumer> logger)
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

        var config = new ConsumerConfig
        {
            BootstrapServers = _kafkaOptions.BootstrapServers,
            GroupId = _options.GroupId,
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        };

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(_options.Topic);
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

                var envelope = JsonSerializer.Deserialize<IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent>>(result.Message.Value, SerializerOptions);
                if (envelope is not null && envelope.EventType == IdentityUserRegisteredIntegrationEvent.EventType)
                {
                    await using var scope = _scopeFactory.CreateAsyncScope();
                    await scope.ServiceProvider.GetRequiredService<IdentityUserRegisteredHandler>().HandleAsync(envelope, stoppingToken).ConfigureAwait(false);
                }

                consumer.Commit(result);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Customer identity lifecycle consumer failed while processing message");
            }
        }
    }
}

