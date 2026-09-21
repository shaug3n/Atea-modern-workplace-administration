using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Features.AdminAuth;

public sealed class AdminAuthEndpointTests
{
    [Fact]
    public async Task Login_session_logout_and_customer_boundary_are_separate()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        var login = await client.PostAsJsonAsync("/api/admin-auth/login", new { username = "admin", password = "secret" });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        login.Headers.GetValues("Set-Cookie").Single().ToLowerInvariant().Should().Contain("httponly").And.Contain("samesite=strict");

        var session = await client.GetAsync("/api/admin-auth/session");
        session.StatusCode.Should().Be(HttpStatusCode.OK);
        (await session.Content.ReadAsStringAsync()).Should().Contain("Local Atea Admin");

        var customer = await client.GetAsync("/api/session");
        customer.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var logout = await client.PostAsync("/api/admin-auth/logout", null);
        logout.StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.GetAsync("/api/admin-auth/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Invalid_credentials_return_generic_unauthorized()
    {
        using var client = CreateFactory().CreateClient();

        var response = await client.PostAsJsonAsync("/api/admin-auth/login", new { username = "admin", password = "wrong" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("wrong").And.NotContain("secret");
    }

    [Fact]
    public async Task Local_cookie_can_use_platform_route_but_not_customer_session()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await client.PostAsJsonAsync("/api/admin-auth/login", new { username = "admin", password = "secret" })).StatusCode.Should().Be(HttpStatusCode.OK);

        var platform = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = "00000000-0000-0000-0000-000000000000", displayName = "" });
        platform.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        (await client.GetAsync("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static WebApplicationFactory<Program> CreateFactory() => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AteaAdmin:LocalDevelopment:Enabled"] = "true",
            ["AteaAdmin:LocalDevelopment:Username"] = "admin",
            ["AteaAdmin:LocalDevelopment:Password"] = "secret",
            ["AteaAdmin:LocalDevelopment:ObjectId"] = "22222222-2222-2222-2222-222222222222",
            ["AteaAdmin:LocalDevelopment:DisplayName"] = "Local Atea Admin",
            ["AteaAdmin:LocalDevelopment:AllowAllWorkspaces"] = "true"
        }));
    });
}
