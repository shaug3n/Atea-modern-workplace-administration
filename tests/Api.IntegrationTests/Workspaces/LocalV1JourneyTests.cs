using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PersistenceWorkspaceMembership = Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.WorkspaceMembership;

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

    [Fact]
    public async Task Local_admin_to_customer_redemption_journey_uses_real_api_boundaries()
    {
        var state = new JourneyState();
        using var adminFactory = CreateAdminFactory(state);
        using var admin = adminFactory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });

        (await admin.PostAsJsonAsync("/api/admin-auth/login", new { username = "local-admin", password = "local-password" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await admin.GetAsync("/api/admin-auth/session")).StatusCode.Should().Be(HttpStatusCode.OK);

        var workspaceResponse = await admin.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = TenantId, displayName = "Local customer" });
        workspaceResponse.StatusCode.Should().Be(HttpStatusCode.Created, await workspaceResponse.Content.ReadAsStringAsync());
        var workspace = await workspaceResponse.Content.ReadFromJsonAsync<WorkspaceDto>();
        workspace.Should().NotBeNull();

        var membershipResponse = await admin.PostAsJsonAsync($"/api/platform/workspaces/{workspace!.Id}/memberships", new
        {
            tenantObjectId = CustomerObjectId,
            email = "customer.admin@example.test",
            platformRole = "CustomerAdmin",
            isAteaOperator = false
        });
        membershipResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        state.Membership.Should().NotBeNull();
        state.Membership!.WorkspaceId.Should().Be(workspace.Id);
        state.Membership.TenantObjectId.Should().Be(CustomerObjectId);
        state.Membership.Email.Should().Be("customer.admin@example.test");
        state.Membership.PlatformRole.Should().Be("CustomerAdmin");

        var invitationResponse = await admin.PostAsJsonAsync($"/api/platform/workspaces/{workspace.Id}/invitations", new
        {
            email = "customer.admin@example.test",
            displayName = "Customer Admin",
            expiresAt = DateTimeOffset.UtcNow.AddHours(1),
            approvedTenantObjectId = CustomerObjectId
        });
        invitationResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var invitation = await invitationResponse.Content.ReadFromJsonAsync<InvitationResponse>();
        invitation.Should().NotBeNull();
        invitation!.InvitationUrl.Should().StartWith("http://localhost:5173/invitations/");
        state.Invitation.Should().NotBeNull();

        (await admin.GetAsync("/api/session")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await admin.GetAsync("/api/users")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var customerFactory = CreateCustomerFactory(state);
        using var customer = customerFactory.CreateClient();
        customer.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("TestCustomer");

        var redemption = await customer.PostAsync($"/api/invitations/{invitation.InvitationUrl.Split('/').Last()}/redeem", null);

        redemption.StatusCode.Should().Be(HttpStatusCode.OK);
        var redeemed = await redemption.Content.ReadFromJsonAsync<InvitationRedemptionResponse>();
        redeemed!.Status.Should().Be("consent_required");
        redeemed.WorkspaceId.Should().Be(workspace.Id);
        redeemed.NextStep.Should().Be("/overview");
        state.Invitation!.RedeemedAt.Should().NotBeNull();
        state.RedeemedMembership.Should().BeSameAs(state.Membership);
        state.RedeemedMembership!.WorkspaceId.Should().Be(workspace.Id);
        state.RedeemedMembership.TenantObjectId.Should().Be(CustomerObjectId);
        state.RedeemedMembership.Email.Should().Be("customer.admin@example.test");
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

    private static WebApplicationFactory<Program> CreateAdminFactory(JourneyState state) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LocalConfiguration()));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IWorkspaceProvisioningService>();
            services.AddSingleton<IWorkspaceProvisioningService>(new JourneyProvisioningService(state));
            services.RemoveAll<IPlatformAuthorization>();
            services.AddSingleton<IPlatformAuthorization>(new JourneyPlatformAuthorization());
            services.RemoveAll<IInvitationRepository>();
            services.AddSingleton<IInvitationRepository>(state);
        });
    });

    private static WebApplicationFactory<Program> CreateCustomerFactory(JourneyState state) => new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
    {
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(LocalConfiguration()));
        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = CustomerAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = CustomerAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, CustomerAuthenticationHandler>(CustomerAuthenticationHandler.Scheme, _ => { });
            services.RemoveAll<IInvitationRepository>();
            services.AddSingleton<IInvitationRepository>(state);
        });
    });

    private static Dictionary<string, string?> LocalConfiguration() => new()
    {
        ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
        ["AzureAd:ClientId"] = "test-client-id",
        ["Onboarding:PublicBaseUrl"] = "http://localhost:5173",
        ["Onboarding:ConsentRedirectUri"] = "http://localhost:5173/onboarding/consent/callback",
        ["Onboarding:ConsentSigningKey"] = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=",
        ["AteaAdmin:LocalDevelopment:Enabled"] = "true",
        ["AteaAdmin:LocalDevelopment:Username"] = "local-admin",
        ["AteaAdmin:LocalDevelopment:Password"] = "local-password",
        ["AteaAdmin:LocalDevelopment:ObjectId"] = AdminObjectId.ToString(),
        ["AteaAdmin:LocalDevelopment:DisplayName"] = "Local Atea Admin",
        ["AteaAdmin:LocalDevelopment:AllowAllWorkspaces"] = "true"
    };

    private sealed record InvitationResponse(Guid InvitationId, string InvitationUrl, DateTimeOffset ExpiresAt);
    private sealed record InvitationRedemptionResponse(string Status, Guid WorkspaceId, string WorkspaceName, string NextStep);

    private sealed class JourneyState : IInvitationRepository
    {
        public Workspace? Workspace { get; set; }
        public PersistenceWorkspaceMembership? Membership { get; set; }
        public PersistenceWorkspaceMembership? RedeemedMembership { get; private set; }
        public PlatformInvitation? Invitation { get; set; }
        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default)
        {
            Invitation = invitation;
            invitation.Workspace = Workspace!;
            return Task.FromResult(invitation);
        }

        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default)
        {
            if (Invitation is null || Workspace is null || Membership is null || Invitation.NonceHash != nonceHash || Invitation.RedeemedAt is not null ||
                Invitation.ExpiresAt <= DateTimeOffset.UtcNow || Workspace.TenantId != tenantId || Invitation.ApprovedTenantObjectId != tenantObjectId ||
                !string.Equals(Invitation.Email, email, StringComparison.OrdinalIgnoreCase) || Membership.WorkspaceId != Workspace.Id ||
                Membership.TenantObjectId != tenantObjectId || !string.Equals(Membership.Email, email, StringComparison.OrdinalIgnoreCase)) return Task.FromResult<InvitationRedemption?>(null);

            Invitation.RedeemedAt = DateTimeOffset.UtcNow;
            Workspace.ConnectionStatus = "consent_required";
            RedeemedMembership = Membership;
            return Task.FromResult<InvitationRedemption?>(new InvitationRedemption(Workspace, Membership));
        }
    }

    private sealed class JourneyProvisioningService(JourneyState state) : IWorkspaceProvisioningService
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>(state.Workspace is null ? [] : [state.Workspace]);
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceAdminDetailDto?>(null);
        public Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default)
        {
            state.Workspace = new Workspace { Id = WorkspaceId, TenantId = tenantId, DisplayName = displayName, ConnectionStatus = "awaiting_invitation" };
            return Task.FromResult(WorkspaceProvisioningResult.Created(state.Workspace));
        }
        public Task<PersistenceWorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
        {
            state.Membership = new PersistenceWorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator };
            return Task.FromResult(state.Membership);
        }
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult(state.Workspace);
    }

    private sealed class JourneyPlatformAuthorization : IPlatformAuthorization
    {
        public bool IsAuthorized(ClaimsPrincipal principal) => principal.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true");
        public PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal) => new(true, new HashSet<Guid>());
        public bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId) => IsAuthorized(principal);
    }

    private sealed class CustomerAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "TestCustomer";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("tid", TenantId.ToString()),
                new Claim("oid", CustomerObjectId.ToString()),
                new Claim("preferred_username", "customer.admin@example.test"),
                new Claim("name", "Customer Admin")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }

    private static readonly Guid AdminObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerObjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
}
