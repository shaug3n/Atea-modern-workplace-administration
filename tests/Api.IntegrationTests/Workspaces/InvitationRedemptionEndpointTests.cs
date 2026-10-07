using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
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

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class InvitationRedemptionEndpointTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private const string ConsentSigningKey = "MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDA=";

    [Fact]
    public async Task Redemption_requires_bearer_authentication_and_does_not_use_the_admin_cookie()
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository);
        using var client = factory.CreateClient();

        var unauthenticated = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);
        unauthenticated.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var cookieClient = factory.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = true });
        (await cookieClient.PostAsJsonAsync("/api/admin-auth/login", new { username = "admin", password = "secret" })).StatusCode.Should().Be(HttpStatusCode.OK);
        var adminOnly = await cookieClient.PostAsync($"/api/invitations/{nonce}/redeem", null);
        adminOnly.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        repository.RedeemCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("", "22222222-2222-2222-2222-222222222222")]
    [InlineData("not-a-guid", "22222222-2222-2222-2222-222222222222")]
    [InlineData("11111111-1111-1111-1111-111111111111", "")]
    [InlineData("11111111-1111-1111-1111-111111111111", "not-a-guid")]
    public async Task Redemption_rejects_missing_or_invalid_tid_and_oid(string tenant, string objectId)
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository);
        using var client = AuthenticatedClient(factory, tenant, objectId, "customer@example.com");

        var response = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        repository.RedeemCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("99999999-9999-9999-9999-999999999999", "22222222-2222-2222-2222-222222222222", "customer@example.com")]
    [InlineData("11111111-1111-1111-1111-111111111111", "99999999-9999-9999-9999-999999999999", "other@example.com")]
    [InlineData("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "other@example.com")]
    public async Task Redemption_rejects_wrong_tenant_object_or_email(string tenant, string objectId, string email)
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository);
        using var client = AuthenticatedClient(factory, tenant, objectId, email);

        var response = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"invitation_invalid_or_expired\"}");
    }

    [Fact]
    public async Task Redemption_rejects_same_tenant_different_object_id_when_invitation_approves_another_object()
    {
        using var factory = CreateFactory(out var repository);
        var approvedObjectId = ObjectId;
        var wrongObjectId = Guid.Parse("99999999-9999-9999-9999-999999999999");
        var nonce = SeedInvitation(repository, approvedTenantObjectId: approvedObjectId);
        using var client = AuthenticatedClient(factory, TenantId.ToString(), wrongObjectId.ToString(), "customer@example.com");

        var response = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"invitation_invalid_or_expired\"}");
        repository.Invitation!.RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task Redemption_returns_only_safe_workspace_metadata_and_rejects_reuse()
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository);
        using var client = AuthenticatedClient(factory, TenantId.ToString(), ObjectId.ToString(), "customer@example.com");

        var first = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);
        var body = await first.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(["status", "workspaceId", "workspaceName", "nextStep"]);
        json.RootElement.GetProperty("status").GetString().Should().Be("consent_required");
        json.RootElement.GetProperty("workspaceId").GetGuid().Should().Be(WorkspaceId);
        json.RootElement.GetProperty("workspaceName").GetString().Should().Be("Customer workspace");
        json.RootElement.GetProperty("nextStep").GetString().Should().Be("/overview");
        body.Should().NotContain(nonce).And.NotContain("nonce").And.NotContain("token");

        var second = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.RedeemCalls.Should().Be(2);
    }

    [Fact]
    public async Task Redemption_rejects_expired_invitations_without_claiming_them()
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var client = AuthenticatedClient(factory, TenantId.ToString(), ObjectId.ToString(), "customer@example.com");

        var response = await client.PostAsync($"/api/invitations/{nonce}/redeem", null);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        repository.Invitation!.RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task Challenge_bound_replay_recovers_only_for_the_recorded_redeemer()
    {
        using var factory = CreateFactory(out var repository);
        var nonce = SeedInvitation(repository);
        var challenge = new ConsentChallengeService(Convert.FromBase64String(ConsentSigningKey))
            .CreateInvitation(WorkspaceId, TenantId, repository.Invitation!.Id, repository.Invitation.ExpiresAt);
        using var sameIdentity = AuthenticatedClient(factory, TenantId.ToString(), ObjectId.ToString(), "customer@example.com");
        using var anotherIdentity = AuthenticatedClient(factory, TenantId.ToString(), "99999999-9999-9999-9999-999999999999", "customer@example.com");

        var first = await sameIdentity.PostAsJsonAsync($"/api/invitations/{nonce}/redeem", new { challenge = challenge.Challenge });
        var nonceOnlyReplay = await sameIdentity.PostAsync($"/api/invitations/{nonce}/redeem", null);
        var anotherIdentityReplay = await anotherIdentity.PostAsJsonAsync($"/api/invitations/{nonce}/redeem", new { challenge = challenge.Challenge });
        var recovered = await sameIdentity.PostAsJsonAsync($"/api/invitations/{nonce}/redeem", new { challenge = challenge.Challenge });

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        nonceOnlyReplay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        anotherIdentityReplay.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        recovered.StatusCode.Should().Be(HttpStatusCode.OK);
        repository.RedemptionAuditCount.Should().Be(1);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory, string tenant, string objectId, string email)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "test-token");
        client.DefaultRequestHeaders.Add("X-Test-Tenant", tenant);
        client.DefaultRequestHeaders.Add("X-Test-Object", objectId);
        client.DefaultRequestHeaders.Add("X-Test-Email", email);
        return client;
    }

    private static string SeedInvitation(TestInvitationRepository repository, DateTimeOffset? expiresAt = null, Guid? approvedTenantObjectId = null)
    {
        const string nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        repository.Invitation = new PlatformInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = WorkspaceId,
            Email = "customer@example.com",
            DisplayName = "Customer Admin",
            ApprovedTenantObjectId = approvedTenantObjectId,
            NonceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant(),
            ExpiresAt = expiresAt ?? DateTimeOffset.UtcNow.AddHours(1),
            CreatedAt = DateTimeOffset.UtcNow
        };
        repository.Workspace = new Workspace { Id = WorkspaceId, TenantId = TenantId, DisplayName = "Customer workspace", ConnectionStatus = "awaiting_invitation" };
        return nonce;
    }

    private static WebApplicationFactory<Program> CreateFactory(out TestInvitationRepository repository)
    {
        var fixtureRepository = new TestInvitationRepository();
        repository = fixtureRepository;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id",
                ["AteaAdmin:LocalDevelopment:Enabled"] = "true",
                ["AteaAdmin:LocalDevelopment:Username"] = "admin",
                ["AteaAdmin:LocalDevelopment:Password"] = "secret",
                ["AteaAdmin:LocalDevelopment:ObjectId"] = ObjectId.ToString(),
                ["AteaAdmin:LocalDevelopment:DisplayName"] = "Local Atea Admin",
                ["AteaAdmin:LocalDevelopment:AllowAllWorkspaces"] = "true",
                ["Onboarding:ConsentSigningKey"] = ConsentSigningKey
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IInvitationRepository>();
                services.AddSingleton<IInvitationRepository>(fixtureRepository);
            });
        });
    }

    private sealed class TestInvitationRepository : IInvitationRepository
    {
        public PlatformInvitation? Invitation { get; set; }
        public Workspace? Workspace { get; set; }
        public int RedeemCalls { get; private set; }
        public int RedemptionAuditCount { get; private set; }

        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default) => Task.FromResult(invitation);

        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default)
            => RedeemAsync(nonceHash, tenantId, tenantObjectId, email, displayName, invitationStateHash: null, cancellationToken);

        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, string? invitationStateHash, CancellationToken cancellationToken = default)
        {
            RedeemCalls++;
            if (Invitation is null || Workspace is null || Invitation.NonceHash != nonceHash || Invitation.ExpiresAt <= DateTimeOffset.UtcNow || Invitation.RevokedAt is not null || Workspace.TenantId != tenantId) return Task.FromResult<InvitationRedemption?>(null);
            if (Invitation.RedeemedAt is not null)
            {
                if (invitationStateHash is null ||
                    invitationStateHash != RecordedChallengeHash ||
                    Invitation.RedeemedByTenantObjectId != tenantObjectId)
                    return Task.FromResult<InvitationRedemption?>(null);
                return Task.FromResult<InvitationRedemption?>(CreateRedemption(tenantObjectId, email));
            }
            if (Invitation.ApprovedTenantObjectId != tenantObjectId && !(Invitation.ApprovedTenantObjectId is null && string.Equals(Invitation.Email, email, StringComparison.OrdinalIgnoreCase)))
                return Task.FromResult<InvitationRedemption?>(null);
            if (invitationStateHash is not null) RecordedChallengeHash = invitationStateHash;
            Invitation.RedeemedAt = DateTimeOffset.UtcNow;
            Invitation.RedeemedByTenantObjectId = tenantObjectId;
            RedemptionAuditCount++;
            Workspace.ConnectionStatus = "consent_required";
            return Task.FromResult<InvitationRedemption?>(CreateRedemption(tenantObjectId, email));
        }

        private InvitationRedemption CreateRedemption(Guid tenantObjectId, string? email) =>
            new(Workspace!, new WorkspaceMembership { WorkspaceId = Workspace!.Id, TenantObjectId = tenantObjectId, Email = email!, PlatformRole = "customer_admin" });

        private string? RecordedChallengeHash { get; set; }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "InvitationTest";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new List<Claim>();
            AddClaimIfPresent(claims, "tid", Request.Headers["X-Test-Tenant"].FirstOrDefault());
            AddClaimIfPresent(claims, "oid", Request.Headers["X-Test-Object"].FirstOrDefault());
            AddClaimIfPresent(claims, "preferred_username", Request.Headers["X-Test-Email"].FirstOrDefault());
            claims.Add(new Claim("name", "Customer Admin"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }

        private static void AddClaimIfPresent(ICollection<Claim> claims, string type, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value)) claims.Add(new Claim(type, value));
        }
    }
}
