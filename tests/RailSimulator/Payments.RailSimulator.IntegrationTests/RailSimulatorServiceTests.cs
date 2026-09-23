using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Payments.RailSimulator.Application;
using Payments.RailSimulator.Infrastructure;
using Payments.RailSimulator.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace Payments.RailSimulator.IntegrationTests;

public sealed class RailSimulatorFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:18-alpine")
        .WithDatabase("payments_rail_simulator")
        .WithUsername("payments")
        .WithPassword("change-me-local-only")
        .Build();

    public ServiceProvider Services { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RailSimulatorDatabase:ConnectionString"] = _postgres.GetConnectionString(),
                ["RailSimulator:RequireRequestSignature"] = "false",
                ["RailSimulator:WorkerIntervalMs"] = "100",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRailSimulatorInfrastructure(configuration);
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.InitializeRailSimulatorAsync();
    }

    public async Task DisposeAsync()
    {
        await Services.DisposeAsync();
        await _postgres.DisposeAsync();
    }
}

public sealed class RailSimulatorServiceTests : IClassFixture<RailSimulatorFixture>
{
    private readonly RailSimulatorFixture _fixture;

    public RailSimulatorServiceTests(RailSimulatorFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task SubmitAsync_ShouldCreateSuccessfulTransfer()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();

        var response = await service.SubmitAsync(CreateRequest("rail-success-1"), new RailRequestContext("client-a"));
        var status = await service.GetByProviderReferenceAsync(response.ProviderReference);

        response.Status.Should().Be("Successful");
        status.ClientReference.Should().Be("rail-success-1");
        status.Status.Should().Be("Successful");
    }

    [Fact]
    public async Task SubmitAsync_ShouldReturnExistingTransfer_ForDuplicateSameInstruction()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();

        var first = await service.SubmitAsync(CreateRequest("rail-duplicate-1"), new RailRequestContext("client-a"));
        var second = await service.SubmitAsync(CreateRequest("rail-duplicate-1"), new RailRequestContext("client-a"));

        second.ProviderReference.Should().Be(first.ProviderReference);
    }

    [Fact]
    public async Task SubmitAsync_ShouldRejectDuplicateReference_WithDifferentInstruction()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();

        await service.SubmitAsync(CreateRequest("rail-conflict-1"), new RailRequestContext("client-a"));
        var act = () => service.SubmitAsync(CreateRequest("rail-conflict-1", amount: 150m), new RailRequestContext("client-a"));

        await act.Should().ThrowAsync<RailSimulatorException>().Where(exception => exception.StatusCode == 409);
    }

    [Fact]
    public async Task DelayedScenario_ShouldCompleteWhenWorkerTickRuns()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();
        await service.ConfigureScenarioAsync(new ProviderScenarioRequest("PendingThenSuccess", DelayMs: 1));

        var response = await service.SubmitAsync(CreateRequest("rail-delayed-1"), new RailRequestContext("client-a"));
        await Task.Delay(20);
        await ((RailSimulatorService)service).CompleteDueTransfersAsync();
        var status = await service.GetByProviderReferenceAsync(response.ProviderReference);

        response.Status.Should().Be("Processing");
        status.Status.Should().Be("Successful");
    }

    [Fact]
    public async Task TimeoutAfterProcessing_ShouldPersistAcceptedTransfer()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();

        var act = () => service.SubmitAsync(CreateRequest("rail-timeout-1"), new RailRequestContext("client-a", ScenarioOverride: Payments.RailSimulator.Domain.RailScenarioMode.TimeoutAfterProcessing));

        await act.Should().ThrowAsync<RailSimulatorException>().Where(exception => exception.StatusCode == 504);
        var status = await service.GetByClientReferenceAsync("client-a", "rail-timeout-1");
        status.Status.Should().Be("Successful");
    }

    [Fact]
    public async Task DuplicateCallbackScenario_ShouldScheduleMultipleCallbackAttempts()
    {
        await ResetAsync();
        using var scope = _fixture.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IRailSimulatorService>();
        await service.ConfigureCallbackAsync(new CallbackConfigurationRequest("http://127.0.0.1:1/callback", "webhook-secret"));
        await service.ConfigureScenarioAsync(new ProviderScenarioRequest("DuplicateCallback", DuplicateCallbackCount: 2));

        var response = await service.SubmitAsync(CreateRequest("rail-callback-1"), new RailRequestContext("client-a"));
        var callbacks = await service.GetCallbackAttemptsAsync(response.ProviderReference);

        callbacks.Should().HaveCount(2);
        callbacks.Select(callback => callback.AttemptNumber).Should().BeEquivalentTo([1, 2]);
    }

    private async Task ResetAsync()
    {
        using var scope = _fixture.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IRailSimulatorService>().ResetAsync();
    }

    private static SubmitRailTransferRequest CreateRequest(string clientReference, decimal amount = 100m)
        => new(clientReference, "058", "0123456789", "Ada Lovelace", amount, "NGN", "Integration test");
}
