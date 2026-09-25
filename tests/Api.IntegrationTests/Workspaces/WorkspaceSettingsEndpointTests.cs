using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class WorkspaceSettingsEndpointTests
{
    [Fact]
    public async Task Updates_allowlisted_settings_for_platform_member()
    {
        using var factory = CreateFactory(new AuthorizationWorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", "admin"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PatchAsJsonAsync("/api/workspaces/current/settings", new
        {
            displayName = "Operations workspace",
            defaultColumns = new[] { "displayName", "userPrincipalName" },
            defaultFilters = new Dictionary<string, string> { ["accountStatus"] = "enabled" },
            supportInstructions = "Contact the service desk.",
            defaultTheme = "dark"
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Operations workspace");
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("javascript:alert(1)")]
    public async Task Rejects_script_or_navigation_content(string supportInstructions)
    {
        using var factory = CreateFactory(new AuthorizationWorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", "admin"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PatchAsJsonAsync("/api/workspaces/current/settings", new { supportInstructions });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Non_platform_member_cannot_patch_or_read_hidden_settings()
    {
        using var factory = CreateFactory(new AuthorizationWorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", "member"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var get = await client.GetAsync("/api/workspaces/current/settings");
        var patch = await client.PatchAsJsonAsync("/api/workspaces/current/settings", new { displayName = "Nope" });

        get.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        patch.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private static WebApplicationFactory<Program> CreateFactory(AuthorizationWorkspaceMembership membership) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
        ["AzureAd:ClientId"] = "test-client-id"
    })).ConfigureServices(services =>
    {
        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
            options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
        }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
        services.RemoveAll<IWorkspaceMembershipReader>();
        services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(membership));
    }));

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private sealed class FixtureMembershipReader(AuthorizationWorkspaceMembership membership) : IWorkspaceMembershipReader { public Task<AuthorizationWorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationWorkspaceMembership?>(membership); }
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim("oid", ObjectId.ToString()), new Claim("tid", TenantId.ToString()), new Claim("aud", "api://atea-unified-workplace-api") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
