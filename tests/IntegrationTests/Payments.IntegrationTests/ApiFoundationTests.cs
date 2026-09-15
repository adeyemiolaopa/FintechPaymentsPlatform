using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Payments.Service.Template.Application.Examples;

namespace Payments.IntegrationTests;

[Collection(InfrastructureCollection.Name)]
public sealed class ApiFoundationTests
{
    private readonly HttpClient _client;

    public ApiFoundationTests(InfrastructureFixture fixture) => _client = fixture.Factory!.CreateClient();

    [Fact]
    public async Task Liveness_endpoint_returns_healthy()
    {
        var response = await _client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Correlation_id_is_generated_when_missing()
    {
        var response = await _client.GetAsync("/health/live");

        response.Headers.TryGetValues("X-Correlation-Id", out var values).Should().BeTrue();
        values!.Single().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Correlation_id_is_propagated_when_supplied()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add("X-Correlation-Id", "integration-correlation-1");

        var response = await _client.SendAsync(request);

        response.Headers.GetValues("X-Correlation-Id").Single().Should().Be("integration-correlation-1");
    }

    [Fact]
    public async Task Versioned_example_post_and_get_round_trip()
    {
        var create = new CreateExampleRequest("Integration Demo", "integration@example.com", "external-12345");

        var createdResponse = await _client.PostAsJsonAsync("/api/v1/examples", create);
        createdResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createdResponse.Content.ReadFromJsonAsync<CreateExampleResponse>();

        var getResponse = await _client.GetAsync($"/api/v1/examples/{created!.Id:D}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var loaded = await getResponse.Content.ReadFromJsonAsync<ExampleResponse>();
        loaded!.Email.Should().Be("integration@example.com");
    }

    [Fact]
    public async Task Validation_errors_use_problem_details()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/examples", new CreateExampleRequest("", "bad", "bad value"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }
}
