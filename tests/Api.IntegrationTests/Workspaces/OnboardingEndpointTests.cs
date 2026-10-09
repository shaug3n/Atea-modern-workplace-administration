using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class OnboardingEndpointTests : IClassFixture<ApiIntegrationTestFactory>
{
    private readonly HttpClient client;

    public OnboardingEndpointTests(ApiIntegrationTestFactory factory) => client = factory.CreateClient();

    [Theory]
    [InlineData("/api/workspaces/current/connection-health")]
    [InlineData("/api/workspaces/current/connection-health/check")]
    [InlineData("/api/workspaces/current/consent/start")]
    public async Task Connection_endpoints_require_the_existing_authenticated_workspace_context(string path)
    {
        var response = path.EndsWith("/check") ? await client.PostAsync(path, null) : await client.GetAsync(path);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
