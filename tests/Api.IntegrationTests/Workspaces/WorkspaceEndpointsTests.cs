using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;
using PersistenceWorkspaceMembership = Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.WorkspaceMembership;

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

    [Fact]
    public async Task Membership_endpoint_returns_structured_forbidden_without_target_workspace_scope()
    {
        using var factory = CreateFactory(new RecordingProvisioningService(), []);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsJsonAsync($"/api/platform/workspaces/{WorkspaceId}/memberships", new { tenantObjectId = ObjectId, email = "member@example.com", platformRole = "member", isAteaOperator = false });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"workspace_provisioning_scope_required\"}");
    }

    [Fact]
    public async Task Membership_endpoint_uses_provisioning_service_after_scope_check()
    {
        var service = new RecordingProvisioningService();
        using var factory = CreateFactory(service, [WorkspaceId]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsJsonAsync($"/api/platform/workspaces/{WorkspaceId}/memberships", new { tenantObjectId = ObjectId, email = "member@example.com", platformRole = "member", isAteaOperator = false });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        service.AddMembershipCalls.Should().Be(1);
    }

    [Fact]
    public async Task Workspace_creation_conflict_is_returned_as_409()
    {
        using var factory = CreateFactory(new RecordingProvisioningService { CreateConflict = true }, []);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = TenantId, displayName = "Duplicate" });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"workspace_already_exists\"}");
    }

    [Fact]
    public async Task Platform_workspace_list_returns_only_authorized_workspaces()
    {
        var service = new RecordingProvisioningService
        {
            ListedWorkspaces = [new Workspace { Id = WorkspaceId, TenantId = TenantId, DisplayName = "Authorized", ConnectionStatus = "connected" }]
        };
        using var factory = CreateFactory(service, [WorkspaceId]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/platform/workspaces");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("Authorized");
        service.LastListScope!.WorkspaceIds.Should().Contain(WorkspaceId);
    }

    [Fact]
    public async Task Platform_workspace_detail_returns_not_found_outside_scope_and_has_no_invitation_secrets()
    {
        var service = new RecordingProvisioningService
        {
            AdminDetail = new WorkspaceAdminDetailDto(WorkspaceId, TenantId, "Authorized", "connected", null, null, [], [new InvitationSummaryDto(Guid.NewGuid(), "invitee@example.com", "Invitee", DateTimeOffset.UtcNow.AddDays(1), null)])
        };
        using var factory = CreateFactory(service, [WorkspaceId]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync($"/api/platform/workspaces/{Guid.NewGuid()}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        service.LastDetailScope!.WorkspaceIds.Should().Contain(WorkspaceId);
    }

    [Fact]
    public async Task Platform_workspace_detail_response_omits_nonce_hash_and_invitation_url()
    {
        var service = new RecordingProvisioningService
        {
            AdminDetail = new WorkspaceAdminDetailDto(WorkspaceId, TenantId, "Authorized", "connected", null, null, [], [new InvitationSummaryDto(Guid.NewGuid(), "invitee@example.com", "Invitee", DateTimeOffset.UtcNow.AddDays(1), null)])
        };
        using var factory = CreateFactory(service, [WorkspaceId]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync($"/api/platform/workspaces/{WorkspaceId}");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().NotContain("nonceHash");
        body.Should().NotContain("invitationUrl");
    }

    [Fact]
    public async Task Membership_database_failure_is_returned_as_503()
    {
        using var factory = CreateFactory(new RecordingProvisioningService { MembershipUnavailable = true }, [WorkspaceId]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PostAsJsonAsync($"/api/platform/workspaces/{WorkspaceId}/memberships", new { tenantObjectId = ObjectId, email = "member@example.com", platformRole = "member", isAteaOperator = false });

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"workspace_database_unavailable\"}");
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingProvisioningService service, Guid[] scopedWorkspaces) =>
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
            services.AddSingleton<IWorkspaceProvisioningService>(service);
            services.RemoveAll<IPlatformAuthorization>();
            services.AddSingleton<IPlatformAuthorization>(new RecordingPlatformAuthorization(scopedWorkspaces));
            services.RemoveAll<IWorkspaceMembershipReader>();
            services.AddSingleton<IWorkspaceMembershipReader>(new NullMembershipReader());
        }));

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class NullMembershipReader : IWorkspaceMembershipReader
    {
        public Task<AuthorizationWorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<AuthorizationWorkspaceMembership?>(null);
    }

    private sealed class RecordingPlatformAuthorization(Guid[] scopedWorkspaces) : IPlatformAuthorization
    {
        public bool IsAuthorized(ClaimsPrincipal principal) => true;
        public PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal) => new(false, scopedWorkspaces.ToHashSet());
        public bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId) => scopedWorkspaces.Contains(workspaceId);
    }

    private sealed class RecordingProvisioningService : IWorkspaceProvisioningService
    {
        public IReadOnlyList<Workspace> ListedWorkspaces { get; init; } = [];
        public WorkspaceAdminDetailDto? AdminDetail { get; init; }
        public PlatformWorkspaceScope? LastListScope { get; private set; }
        public PlatformWorkspaceScope? LastDetailScope { get; private set; }
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) { LastListScope = workspaceScope; return Task.FromResult(ListedWorkspaces); }
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) { LastDetailScope = workspaceScope; return Task.FromResult(workspaceId == WorkspaceId ? AdminDetail : null); }
        public bool CreateConflict { get; init; }
        public bool MembershipUnavailable { get; init; }
        public int AddMembershipCalls { get; private set; }
        public Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => Task.FromResult(CreateConflict ? WorkspaceProvisioningResult.Conflict() : WorkspaceProvisioningResult.Created(new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName, ConnectionStatus = "awaiting_invitation" }));
        public Task<WorkspaceOnboardingProvisioningResult> OnboardAsync(Guid tenantId, string displayName, string adminUpn, string adminDisplayName, AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.FromResult(WorkspaceOnboardingProvisioningResult.Created(new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName }, "http://localhost/invitations/token", DateTimeOffset.UtcNow.AddDays(7)));
        public Task<PersistenceWorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) { if (MembershipUnavailable) throw new WorkspaceProvisioningUnavailableException("database unavailable"); AddMembershipCalls++; return Task.FromResult(new PersistenceWorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator }); }
        public Task<PersistenceWorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) => AddMembershipAsync(workspaceId, tenantObjectId, email, platformRole, isAteaOperator, cancellationToken);
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
    }

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
