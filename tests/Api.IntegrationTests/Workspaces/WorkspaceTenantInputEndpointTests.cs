using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Net.Http.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PersistenceWorkspaceMembership = Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class WorkspaceTenantInputEndpointTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public async Task Create_rejects_missing_or_mutually_supplied_tenant_inputs()
    {
        var resolver = new StubTenantResolver();
        using var factory = CreateFactory(new RecordingProvisioningService(), resolver);
        using var client = AuthenticatedClient(factory);

        var missing = await client.PostAsJsonAsync("/api/platform/workspaces", new { displayName = "Missing" });
        var both = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = TenantId, tenantDomain = "contoso.com", displayName = "Both" });

        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("\"error\":\"invalid_tenant_input\"");
        both.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        resolver.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Onboard_resolves_domain_before_provisioning_and_returns_canonical_tenant()
    {
        var provisioning = new RecordingProvisioningService();
        var resolver = new StubTenantResolver(TenantResolutionStatus.Resolved, TenantId);
        using var factory = CreateFactory(provisioning, resolver);
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantDomain = "contoso.com",
            displayName = "Onboarded",
            adminUpn = "owner@example.com",
            adminDisplayName = "Workspace Owner"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        provisioning.LastTenantId.Should().Be(TenantId);
        provisioning.CreateCalls.Should().Be(0);
        provisioning.OnboardCalls.Should().Be(1);
        resolver.LastDomain.Should().Be("contoso.com");
    }

    [Fact]
    public async Task Create_keeps_guid_only_requests_and_rejects_empty_guid()
    {
        var provisioning = new RecordingProvisioningService();
        var resolver = new StubTenantResolver();
        using var factory = CreateFactory(provisioning, resolver);
        using var client = AuthenticatedClient(factory);

        var created = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = TenantId, displayName = "Created" });
        var empty = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = Guid.Empty, displayName = "Empty" });

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        provisioning.LastTenantId.Should().Be(TenantId);
        empty.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await empty.Content.ReadAsStringAsync()).Should().Contain("\"error\":\"invalid_tenant_input\"");
        resolver.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Domain_resolution_failures_map_to_safe_statuses_without_provisioning()
    {
        var invalidResolver = new StubTenantResolver(TenantResolutionStatus.InvalidInput);
        using (var factory = CreateFactory(new RecordingProvisioningService(), invalidResolver))
        using (var client = AuthenticatedClient(factory))
        {
            var response = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantDomain = "https://contoso.com", displayName = "Invalid" });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\":\"invalid_tenant_input\"");
        }

        var notFoundProvisioning = new RecordingProvisioningService();
        using (var factory = CreateFactory(notFoundProvisioning, new StubTenantResolver(TenantResolutionStatus.NotFound)))
        using (var client = AuthenticatedClient(factory))
        {
            var response = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantDomain = "unknown.example", displayName = "Missing" });
            response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\":\"tenant_domain_not_found\"");
            notFoundProvisioning.CreateCalls.Should().Be(0);
            notFoundProvisioning.OnboardCalls.Should().Be(0);
        }

        var unavailableProvisioning = new RecordingProvisioningService();
        using (var factory = CreateFactory(unavailableProvisioning, new StubTenantResolver(TenantResolutionStatus.Unavailable)))
        using (var client = AuthenticatedClient(factory))
        {
            var response = await client.PostAsJsonAsync("/api/platform/workspaces/onboard", new
            {
                tenantDomain = "contoso.com",
                displayName = "Unavailable",
                adminUpn = "owner@example.com",
                adminDisplayName = "Workspace Owner"
            });
            response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
            (await response.Content.ReadAsStringAsync()).Should().Contain("\"error\":\"tenant_resolution_unavailable\"");
            unavailableProvisioning.CreateCalls.Should().Be(0);
            unavailableProvisioning.OnboardCalls.Should().Be(0);
        }
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingProvisioningService provisioning,
        StubTenantResolver resolver) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id",
                ["PlatformAuthorization:AdminObjectIds:0"] = ObjectId.ToString()
            });
        }).ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
            services.AddAuthorization(options => options.AddPolicy("PlatformAdminPolicy", policy => policy
                .AddAuthenticationSchemes(TestAuthenticationHandler.Scheme)
                .RequireAuthenticatedUser()
                .RequireClaim("oid")));
            services.RemoveAll<IWorkspaceProvisioningService>();
            services.AddSingleton<IWorkspaceProvisioningService>(provisioning);
            services.RemoveAll<IPlatformAuthorization>();
            services.AddSingleton<IPlatformAuthorization>(new RecordingPlatformAuthorization());
            services.RemoveAll<ITenantResolver>();
            services.AddSingleton<ITenantResolver>(resolver);
        }));

    private sealed class StubTenantResolver(
        TenantResolutionStatus status = TenantResolutionStatus.InvalidInput,
        Guid? tenantId = null) : ITenantResolver
    {
        public int Calls { get; private set; }
        public string? LastDomain { get; private set; }

        public Task<TenantResolutionResult> ResolveAsync(string domain, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastDomain = domain;
            return Task.FromResult(new TenantResolutionResult(status, tenantId));
        }
    }

    private sealed class RecordingProvisioningService : IWorkspaceProvisioningService
    {
        public int CreateCalls { get; private set; }
        public int OnboardCalls { get; private set; }
        public Guid? LastTenantId { get; private set; }

        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Workspace>>([]);

        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceAdminDetailDto?>(null);

        public Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            LastTenantId = tenantId;
            return Task.FromResult(WorkspaceProvisioningResult.Created(new Workspace
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                DisplayName = displayName,
                ConnectionStatus = "awaiting_invitation"
            }));
        }

        public Task<WorkspaceOnboardingProvisioningResult> OnboardAsync(
            Guid tenantId,
            string displayName,
            string adminUpn,
            string adminDisplayName,
            AuditEvent auditEvent,
            CancellationToken cancellationToken = default)
        {
            OnboardCalls++;
            LastTenantId = tenantId;
            return Task.FromResult(WorkspaceOnboardingProvisioningResult.Created(
                new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName },
                "https://example.test/invitations/token",
                DateTimeOffset.UtcNow.AddDays(7)));
        }

        public Task<PersistenceWorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PersistenceWorkspaceMembership());

        public Task<PersistenceWorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) =>
            Task.FromResult(new PersistenceWorkspaceMembership());

        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Workspace?>(null);
    }

    private sealed class RecordingPlatformAuthorization : IPlatformAuthorization
    {
        public bool IsAuthorized(ClaimsPrincipal principal) => true;
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
                new Claim("oid", ObjectId.ToString()),
                new Claim("tid", TenantId.ToString()),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(
                new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)),
                Scheme)));
        }
    }
}
