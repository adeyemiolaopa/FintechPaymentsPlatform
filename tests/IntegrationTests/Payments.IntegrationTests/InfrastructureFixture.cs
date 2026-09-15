using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.Service.Template.Infrastructure.Persistence;
using Testcontainers.Kafka;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Payments.IntegrationTests;

public sealed class InfrastructureFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;
    private readonly RedisContainer _redis;
    private readonly KafkaContainer _kafka;

    public InfrastructureFixture()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", Environment.GetEnvironmentVariable("DOCKER_API_VERSION") ?? "1.41");
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
        _redis = new RedisBuilder("redis:8-alpine").Build();
        _kafka = new KafkaBuilder("confluentinc/cp-kafka:8.1.0").WithKRaft().Build();
    }

    public WebApplicationFactory<Program>? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await _redis.StartAsync();
        await _kafka.StartAsync();

        Environment.SetEnvironmentVariable("Database__ConnectionString", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable("Redis__ConnectionString", _redis.GetConnectionString());
        Environment.SetEnvironmentVariable("Redis__KeyPrefix", "test:template");
        Environment.SetEnvironmentVariable("Kafka__BootstrapServers", _kafka.GetBootstrapAddress());
        Environment.SetEnvironmentVariable("Kafka__ProducerName", "integration-tests");
        Environment.SetEnvironmentVariable("Observability__EnableOtlpExporter", "false");

        Factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Database:ConnectionString"] = _postgres.GetConnectionString(),
                        ["Redis:ConnectionString"] = _redis.GetConnectionString(),
                        ["Redis:KeyPrefix"] = "test:template",
                        ["Kafka:BootstrapServers"] = _kafka.GetBootstrapAddress(),
                        ["Kafka:ProducerName"] = "integration-tests",
                        ["Observability:EnableOtlpExporter"] = "false",
                    });
                });
            });

        using var scope = Factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TemplateDbContext>();
        await dbContext.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }
        await _kafka.DisposeAsync();
        await _redis.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

[CollectionDefinition(Name)]
public sealed class InfrastructureCollection : ICollectionFixture<InfrastructureFixture>
{
    public const string Name = "infrastructure";
}
