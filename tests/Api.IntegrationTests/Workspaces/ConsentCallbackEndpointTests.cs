using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class ConsentCallbackEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> factory;

    public ConsentCallbackEndpointTests(WebApplicationFactory<Program> factory) => this.factory = factory;

    [Fact]
    public async Task Consent_completion_requires_the_authenticated_workspace_context()
    {
        using var client = factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/workspaces/current/consent/complete", new
        {
            state = "not-a-token",
            tenant = Guid.NewGuid()
        });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("not-a-token");
    }
}
