using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class InvitationConsentEndpointTests
{
    private const string Nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static readonly Guid InvitationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task Preview_exposes_only_the_safe_shape_for_a_live_invitation()
    {
        using var factory = CreateFactory("customer_admin");
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/invitations/{Nonce}/preview");
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.RootElement.EnumerateObject().Select(property => property.Name)
            .Should().Equal("workspaceName", "expiresAt", "flow", "permissionScopes");
        json.RootElement.GetProperty("flow").GetString().Should().Be("consent_first");
        body.Should().NotContain(TenantId.ToString()).And.NotContain(WorkspaceId.ToString())
            .And.NotContain(InvitationId.ToString()).And.NotContain("customer@example.com").And.NotContain("moduleKeys");
    }

    [Fact]
    public async Task Start_returns_a_signed_challenge_for_the_customer_spa_and_api_resource()
    {
        using var factory = CreateFactory("workspace_owner", out var challenges);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");

        var response = await client.PostAsJsonAsync($"/api/invitations/{Nonce}/consent/start", new { });
        var result = await response.Content.ReadFromJsonAsync<InvitationConsentStartResponse>();
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(result!.AuthorizationUrl).Query);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        query["client_id"].ToString().Should().Be("customer-spa");
        query["scope"].ToString().Should().Be("api://customer-api/.default");
        challenges.Record.Should().NotBeNull();
        challenges.Record!.StateHash.Should().Be(ConsentChallengeService.HashState(result.Challenge));
    }

    [Fact]
    public async Task Resume_is_anonymous_and_does_not_consume_the_challenge()
    {
        using var factory = CreateFactory("customer_admin", out var challenges);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");
        var start = await client.PostAsJsonAsync($"/api/invitations/{Nonce}/consent/start", new { });
        var started = await start.Content.ReadFromJsonAsync<InvitationConsentStartResponse>();

        var response = await client.PostAsJsonAsync($"/api/invitations/{Nonce}/consent/resume", new
        {
            state = started!.Challenge,
            tenant = TenantId
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"ready_to_sign_in\"").And.Contain(TenantId.ToString());
        challenges.ConsumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Member_invitation_preview_stays_sign_in_only()
    {
        using var factory = CreateFactory("member");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("Origin", "http://localhost");

        var preview = await client.GetFromJsonAsync<JsonElement>($"/api/invitations/{Nonce}/preview");
        var start = await client.PostAsJsonAsync($"/api/invitations/{Nonce}/consent/start", new { });

        preview.GetProperty("flow").GetString().Should().Be("sign_in");
        start.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await start.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"invitation_consent_not_available\"}");
    }

    [Fact]
    public async Task Preview_is_anonymous_and_returns_uncacheable_unavailable_response()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/invitations/invalid/preview");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("\"error\":\"invitation_unavailable\"");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
        response.Headers.GetValues("Referrer-Policy").Should().ContainSingle().Which.Should().Be("no-referrer");
    }

    [Fact]
    public async Task Anonymous_consent_post_rejects_cross_origin_requests()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/invalid/consent/start")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "https://attacker.example");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().NotContain("attacker.example");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_consent_post_with_trailing_slash_rejects_cross_origin_requests()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/invalid/consent/start/")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "https://attacker.example");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Existing_workspace_routes_remain_authenticated()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/workspaces/current");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Anonymous_preview_is_limited_to_twenty_requests_per_client_ip()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? final = null;
        for (var requestNumber = 0; requestNumber < 21; requestNumber++)
        {
            final?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, "/api/invitations/invalid/preview");
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{requestNumber + 1}");
            final = await client.SendAsync(request);
        }

        using (final)
        {
            final!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            final.Headers.RetryAfter.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Invitation_endpoints_share_a_two_hundred_request_instance_cap()
    {
        using var factory = CreateFactory(null, out _, "127.0.0.1,::1");
        using var client = factory.CreateClient();

        HttpResponseMessage? final = null;
        for (var requestNumber = 0; requestNumber < 201; requestNumber++)
        {
            final?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/invitations/invalid/preview");
            request.Headers.Add("X-Forwarded-For", $"198.51.100.{requestNumber + 1}");
            final = await client.SendAsync(request);
        }

        using (final)
        {
            final!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            final.Headers.RetryAfter.Should().NotBeNull();
        }
    }

    [Fact]
    public async Task Resume_rejects_oversized_callback_bodies_before_parsing_state()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/invalid/consent/resume")
        {
            Content = new StringContent($"{{\"state\":\"{new string('x', 9000)}\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task Resume_with_trailing_slash_rejects_oversized_callback_bodies_before_parsing_state()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/invalid/consent/resume/")
        {
            Content = new StringContent($"{{\"state\":\"{new string('x', 9000)}\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Origin", "http://localhost");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
    }

    [Fact]
    public async Task Start_has_a_five_request_per_ip_limit()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        HttpResponseMessage? final = null;
        for (var requestNumber = 0; requestNumber < 6; requestNumber++)
        {
            final?.Dispose();
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/invitations/invalid/consent/start")
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Origin", "http://localhost");
            final = await client.SendAsync(request);
        }

        using (final)
        {
            final!.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
            final.Headers.RetryAfter.Should().NotBeNull();
        }
    }

    private static WebApplicationFactory<Program> CreateFactory() => CreateFactory(null, out _, string.Empty);

    private static WebApplicationFactory<Program> CreateFactory(string? role) => CreateFactory(role, out _, string.Empty);

    private static WebApplicationFactory<Program> CreateFactory(string? role, out FixtureConsentChallengeRepository challenges) =>
        CreateFactory(role, out challenges, string.Empty);

    private static WebApplicationFactory<Program> CreateFactory(string? role, out FixtureConsentChallengeRepository challenges, string trustedProxyAddresses)
    {
        var lookup = role is null ? null : new InvitationLookup(
            InvitationId, WorkspaceId, "Customer workspace", TenantId, role, DateTimeOffset.UtcNow.AddHours(1), false, false);
        var readRepository = new FixtureInvitationReadRepository(lookup);
        var fixtureChallenges = new FixtureConsentChallengeRepository();
        challenges = fixtureChallenges;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Onboarding:PublicBaseUrl"] = "http://localhost",
                ["Onboarding:ConsentRedirectUri"] = "http://localhost/onboarding/consent/callback",
                ["Onboarding:ConsentSigningKey"] = Convert.ToBase64String(Enumerable.Repeat((byte)1, 32).ToArray()),
                ["Onboarding:CustomerClientId"] = "customer-spa",
                ["Onboarding:ApiApplicationIdUri"] = "api://customer-api",
                ["Onboarding:TrustedProxyAddresses"] = trustedProxyAddresses
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IInvitationReadRepository>();
                services.AddSingleton<IInvitationReadRepository>(readRepository);
                services.RemoveAll<IConsentChallengeRepository>();
                services.AddSingleton<IConsentChallengeRepository>(fixtureChallenges);
            });
        });
    }

    private sealed class FixtureInvitationReadRepository(InvitationLookup? lookup) : IInvitationReadRepository
    {
        public Task<InvitationLookup?> FindByNonceHashAsync(string nonceHash, CancellationToken cancellationToken = default)
        {
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce))).ToLowerInvariant();
            return Task.FromResult(nonceHash == expected ? lookup : null);
        }
    }

    private sealed class FixtureConsentChallengeRepository : IConsentChallengeRepository
    {
        public InvitationConsentChallengeRecord? Record { get; private set; }
        public int ConsumeCalls { get; private set; }

        public Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task CreateInvitationAsync(InvitationConsentChallengeRecord challenge, CancellationToken cancellationToken = default) { Record = challenge; return Task.CompletedTask; }
        public Task<InvitationConsentChallengeRecord?> FindInvitationAsync(string stateHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record?.StateHash == stateHash ? Record : null);
        public Task<bool> TryConsumeInvitationAsync(string stateHash, Guid invitationId, Guid workspaceId, Guid tenantId, Guid redeemerObjectId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            return Task.FromResult(false);
        }
    }
}
