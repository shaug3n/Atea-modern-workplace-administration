using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.PlatformAbout;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.PlatformAbout;

public sealed class PlatformAboutEndpointTests
{
    private const string ApiUrl = "/api/about/system-versions";
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    [Fact]
    public async Task System_versions_requires_authentication_workspace_about_module_and_capability()
    {
        using var unauthenticatedFactory = CreateFactory();
        using var unauthenticatedClient = unauthenticatedFactory.CreateClient();
        var unauthenticated = await unauthenticatedClient.GetAsync(ApiUrl);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var noMembershipFactory = CreateFactory(includeMembership: false);
        using var noMembershipClient = AuthorizedClient(noMembershipFactory);
        var noMembership = await noMembershipClient.GetAsync(ApiUrl);
        noMembership.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var wrongModuleFactory = CreateFactory(moduleKeys: ["users"]);
        using var wrongModuleClient = AuthorizedClient(wrongModuleFactory);
        var wrongModule = await wrongModuleClient.GetAsync(ApiUrl);
        wrongModule.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var aboutEndpoint = wrongModuleFactory.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Single(endpoint => endpoint.RoutePattern.RawText == ApiUrl);
        aboutEndpoint.Metadata.GetMetadata<RequireCapabilityAttribute>()!.Capability.Should().Be(Capability.PlatformAboutView);
    }

    [Fact]
    public async Task System_versions_returns_api_build_metadata_for_authorized_member()
    {
        using var factory = CreateFactory(
            productVersion: "0.1.0",
            commit: "abc123",
            branch: "feature/f6",
            moduleKeys: ["about"]);
        factory.Services.GetRequiredService<PlatformBuildMetadata>().Should().Be(new PlatformBuildMetadata("0.1.0", "abc123", "feature/f6"));
        using var client = AuthorizedClient(factory);

        var response = await client.GetAsync(ApiUrl);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("{\"productVersion\":\"0.1.0\",\"commit\":\"abc123\",\"branch\":\"feature/f6\"}");
    }

    [Fact]
    public async Task System_versions_labels_missing_inputs_unavailable()
    {
        using var factory = CreateFactory(moduleKeys: ["about"]);
        using var client = AuthorizedClient(factory);

        var response = await client.GetAsync(ApiUrl);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("{\"productVersion\":\"Unavailable\",\"commit\":\"Unavailable\",\"branch\":\"Unavailable\"}");
    }

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        IReadOnlyCollection<string>? moduleKeys = null,
        bool includeMembership = true,
        string? productVersion = null,
        string? commit = null,
        string? branch = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                    ["AzureAd:ClientId"] = "test-client-id",
                    ["AteaBuild:ProductVersion"] = productVersion,
                    ["AteaBuild:Commit"] = commit,
                    ["AteaBuild:Branch"] = branch
                }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(includeMembership
                    ? new WorkspaceMembership(WorkspaceId, "customer-workspace", ModuleKeys: moduleKeys ?? ["about"])
                    : null));
                services.RemoveAll<IWorkspaceSettingsService>();
                services.AddSingleton<IWorkspaceSettingsService, FixtureWorkspaceSettingsService>();
            });
        });

    private sealed class FixtureMembershipReader(WorkspaceMembership? membership) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(membership);
    }

    private sealed class FixtureWorkspaceSettingsService : IWorkspaceSettingsService
    {
        public Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceConfiguration(["about"], [], new Dictionary<string, string>(), string.Empty, "light"));

        public Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("oid", ObjectId.ToString()),
                new Claim("tid", TenantId.ToString()),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
