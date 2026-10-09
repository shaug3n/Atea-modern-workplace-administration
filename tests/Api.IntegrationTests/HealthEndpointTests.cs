using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests;

public sealed class HealthEndpointTests : IClassFixture<ApiIntegrationTestFactory>
{
    private readonly HttpClient client;

    public HealthEndpointTests(ApiIntegrationTestFactory factory) => client = factory.CreateClient();

    [Fact]
    public async Task ApiStartsAndExposesHealthEndpoint()
    {
        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, "response body was {0}", body);
        body.Should().Be("{\"status\":\"ok\"}");
    }
}
