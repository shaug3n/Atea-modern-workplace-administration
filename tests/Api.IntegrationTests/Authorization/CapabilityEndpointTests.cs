using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Authorization;

public sealed class CapabilityEndpointTests
{
    [Fact]
    public async Task Capability_endpoint_returns_complete_workspace_scoped_snapshot()
    {
        using var factory = CreateFactory(GraphAuthorizationSnapshot.Available(
            "user-1",
            ["Directory.Read.All", "User.Read.All"],
            [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/capabilities");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"workspaceId\":\"55555555-5555-5555-5555-555555555555\"");
        body.Should().Contain("\"capability\":\"users.view\"");
        body.Should().Contain("\"state\":\"allowed\"");
        body.Should().Contain("\"capability\":\"users.create\"");
        body.Should().Contain("\"state\":\"read_only\"");
        body.Should().NotContain("access_token");
        body.Should().NotContain("Authorization");
    }

    [Fact]
    public async Task Unavailable_graph_snapshot_cannot_grant_a_mutation()
    {
        using var factory = CreateFactory(GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/capabilities");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"capability\":\"users.create\"");
        body.Should().Contain("\"state\":\"temporarily_unavailable\"");
        body.Should().NotContain("\"capability\":\"users.create\",\"state\":\"allowed\"");
    }

    [Fact]
    public async Task Consent_start_allows_a_nominated_customer_admin_without_platform_settings_access()
    {
        var reader = new RecordingSnapshotReader(GraphAuthorizationSnapshot.Available("user-1", ["Directory.Read.All"], []));
        using var factory = CreateFactory(reader: reader, platformRole: "member");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsync("/api/workspaces/current/consent/start", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"authorizationUrl\"");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Platform_only_guard_allows_workspace_admin_without_delegated_graph_availability()
    {
        var reader = new RecordingSnapshotReader(GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"));
        using var factory = CreateFactory(reader: reader, platformRole: "admin");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsync("/api/workspaces/current/consent/start", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"authorizationUrl\"");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Consent_start_uses_the_validated_configured_redirect_uri()
    {
        const string publicBaseUrl = "https://consent.workplace.example";
        const string consentRedirectUri = $"{publicBaseUrl}/onboarding/consent/callback";
        using var factory = CreateFactory(consentRedirectUri: consentRedirectUri, publicBaseUrl: publicBaseUrl);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsync("/api/workspaces/current/consent/start", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain(Uri.EscapeDataString(consentRedirectUri));
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GraphAuthorizationSnapshot? snapshot = null,
        RecordingSnapshotReader? reader = null,
        string platformRole = "admin",
        string consentRedirectUri = "http://localhost:5173/onboarding/consent/callback",
        string publicBaseUrl = "http://localhost:5173") =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                    ["AzureAd:ClientId"] = "test-client-id",
                    ["Onboarding:PublicBaseUrl"] = publicBaseUrl,
                    ["Onboarding:ConsentSigningKey"] = Convert.ToBase64String(new byte[32]),
                    ["Onboarding:ConsentRedirectUri"] = consentRedirectUri
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(platformRole));
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(reader ?? new RecordingSnapshotReader(snapshot ?? GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable")));
                services.RemoveAll<IConsentChallengeRepository>();
                services.AddSingleton<IConsentChallengeRepository, RecordingConsentChallengeRepository>();
            });
        });

    private sealed class RecordingSnapshotReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public int Calls { get; private set; }

        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Task.FromResult(snapshot);
        }
    }

    private sealed class RecordingConsentChallengeRepository : IConsentChallengeRepository
    {
        public Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(true);
    }

    private sealed class FixtureMembershipReader(string platformRole) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                "customer-workspace",
                platformRole,
                ModuleKeys: ["users", "devices", "licenses"]));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("oid", "22222222-2222-2222-2222-222222222222"),
                new Claim("tid", "11111111-1111-1111-1111-111111111111"),
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.ContentType = "application/json";
            return Response.WriteAsync("{\"error\":\"authentication_required\"}");
        }
    }
}
