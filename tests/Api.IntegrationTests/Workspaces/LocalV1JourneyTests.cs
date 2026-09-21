using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class LocalV1JourneyTests
{
    [Fact]
    public async Task Local_admin_cookie_reaches_platform_routes_but_not_customer_graph_routes()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        (await client.PostAsJsonAsync("/api/admin-auth/login", new { username = "local-admin", password = "local-password" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        (await client.GetAsync("/api/admin-auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/workspaces/current")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Customer_invitation_route_is_reachable_without_leaking_local_admin_session()
    {
        using var client = CreateFactory().CreateClient();

        var response = await client.PostAsync("/api/invitations/not-a-real-nonce/redeem", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("not-a-real-nonce");
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AteaAdmin:LocalDevelopment:Enabled"] = "true",
            ["AteaAdmin:LocalDevelopment:Username"] = "local-admin",
            ["AteaAdmin:LocalDevelopment:Password"] = "local-password",
            ["AteaAdmin:LocalDevelopment:ObjectId"] = "22222222-2222-2222-2222-222222222222",
            ["AteaAdmin:LocalDevelopment:DisplayName"] = "Local Atea Admin",
            ["AteaAdmin:LocalDevelopment:AllowAllWorkspaces"] = "true"
        }));
    });
}
