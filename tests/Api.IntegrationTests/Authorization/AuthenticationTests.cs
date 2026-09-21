using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Microsoft.Extensions.Configuration;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Authorization;

public sealed class AuthenticationTests
{
    [Fact]
    public async Task UnauthenticatedApiCallReturnsStructured401()
    {
        using var client = CreateFactory().CreateClient();
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "problem-correlation-123");
        var response = await client.GetAsync("/api/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var problem = await AssertProblemAsync(response, ApiProblemCode.AuthenticationRequired);
        problem.RootElement.GetProperty("title").GetString().Should().Be("Authentication required");
        problem.RootElement.GetProperty("correlationId").GetString().Should().Be("problem-correlation-123");
    }

    [Fact]
    public async Task AuthenticatedUserWithoutMembershipReturnsStructured403()
    {
        using var client = CreateFactory().CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        var response = await client.GetAsync("/api/session");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await AssertProblemAsync(response, ApiProblemCode.AuthorizationDenied);
        problem.RootElement.GetProperty("detail").GetString().Should().Be("The signed-in user is not assigned to this workspace.");
    }

    [Fact]
    public async Task AuthenticatedMemberCanReadSessionWithoutTokenContents()
    {
        using var client = CreateFactory(includeMembership: true).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        var response = await client.GetAsync("/api/session");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("customer-workspace");
        body.Should().NotContain("access_token");
        body.Should().NotContain("refresh_token");
        body.Should().NotContain("client_secret");
        body.Should().NotContain("Authorization: Bearer");
    }

    [Fact]
    public async Task AuthenticatedIdentityWithoutTenantClaimReturnsStructured401()
    {
        using var client = CreateFactory(malformedIdentity: true).CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        var response = await client.GetAsync("/api/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemAsync(response, ApiProblemCode.AuthenticationRequired);
    }

    [Fact]
    public async Task UnauthenticatedApiEndpointWithoutExplicitMetadataReturnsStructured401()
    {
        using var client = CreateFactory().CreateClient();
        var response = await client.GetAsync("/api/ping");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        await AssertProblemAsync(response, ApiProblemCode.AuthenticationRequired);
    }

    private static WebApplicationFactory<Program> CreateFactory(bool includeMembership = false, bool malformedIdentity = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(includeMembership));
                services.AddSingleton(typeof(TestAuthenticationMode), malformedIdentity ? TestAuthenticationMode.Malformed : TestAuthenticationMode.Valid);
            });
        });

    private static async Task<JsonDocument> AssertProblemAsync(HttpResponseMessage response, string code)
    {
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        response.Headers.GetValues("X-Correlation-ID").Should().NotBeEmpty();
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("access_token");
        body.Should().NotContain("raw Graph");
        var document = JsonDocument.Parse(body);
        document.RootElement.GetProperty("code").GetString().Should().Be(code);
        document.RootElement.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
        return document;
    }

    private sealed class FixtureMembershipReader(bool includeMembership) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(includeMembership ? new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "customer-workspace") : null);
    }

    private enum TestAuthenticationMode { Valid, Malformed }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestAuthenticationMode mode)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new Claim("oid", "22222222-2222-2222-2222-222222222222"),
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("userType", "Member"),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            if (mode == TestAuthenticationMode.Valid) claims.Add(new Claim("tid", "11111111-1111-1111-1111-111111111111"));
            var identity = new ClaimsIdentity(claims, Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }
}
