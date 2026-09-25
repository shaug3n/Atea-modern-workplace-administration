using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit.Sdk;
using PlatformWorkspaceScope = Atea.UnifiedWorkplace.Api.Authorization.PlatformWorkspaceScope;

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
                ["Onboarding:ConsentSigningKey"] = "dGVzdC1zaWduaW5nLWtleS13aXRoLWF0LWxlYXN0LTMyLWNoYXJhY3RlcnM=",
                ["PlatformAuthorization:AdminObjectIds:0"] = AdminObjectId.ToString(),
                [$"PlatformAuthorization:AdminWorkspaceScopes:{AdminObjectId}:0"] = WorkspaceId.ToString()
            }));
            builder.ConfigureServices(services => services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = AccessTestAuthenticationHandler.Scheme;
                options.DefaultChallengeScheme = AccessTestAuthenticationHandler.Scheme;
            }).AddScheme<AuthenticationSchemeOptions, AccessTestAuthenticationHandler>(AccessTestAuthenticationHandler.Scheme, _ => { }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthorization(options =>
                {
                    options.FallbackPolicy = new AuthorizationPolicyBuilder(AccessTestAuthenticationHandler.Scheme).RequireAuthenticatedUser().Build();
                    options.AddPolicy("PlatformAdminPolicy", policy => policy
                        .AddAuthenticationSchemes(AccessTestAuthenticationHandler.Scheme)
                        .RequireAuthenticatedUser()
                        .RequireClaim("oid"));
                });
                services.RemoveAll<Atea.UnifiedWorkplace.Api.Authorization.IPlatformAuthorization>();
                services.AddSingleton<Atea.UnifiedWorkplace.Api.Authorization.IPlatformAuthorization, WorkspaceAccessTestPlatformAuthorization>();
            });
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
    public async Task Legacy_invitation_applies_its_role_to_an_existing_workspace_membership()
    {
        await SeedWorkspaceAsync();
        const string nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
            db.PlatformInvitations.Add(new PlatformInvitation
            {
                Id = Guid.NewGuid(), WorkspaceId = WorkspaceId, Email = "member@example.com", DisplayName = "Member",
                Role = "customer_admin", NonceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant(),
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        using var invitee = AuthenticatedClient(TenantId, MemberObjectId, "member@example.com", "Member");
        (await invitee.PostAsync($"/api/invitations/{nonce}/redeem", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await verifyDb.WorkspaceMemberships.SingleAsync(x => x.WorkspaceId == WorkspaceId && x.TenantObjectId == MemberObjectId)).PlatformRole.Should().Be("customer_admin");
        (await verifyDb.AuditEvents.AnyAsync(x => x.Action == "workspace.invitation.redeemed" && x.TargetType == "invitation")).Should().BeTrue();
    }

    [Fact]
    public async Task Null_role_returns_validation_error_instead_of_server_error()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var response = await admin.PatchAsJsonAsync($"/api/workspaces/current/access/memberships/{Guid.NewGuid()}", new { role = (string?)null });
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData(null, "Invitee", "member")]
    [InlineData("invitee@example.com", null, "member")]
    [InlineData("invitee@example.com", "Invitee", null)]
    public async Task Null_customer_invitation_fields_return_validation_error(string? email, string? displayName, string? role)
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);

        var response = await admin.PostAsJsonAsync("/api/workspaces/current/access/invitations", new { email, displayName, role });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Atea_onboarding_creates_workspace_and_first_admin_invitation_atomically()
    {
        await ResetDatabaseAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);

        var response = await admin.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantId = TenantId,
            displayName = "New customer",
            adminUpn = "owner@customer.example",
            adminDisplayName = "Workspace Owner"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        body.RootElement.GetProperty("workspace").GetProperty("tenantId").GetGuid().Should().Be(TenantId);
        body.RootElement.GetProperty("workspace").GetProperty("displayName").GetString().Should().Be("New customer");
        var invitationUrl = body.RootElement.GetProperty("invitationUrl").GetString()!;
        invitationUrl.Should().StartWith("http://localhost:5173/invitations/");
        var expiresAt = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        expiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(6)).And.BeBefore(DateTimeOffset.UtcNow.AddDays(8));

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        var workspace = await db.Workspaces.SingleAsync(x => x.TenantId == TenantId);
        var invitation = await db.PlatformInvitations.SingleAsync(x => x.WorkspaceId == workspace.Id);
        invitation.Email.Should().Be("owner@customer.example");
        invitation.DisplayName.Should().Be("Workspace Owner");
        invitation.Role.Should().Be("customer_admin");
        invitation.ExpiresAt.Should().Be(expiresAt);
        invitation.NonceHash.Should().NotContain(invitationUrl.Split('/').Last());
    }

    [Fact]
    public async Task Atea_onboarding_rejects_duplicate_tenant_without_adding_an_invitation()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);

        var response = await admin.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantId = TenantId,
            displayName = "Duplicate",
            adminUpn = "owner@customer.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await db.Workspaces.CountAsync(x => x.TenantId == TenantId)).Should().Be(1);
        (await db.PlatformInvitations.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Atea_onboarding_rejects_requests_from_non_platform_admins()
    {
        await ResetDatabaseAsync();
        using var customer = AuthenticatedClient(TenantId, MemberObjectId);

        var response = await customer.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantId = TenantId,
            displayName = "Unauthorized",
            adminUpn = "owner@customer.example"
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Failed_first_invitation_persistence_rolls_back_workspace_creation()
    {
        await ResetDatabaseAsync();
        var workspace = new Workspace
        {
            Id = Guid.NewGuid(), TenantId = TenantId, DisplayName = "Atomic customer",
            ConnectionStatus = "awaiting_invitation", CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
        };
        var invitation = new PlatformInvitation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspace.Id, Email = "admin@example.com", DisplayName = "Admin",
            Role = "customer_admin", NonceHash = new string('a', 129), ExpiresAt = DateTimeOffset.UtcNow.AddDays(7), CreatedAt = DateTimeOffset.UtcNow
        };
        var audit = new AuditEvent
        {
            WorkspaceId = workspace.Id, TenantId = TenantId, ActorTenantId = TenantId, ActorObjectId = AdminObjectId,
            Action = "workspace.onboarded", TargetType = "workspace", Outcome = "success", Timestamp = DateTimeOffset.UtcNow
        };

        await using (var writeScope = factory.Services.CreateAsyncScope())
        {
            var repository = writeScope.ServiceProvider.GetRequiredService<IWorkspaceProvisioningRepository>();
            await Assert.ThrowsAsync<WorkspaceProvisioningDatabaseException>(() => repository.CreateWithInvitationAsync(workspace, invitation, audit));
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await verifyDb.Workspaces.AnyAsync(x => x.Id == workspace.Id)).Should().BeFalse();
        (await verifyDb.PlatformInvitations.AnyAsync(x => x.Id == invitation.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task Atea_workspace_invitation_reissue_preserves_invitee_and_role_and_invalidates_old_link()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var original = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");
        var response = await admin.PostAsync($"/api/platform/workspaces/{WorkspaceId}/invitations/{original.Id}/reissue", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var replacementUrl = body.RootElement.GetProperty("invitationUrl").GetString()!;
        var replacementId = body.RootElement.GetProperty("id").GetGuid();
        var replacementExpiry = body.RootElement.GetProperty("expiresAt").GetDateTimeOffset();
        replacementExpiry.Should().BeAfter(DateTimeOffset.UtcNow.AddDays(6)).And.BeBefore(DateTimeOffset.UtcNow.AddDays(8));
        replacementUrl.Should().NotBe(original.Url);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        var prior = await db.PlatformInvitations.SingleAsync(x => x.Id == original.Id);
        var replacement = await db.PlatformInvitations.SingleAsync(x => x.Id == replacementId);
        prior.RevokedAt.Should().NotBeNull();
        replacement.Email.Should().Be("person@example.com");
        replacement.DisplayName.Should().Be("Person");
        replacement.Role.Should().Be("member");
        replacement.ExpiresAt.Should().Be(replacementExpiry);
    }

    [Fact]
    public async Task Atea_workspace_invitation_recovery_is_denied_outside_platform_workspace_scope()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var invitation = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");

        var response = await admin.PostAsync($"/api/platform/workspaces/{Guid.NewGuid()}/invitations/{invitation.Id}/reissue", null);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await admin.DeleteAsync($"/api/platform/workspaces/{Guid.NewGuid()}/invitations/{invitation.Id}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Atea_workspace_invitation_recovery_audits_without_plaintext_link_and_revokes_transactionally()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);
        var invitation = await CreateInvitationAsync(admin, "person@example.com", "Person", "member");
        var reissue = await admin.PostAsync($"/api/platform/workspaces/{WorkspaceId}/invitations/{invitation.Id}/reissue", null);
        reissue.StatusCode.Should().Be(HttpStatusCode.OK);
        using var body = JsonDocument.Parse(await reissue.Content.ReadAsStringAsync());
        var newId = body.RootElement.GetProperty("id").GetGuid();
        var newUrl = body.RootElement.GetProperty("invitationUrl").GetString()!;
        var delete = await admin.DeleteAsync($"/api/platform/workspaces/{WorkspaceId}/invitations/{newId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);

        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await db.PlatformInvitations.SingleAsync(x => x.Id == newId)).RevokedAt.Should().NotBeNull();
        var audit = string.Join("\n", await db.AuditEvents.Select(x => x.SafeMetadataJson).ToArrayAsync());
        audit.Should().Contain("invitation").And.NotContain(newUrl).And.NotContain(invitation.Nonce);
    }

    [Fact]
    public async Task Legacy_platform_membership_add_is_audited()
    {
        await SeedWorkspaceAsync();
        using var admin = AuthenticatedClient(TenantId, AdminObjectId);

        var response = await admin.PostAsJsonAsync($"/api/platform/workspaces/{WorkspaceId}/memberships", new
        {
            tenantObjectId = InviteeObjectId,
            email = "recovery@example.com",
            platformRole = "member",
            isAteaOperator = false
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await db.AuditEvents.AnyAsync(x => x.Action == "workspace.membership.added" && x.TenantId == TenantId)).Should().BeTrue();
    }

    [Fact]
    public async Task Legacy_platform_membership_add_rolls_back_when_audit_write_fails()
    {
        await SeedWorkspaceAsync();
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
            var repository = new WorkspaceProvisioningRepository(db, new FailingWorkspaceAuditWriter());
            var audit = new AuditEvent
            {
                WorkspaceId = WorkspaceId, TenantId = TenantId, ActorTenantId = TenantId, ActorObjectId = AdminObjectId,
                Action = "workspace.membership.added", TargetType = "membership", Outcome = "success", Timestamp = DateTimeOffset.UtcNow
            };
            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.AddMembershipAsync(WorkspaceId, InviteeObjectId, "recovery@example.com", "member", false, audit));
        }
        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await verifyDb.WorkspaceMemberships.AnyAsync(x => x.WorkspaceId == WorkspaceId && x.TenantObjectId == InviteeObjectId)).Should().BeFalse();
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

    [Fact]
    public async Task Membership_change_rolls_back_when_audit_write_fails()
    {
        await SeedWorkspaceAsync();
        Guid memberMembershipId;
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
            memberMembershipId = await db.WorkspaceMemberships.Where(x => x.WorkspaceId == WorkspaceId && x.TenantObjectId == MemberObjectId).Select(x => x.Id).SingleAsync();
            var repository = new WorkspaceAccessRepository(db, new FailingAuditWriter());
            var audit = new AuditEvent
            {
                WorkspaceId = WorkspaceId, TenantId = TenantId, ActorTenantId = TenantId, ActorObjectId = AdminObjectId,
                Action = "workspace.membership.role_changed", TargetType = "membership", TargetId = memberMembershipId.ToString("D"),
                Outcome = "success", Timestamp = DateTimeOffset.UtcNow, SafeMetadataJson = "{}"
            };

            await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ChangeRoleAsync(WorkspaceId, memberMembershipId, "customer_admin", audit));
        }

        await using var verifyScope = factory.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await verifyDb.WorkspaceMemberships.SingleAsync(x => x.Id == memberMembershipId)).PlatformRole.Should().Be("member");
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

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("Injected audit failure.");
    }

    private sealed class WorkspaceAccessTestPlatformAuthorization : Atea.UnifiedWorkplace.Api.Authorization.IPlatformAuthorization
    {
        public bool IsAuthorized(ClaimsPrincipal principal) => Guid.TryParse(principal.FindFirst("oid")?.Value, out var id) && id == AdminObjectId;
        public PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal) => IsAuthorized(principal) ? new(false, new HashSet<Guid> { WorkspaceId }) : new(false, new HashSet<Guid>());
        public bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId) => IsAuthorized(principal) && workspaceId == WorkspaceId;
    }

    private sealed class FailingWorkspaceAuditWriter : Atea.UnifiedWorkplace.Api.Infrastructure.Observability.IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => throw new InvalidOperationException("Injected audit failure.");
    }
}
