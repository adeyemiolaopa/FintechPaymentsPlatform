using Confluent.Kafka;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Service.Template.Infrastructure.Persistence;
using StackExchange.Redis;

namespace Payments.Service.Template.Api.Health;

public sealed class PostgresHealthCheck : IHealthCheck
{
    private readonly TemplateDbContext _dbContext;

    public PostgresHealthCheck(TemplateDbContext dbContext) => _dbContext = dbContext;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Database.CanConnectAsync(cancellationToken).ConfigureAwait(false)
            ? HealthCheckResult.Healthy("PostgreSQL is reachable.")
            : HealthCheckResult.Unhealthy("PostgreSQL is not reachable.");
    }
}

public sealed class RedisHealthCheck : IHealthCheck
{
    private readonly IConnectionMultiplexer _connection;

    public RedisHealthCheck(IConnectionMultiplexer connection) => _connection = connection;

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var database = _connection.GetDatabase();
        var result = await database.PingAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        return result > TimeSpan.Zero ? HealthCheckResult.Healthy("Redis is reachable.") : HealthCheckResult.Unhealthy("Redis ping failed.");
    }
}

public sealed class KafkaHealthCheck : IHealthCheck
{
    private readonly KafkaOptions _options;

    public KafkaHealthCheck(IOptions<KafkaOptions> options) => _options = options.Value;

    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = _options.BootstrapServers }).Build();
        var metadata = admin.GetMetadata(TimeSpan.FromSeconds(3));
        return Task.FromResult(metadata.Brokers.Count > 0
            ? HealthCheckResult.Healthy("Kafka is reachable.")
            : HealthCheckResult.Unhealthy("Kafka has no reachable brokers."));
    }
}
