using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class ConsentCallbackEndpointTests
{
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OtherTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    [Fact]
    public async Task Consent_start_uses_the_adminconsent_endpoint_and_persists_state()
    {
        using var factory = CreateFactory(out var repository);
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync("/api/workspaces/current/consent/start", null);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("/v2.0/adminconsent?").And.NotContain("/oauth2/v2.0/authorize").And.NotContain("response_type");
        repository.Records.Should().HaveCount(1);
    }

    [Fact]
    public async Task Valid_callback_is_consumed_once_without_transitioning_connection()
    {
        using var factory = CreateFactory(out var repository);
        using var client = AuthenticatedClient(factory);
        var challenge = await StartAsync(client);

        var response = await CompleteAsync(client, challenge.Challenge, TenantId);
        var second = await CompleteAsync(client, challenge.Challenge, TenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"status\":\"consent_received\"").And.NotContain("connected");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("\"valid\":false");
        repository.ConsumeCalls.Should().Be(2);
    }

    [Fact]
    public async Task Wrong_tenant_callback_is_rejected_without_consuming_state()
    {
        using var factory = CreateFactory(out var repository);
        using var client = AuthenticatedClient(factory);
        var challenge = await StartAsync(client);

        var response = await CompleteAsync(client, challenge.Challenge, OtherTenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"valid\":false");
        repository.ConsumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Expired_callback_is_rejected()
    {
        using var factory = CreateFactory(out var repository);
        using var client = AuthenticatedClient(factory);
        var service = new ConsentChallengeService(new byte[32]);
        var challenge = service.Create(WorkspaceId, TenantId, DateTimeOffset.UtcNow.AddMinutes(-1));
        repository.Add(WorkspaceId, TenantId, ConsentChallengeService.HashState(challenge.Challenge), challenge.CorrelationId, challenge.ExpiresAt);

        var response = await CompleteAsync(client, challenge.Challenge, TenantId);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"valid\":false");
    }

    [Fact]
    public async Task Provider_error_returns_safe_denial_without_echoing_description()
    {
        using var factory = CreateFactory(out _);
        using var client = AuthenticatedClient(factory);
        var challenge = await StartAsync(client);

        var response = await client.PostAsJsonAsync("/api/workspaces/current/consent/complete", new
        {
            state = challenge.Challenge,
            tenant = TenantId,
            errorCode = "access_denied"
        });
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"consent_denied\"").And.NotContain("access_denied").And.NotContain("error_description");
    }

    private static async Task<ConsentStartResponse> StartAsync(HttpClient client)
    {
        var response = await client.PostAsync("/api/workspaces/current/consent/start", null);
        return await response.Content.ReadFromJsonAsync<ConsentStartResponse>() ?? throw new InvalidOperationException("missing consent challenge");
    }

    private static Task<HttpResponseMessage> CompleteAsync(HttpClient client, string challenge, Guid tenant) =>
        client.PostAsJsonAsync("/api/workspaces/current/consent/complete", new { state = challenge, tenant });

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(out RecordingConsentChallengeRepository repository)
    {
        var configuredRepository = new RecordingConsentChallengeRepository();
        repository = configuredRepository;
        return new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id",
                ["Onboarding:ConsentSigningKey"] = Convert.ToBase64String(new byte[32]),
                ["Onboarding:ConsentRedirectUri"] = "http://localhost:5173/onboarding/consent/callback"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader());
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticSnapshotReader());
                services.RemoveAll<IConsentChallengeRepository>();
                services.AddSingleton<IConsentChallengeRepository>(configuredRepository);
            });
        });
    }

    private sealed class RecordingConsentChallengeRepository : IConsentChallengeRepository
    {
        public List<Record> Records { get; } = [];
        public int ConsumeCalls { get; private set; }

        public Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
        {
            Add(workspaceId, tenantId, stateHash, correlationId, expiresAt);
            return Task.CompletedTask;
        }

        public Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            var record = Records.SingleOrDefault(x => x.StateHash == stateHash && x.WorkspaceId == workspaceId && x.TenantId == tenantId && x.ConsumedAt is null && x.ExpiresAt > now);
            if (record is null) return Task.FromResult(false);
            record.ConsumedAt = now;
            return Task.FromResult(true);
        }

        public void Add(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt) => Records.Add(new Record(stateHash, workspaceId, tenantId, correlationId, expiresAt));
        public sealed record Record(string StateHash, Guid WorkspaceId, Guid TenantId, string CorrelationId, DateTimeOffset ExpiresAt) { public DateTimeOffset? ConsumedAt { get; set; } }
    }

    private sealed class StaticSnapshotReader : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"));
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "customer-workspace", "admin"));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim("oid", "22222222-2222-2222-2222-222222222222"), new Claim("tid", TenantId.ToString()), new Claim("preferred_username", "alex@example.com"), new Claim("name", "Alex Example"), new Claim("aud", "api://atea-unified-workplace-api") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
