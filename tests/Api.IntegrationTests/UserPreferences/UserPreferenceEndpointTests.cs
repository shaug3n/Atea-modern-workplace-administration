using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Features.UserPreferences;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.UserPreferences;

public sealed class UserPreferenceEndpointTests
{
    [Fact]
    public async Task Theme_preference_does_not_require_workspace_membership()
    {
        var service = new RecordingThemePreferenceService();
        using var client = CreateFactory(service).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/user-preferences/theme");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Be("{\"theme\":null}");
        service.Reads.Should().Contain((TenantA, UserA));
    }

    [Fact]
    public async Task Theme_preference_rejects_values_other_than_light_or_dark()
    {
        var service = new RecordingThemePreferenceService();
        using var client = CreateFactory(service).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.PutAsync(
            "/api/user-preferences/theme",
            new StringContent("{\"theme\":\"blue\"}", Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        service.Writes.Should().BeEmpty();
    }

    [Fact]
    public async Task Theme_preference_is_keyed_by_verified_tenant_and_user_claims()
    {
        var service = new RecordingThemePreferenceService();
        using var clientA = CreateFactory(service, TenantA, UserA).CreateClient();
        using var clientB = CreateFactory(service, TenantB, UserB).CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var stored = await clientA.PutAsync(
            "/api/user-preferences/theme",
            new StringContent("{\"theme\":\"dark\"}", Encoding.UTF8, "application/json"));
        var otherUser = await clientB.GetAsync("/api/user-preferences/theme");

        stored.StatusCode.Should().Be(HttpStatusCode.OK);
        (await otherUser.Content.ReadAsStringAsync()).Should().Be("{\"theme\":null}");
        service.Writes.Should().Contain((TenantA, UserA, "dark"));
        service.Reads.Should().Contain((TenantB, UserB));
    }

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TenantB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserB = Guid.Parse("44444444-4444-4444-4444-444444444444");

    private static WebApplicationFactory<Program> CreateFactory(RecordingThemePreferenceService service, Guid? tenantId = null, Guid? objectId = null) =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.AddSingleton(new TestIdentity(tenantId ?? TenantA, objectId ?? UserA));
                services.RemoveAll<IThemePreferenceService>();
                services.AddSingleton<IThemePreferenceService>(service);
            });
        });

    private sealed class RecordingThemePreferenceService : IThemePreferenceService
    {
        private readonly Dictionary<(Guid TenantId, Guid UserObjectId), string> themes = [];
        public List<(Guid TenantId, Guid UserObjectId)> Reads { get; } = [];
        public List<(Guid TenantId, Guid UserObjectId, string Theme)> Writes { get; } = [];

        public Task<string?> GetThemeAsync(Guid tenantId, Guid userObjectId, CancellationToken cancellationToken = default)
        {
            Reads.Add((tenantId, userObjectId));
            return Task.FromResult(themes.GetValueOrDefault((tenantId, userObjectId)));
        }

        public Task SetThemeAsync(Guid tenantId, Guid userObjectId, string theme, CancellationToken cancellationToken = default)
        {
            Writes.Add((tenantId, userObjectId, theme));
            themes[(tenantId, userObjectId)] = theme;
            return Task.CompletedTask;
        }
    }

    private sealed record TestIdentity(Guid TenantId, Guid ObjectId);

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestIdentity identity)
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
                new Claim("oid", identity.ObjectId.ToString()),
                new Claim("tid", identity.TenantId.ToString()),
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
