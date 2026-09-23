using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit.Sdk;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class WorkspaceAccessEndpointTests : IAsyncLifetime
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid AdminObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid MemberObjectId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid InviteeObjectId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid OtherTenantId = Guid.Parse("99999999-9999-9999-9999-999999999999");
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().Build();
    private WebApplicationFactory<Program> factory = null!;

    public async Task InitializeAsync()
    {
        try { await postgres.StartAsync(); }
        catch (DockerUnavailableException exception) { throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}"); }

        factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:WorkplaceDb"] = postgres.GetConnectionString(),
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id",
                ["Onboarding:PublicBaseUrl"] = "http://localhost:5173/",
                ["Onboarding:ConsentSigningKey"] = "dGVzdC1zaWduaW5nLWtleS13aXRoLWF0LWxlYXN0LTMyLWNoYXJhY3RlcnM="
            }));
            builder.ConfigureServices(services => services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AccessTestAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = AccessTestAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, AccessTestAuthenticationHandler>(AccessTestAuthenticationHandler.Scheme, _ => { }));
            builder.ConfigureServices(services => services.AddAuthorization(options =>
                options.FallbackPolicy = new AuthorizationPolicyBuilder(AccessTestAuthenticationHandler.Scheme).RequireAuthenticatedUser().Build()));
        });
        _ = factory.Services;
        await ResetDatabaseAsync();
    }

    public async Task DisposeAsync()
    {
        factory.Dispose();
        await postgres.DisposeAsync();
    }

    [Theory]
    [InlineData("member", "member")]
    [InlineData("customer_admin", "customer_admin")]
    public async Task Invitation_redemption_binds_claimed_identity_and_invited_workspace_role(string role, string expectedRole)
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var invite = await admin.PostAsJsonAsync("/api/workspaces/current/access/invitations", new { email = "new.user@example.com", displayName = "New User", role });
        invite.StatusCode.Should().Be(HttpStatusCode.Created);
        using var inviteJson = JsonDocument.Parse(await invite.Content.ReadAsStringAsync());
        var nonce = inviteJson.RootElement.GetProperty("invitationUrl").GetString()!.TrimEnd('/').Split('/').Last();

        using var invitedUser = AuthenticatedClient(TenantId, InviteeObjectId, "new.user@example.com", "New User");
        (await invitedUser.PostAsync($"/api/invitations/{nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.OK);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        var membership = await db.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == WorkspaceId && x.TenantObjectId == InviteeObjectId);
        membership.PlatformRole.Should().Be(expectedRole);
        membership.Email.Should().Be("new.user@example.com");
    }

    [Fact]
    public async Task Reissuing_an_invitation_invalidates_the_previous_link_and_redeeming_invalidates_siblings()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var first = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");
        var second = await CreateInvitationAsync(admin, " PERSON@example.com ", "Person", "customer_admin");
        using var invitee = AuthenticatedClient(TenantId, MemberObjectId, "person@example.com", "Person");

        (await invitee.PostAsync($"/api/invitations/{first.Nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invitee.PostAsync($"/api/invitations/{second.Nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await invitee.PostAsync($"/api/invitations/{first.Nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Revoked_invitation_cannot_be_redeemed()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var invitation = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");
        (await admin.DeleteAsync($"/api/workspaces/current/access/invitations/{invitation.Id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        using var invitee = AuthenticatedClient(TenantId, MemberObjectId, "person@example.com", "Person");
        (await invitee.PostAsync($"/api/invitations/{invitation.Nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Last_customer_admin_cannot_be_downgraded_or_removed()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var response = await admin.GetAsync("/api/workspaces/current/access");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var membershipId = body.RootElement.GetProperty("memberships").EnumerateArray()
            .Single(x => x.GetProperty("tenantObjectId").GetGuid() == AdminObjectId).GetProperty("id").GetGuid();

        (await admin.PatchAsJsonAsync($"/api/workspaces/current/access/memberships/{membershipId}", new { role = "member" })).StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await admin.DeleteAsync($"/api/workspaces/current/access/memberships/{membershipId}")).StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Access_endpoints_require_membership_and_never_accept_caller_workspace_scope()
    {
        await SeedWorkspaceAsync();
        using var anonymous = factory.CreateClient();
        (await anonymous.GetAsync("/api/workspaces/current/access")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var otherTenant = AuthenticatedClient(OtherTenantId, AdminObjectId);
        (await otherTenant.GetAsync("/api/workspaces/current/access?workspaceId=" + WorkspaceId)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await otherTenant.PostAsJsonAsync("/api/workspaces/current/access/invitations", new { workspaceId = WorkspaceId, email = "attacker@example.com", displayName = "Attacker", role = "customer_admin" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Customer_admin_can_manage_settings_while_member_cannot()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        using var member = AuthenticatedClient(TenantId, MemberObjectId, "member@example.com", "Member");
        (await admin.PatchAsJsonAsync("/api/workspaces/current/settings", new { displayName = "Managed by customer" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await member.PatchAsJsonAsync("/api/workspaces/current/settings", new { displayName = "Unauthorized" })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Invitation_mutations_are_audited_without_plaintext_invitation_tokens()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var invitation = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        var auditJson = string.Join("\n", await db.AuditEvents.Select(x => x.SafeMetadataJson).ToArrayAsync());
        auditJson.Should().Contain("invitation").And.NotContain(invitation.Nonce).And.NotContain(invitation.Url);
    }

    private async Task SeedWorkspaceAsync()
    {
        await ResetDatabaseAsync();
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        var now = DateTimeOffset.UtcNow;
        db.Workspaces.Add(new Workspace { Id = WorkspaceId, TenantId = TenantId, DisplayName = "Customer", ConnectionStatus = "connected", CreatedAt = now, UpdatedAt = now });
        db.WorkspaceMemberships.AddRange(
            new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, TenantObjectId = AdminObjectId, Email = "admin@example.com", PlatformRole = "CustomerAdmin", CreatedAt = now },
            new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, TenantObjectId = MemberObjectId, Email = "member@example.com", PlatformRole = "member", CreatedAt = now });
        await db.SaveChangesAsync();
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlRawAsync("TRUNCATE TABLE \"AuditEvents\", \"ConsentChallenges\", \"IdempotencyRecords\", \"PlatformInvitations\", \"TenantConnections\", \"UserPreferences\", \"WorkspaceMemberships\", \"WorkspaceSettings\", \"Workspaces\" CASCADE");
    }

    private HttpClient AuthenticatedClient(Guid tenant, Guid objectId, string email = "admin@example.com", string name = "Admin")
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(AccessTestAuthenticationHandler.Scheme);
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Object", objectId.ToString());
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        client.DefaultRequestHeaders.Add("X-Test-Name", name);
        return client;
    }

    private static async Task<(Guid Id, string Url, string Nonce)> CreateInvitationAsync(HttpClient client, string email, string displayName, string role)
    {
        var response = await client.PostAsJsonAsync("/api/workspaces/current/access/invitations", new { email, displayName, role });
        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = json.RootElement.GetProperty("invitationUrl").GetString()!;
        return (json.RootElement.GetProperty("id").GetGuid(), url, url.TrimEnd('/').Split('/').Last());
    }

    public sealed class AccessTestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "WorkspaceAccessTest";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("tid", Request.Headers["X-Test-Tenant"].ToString()), new Claim("oid", Request.Headers["X-Test-Object"].ToString()),
                new Claim("aud", "api://atea-unified-workplace-api"), new Claim("preferred_username", Request.Headers["X-Test-Email"].ToString()),
                new Claim("name", Request.Headers["X-Test-Name"].ToString())
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
