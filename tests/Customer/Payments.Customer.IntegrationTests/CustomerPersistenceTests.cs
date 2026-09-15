using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Infrastructure.Runtime;
using Payments.BuildingBlocks.Messaging.Events;
using Payments.Customer.Infrastructure.Messaging;
using Payments.Customer.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace Payments.Customer.IntegrationTests;

public sealed class CustomerPersistenceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres;

    public CustomerPersistenceTests()
    {
        Environment.SetEnvironmentVariable("DOCKER_API_VERSION", Environment.GetEnvironmentVariable("DOCKER_API_VERSION") ?? "1.41");
        _postgres = new PostgreSqlBuilder("postgres:18-alpine").Build();
    }

    public async Task InitializeAsync() => await _postgres.StartAsync();

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Fact]
    public async Task Migrations_create_customer_schema()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        (await dbContext.Customers.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Identity_registration_event_is_idempotent()
    {
        await using var dbContext = CreateContext();
        await dbContext.Database.MigrateAsync();
        var repository = new CustomerRepository(dbContext);
        var handler = new IdentityUserRegisteredHandler(repository, new SystemClock(), new TestRequestContext(), NullLogger<IdentityUserRegisteredHandler>.Instance);
        var payload = new IdentityUserRegisteredIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), "customer@example.com", "+2348012345678");
        var envelope = new IntegrationEventEnvelope<IdentityUserRegisteredIntegrationEvent>(Guid.NewGuid(), IdentityUserRegisteredIntegrationEvent.EventType, IdentityUserRegisteredIntegrationEvent.EventVersion, DateTimeOffset.UtcNow, Guid.NewGuid().ToString("D"), null, "identity-service", payload);

        await handler.HandleAsync(envelope);
        await handler.HandleAsync(envelope);

        (await dbContext.Customers.CountAsync()).Should().Be(1);
        (await dbContext.ProcessedIntegrationEvents.CountAsync()).Should().Be(1);
    }

    private CustomerDbContext CreateContext()
        => new(new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(_postgres.GetConnectionString()).Options);

    private sealed class TestRequestContext : IRequestContext
    {
        public string CorrelationId => Guid.NewGuid().ToString("D");
        public string? CausationId => null;
        public string TraceId => Guid.NewGuid().ToString("D");
    }
}
