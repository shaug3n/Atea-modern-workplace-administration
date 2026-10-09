using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit.Sdk;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Audit;

public sealed class AuditEndpointTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        try { await postgres.StartAsync(); }
        catch (DockerUnavailableException exception) { throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}"); }
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Audit_endpoint_returns_workspace_scoped_redacted_events_and_correlation_header()
    {
        await SeedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "safe-correlation-123");

        var response = await client.GetAsync("/api/audit/events?action=users.update&outcome=succeeded&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-ID").Should().ContainSingle().Which.Should().Be("safe-correlation-123");
        body.Should().Contain("users.update");
        body.Should().Contain("graph-request-safe");
        body.Should().Contain("pim-request-safe");
        body.Should().Contain("safe-correlation-123");
        body.Should().NotContain("other-workspace");
        body.Should().NotContain("access_token");
        body.Should().NotContain("Temporary-Password");
        body.Should().Contain("Microsoft 365 audit logs remain authoritative");
    }

    [Fact]
    public async Task Invalid_correlation_header_is_replaced_with_safe_server_value()
    {
        await SeedAsync();
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        client.DefaultRequestHeaders.Add("X-Correlation-ID", "Bearer secret token with spaces");

        var response = await client.GetAsync("/api/audit/events");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.GetValues("X-Correlation-ID").Single().Should().MatchRegex("^[A-Za-z0-9_.-]{16,100}$");
        response.Headers.GetValues("X-Correlation-ID").Single().Should().NotContain("secret");
    }

    [Fact]
    public async Task Audit_endpoint_denies_members_without_audit_view_capability()
    {
        await SeedAsync();
        using var factory = CreateFactory(platformRole: "member");
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/audit/events");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain("\"capability\":\"audit.view\"");
        body.Should().NotContain("other-workspace");
    }

    [Fact]
    public async Task Audit_endpoint_returns_opaque_filter_bound_continuation_token()
    {
        await SeedAsync();
        await AddAuditEventAsync(DateTimeOffset.Parse("2026-09-21T07:59:00Z"), "users.update", "succeeded");
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var first = await client.GetAsync("/api/audit/events?action=users.update&outcome=succeeded&pageSize=1");
        var firstBody = await first.Content.ReadAsStringAsync();
        var firstResult = System.Text.Json.JsonDocument.Parse(firstBody).RootElement;
        var token = firstResult.GetProperty("nextContinuationToken").GetString();

        token.Should().NotBeNullOrWhiteSpace();
        token.Should().NotContain(WorkspaceId.ToString());
        token.Should().NotContain("users.update");

        var second = await client.GetAsync($"/api/audit/events?action=users.update&outcome=succeeded&pageSize=1&continuationToken={Uri.EscapeDataString(token!)}");
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        (await second.Content.ReadAsStringAsync()).Should().Contain("2026-09-21T07:59:00");

        var mismatchedFilter = await client.GetAsync($"/api/audit/events?action=users.disable&outcome=succeeded&pageSize=1&continuationToken={Uri.EscapeDataString(token!)}");
        mismatchedFilter.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await mismatchedFilter.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"invalid_continuation_token\"}");
    }

    private WebApplicationFactory<Program> CreateFactory(string platformRole = "owner") =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                    ["AzureAd:ClientId"] = "test-client-id",
                    ["ConnectionStrings:WorkplaceDb"] = postgres.GetConnectionString()
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(platformRole));
                services.RemoveAll<DbContextOptions<WorkplaceDbContext>>();
                services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
            });
        });

    private async Task AddAuditEventAsync(DateTimeOffset timestamp, string action, string outcome)
    {
        await using var db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options);
        await db.AuditEvents.AddAsync(new AuditEvent
        {
            WorkspaceId = WorkspaceId,
            TenantId = TenantId,
            ActorTenantId = TenantId,
            ActorObjectId = ObjectId,
            Action = action,
            TargetType = "user",
            TargetId = "user-older",
            Outcome = outcome,
            Timestamp = timestamp,
            SafeMetadataJson = "{}"
        });
        await db.SaveChangesAsync();
    }

    private async Task SeedAsync()
    {
        await using var db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options);
        await db.Database.MigrateAsync();
        db.AuditEvents.RemoveRange(db.AuditEvents);
        await db.SaveChangesAsync();

        await db.AuditEvents.AddRangeAsync(
            new AuditEvent
            {
                WorkspaceId = WorkspaceId,
                TenantId = TenantId,
                ActorTenantId = TenantId,
                ActorObjectId = ObjectId,
                Action = "users.update",
                TargetType = "user",
                TargetId = "user-1",
                Outcome = "succeeded",
                Timestamp = DateTimeOffset.Parse("2026-09-21T08:00:00Z"),
                CorrelationId = "safe-correlation-123",
                GraphRequestId = "graph-request-safe",
                PimRequestId = "pim-request-safe",
                SafeMetadataJson = """{"after":{"department":"Operations"},"token":"[REDACTED]"}"""
            },
            new AuditEvent
            {
                WorkspaceId = Guid.NewGuid(),
                TenantId = Guid.NewGuid(),
                ActorTenantId = Guid.NewGuid(),
                ActorObjectId = Guid.NewGuid(),
                Action = "other-workspace",
                TargetType = "user",
                TargetId = "user-2",
                Outcome = "failed",
                Timestamp = DateTimeOffset.Parse("2026-09-21T08:01:00Z"),
                CorrelationId = "other-correlation",
                SafeMetadataJson = """{"temporaryPassword":"Temporary-Password-123"}"""
            });
        await db.SaveChangesAsync();
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class FixtureMembershipReader(string platformRole) : IWorkspaceMembershipReader
    {
        public Task<AuthorizationWorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<AuthorizationWorkspaceMembership?>(new AuthorizationWorkspaceMembership(WorkspaceId, "customer-workspace", platformRole));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
