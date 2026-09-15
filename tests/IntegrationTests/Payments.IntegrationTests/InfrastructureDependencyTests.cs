using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Payments.BuildingBlocks.Application.Abstractions;
using Payments.BuildingBlocks.Messaging.Events;

namespace Payments.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class InfrastructureDependencyTests
{
    private readonly InfrastructureFixture _fixture;

    public InfrastructureDependencyTests(InfrastructureFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Redis_cache_round_trip_uses_real_container()
    {
        using var scope = _fixture.Factory!.Services.CreateScope();
        var cache = scope.ServiceProvider.GetRequiredService<ICacheService>();

        await cache.SetAsync("example:redis", new CacheProbe("ok"), TimeSpan.FromMinutes(1));
        var loaded = await cache.GetAsync<CacheProbe>("example:redis");

        loaded.Should().Be(new CacheProbe("ok"));
    }

    [Fact]
    public async Task Kafka_publisher_can_publish_to_real_container()
    {
        using var scope = _fixture.Factory!.Services.CreateScope();
        var publisher = scope.ServiceProvider.GetRequiredService<IEventPublisher>();

        await publisher.PublishAsync("template.integration.probe", new ProbeIntegrationEvent("ok"));
    }

    private sealed record CacheProbe(string Value);

    private sealed record ProbeIntegrationEvent(string Value) : IIntegrationEvent
    {
        public static string EventType => "template.probe";

        public static int EventVersion => 1;
    }
}
