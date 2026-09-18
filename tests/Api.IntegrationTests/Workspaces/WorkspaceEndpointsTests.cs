using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class WorkspaceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient client;

    public WorkspaceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        client = factory.WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["PlatformAuthorization:AdminObjectIds:0"] = "22222222-2222-2222-2222-222222222222"
            });
        }).ConfigureServices(services => { })).CreateClient();
    }

    [Fact]
    public async Task Current_workspace_requires_verified_workspace_context()
    {
        var response = await client.GetAsync("/api/workspaces/current");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
