using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Feedback;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit.Sdk;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Feedback;

public sealed class FeedbackEndpointTests : IAsyncLifetime
{
    private const string CreateUrl = "/api/feedback/submissions";
    private const string ListUrl = "/api/feedback/submissions";
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TenantB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorkspaceA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid WorkspaceB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync();
            await using var db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>()
                .UseNpgsql(postgres.GetConnectionString())
                .Options);
            await db.Database.MigrateAsync();
            var nowUtc = DateTimeOffset.UtcNow;
            db.Workspaces.AddRange(
                new Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.Workspace
                {
                    Id = WorkspaceA,
                    TenantId = TenantA,
                    DisplayName = "Workspace A",
                    ConnectionStatus = "active",
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                },
                new Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.Workspace
                {
                    Id = WorkspaceB,
                    TenantId = TenantB,
                    DisplayName = "Workspace B",
                    ConnectionStatus = "active",
                    CreatedAt = nowUtc,
                    UpdatedAt = nowUtc
                });
            await db.SaveChangesAsync();
        }
        catch (DockerUnavailableException exception)
        {
            throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}");
        }
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Feedback_endpoints_require_authentication_workspace_membership_capability_and_enabled_assigned_module()
    {
        using var unauthenticatedFactory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var unauthenticatedClient = unauthenticatedFactory.CreateClient();
        (await unauthenticatedClient.GetAsync(ListUrl)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await PostAsync(unauthenticatedClient, "unauthenticated-key", "Bug", "Subject", "Message"))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        using var noMembershipFactory = CreateFactory(new TestIdentity(TenantA, UserA, null));
        using var noMembershipClient = AuthorizedClient(noMembershipFactory);
        (await noMembershipClient.GetAsync(ListUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var unassignedFactory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA, ["users"]));
        using var unassignedClient = AuthorizedClient(unassignedFactory);
        (await unassignedClient.GetAsync(ListUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var disabledFactory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA), enabledModules: []);
        using var disabledClient = AuthorizedClient(disabledFactory);
        (await disabledClient.GetAsync(ListUrl)).StatusCode.Should().Be(HttpStatusCode.Forbidden);

        using var enabledFactory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        var endpoints = enabledFactory.Services.GetRequiredService<IEnumerable<Microsoft.AspNetCore.Routing.EndpointDataSource>>()
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(candidate => candidate.RoutePattern.RawText == ListUrl)
            .ToArray();
        endpoints.Should().HaveCount(2);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
            endpoint.Metadata.GetMetadata<RequireCapabilityAttribute>()!.Capability.Should().Be(Capability.FeedbackSubmit);
        }
    }

    [Fact]
    public async Task Submission_identity_comes_from_verified_context_not_request_body()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);
        var response = await PostAsync(client, "identity-test-key", "Bug", "Private subject", "Private message", new
        {
            workspaceId = WorkspaceB,
            submitterObjectId = UserB,
            ownerObjectId = UserB
        });
        var responseBody = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created, responseBody);
        var receipt = JsonSerializer.Deserialize<FeedbackSubmissionReceipt>(responseBody, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        receipt.Should().NotBeNull();
        (receipt!.ExpiresAt - receipt.CreatedAt).Should().Be(TimeSpan.FromDays(90));
        receipt.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await db.Database.SqlQuery<Guid>($"SELECT \"WorkspaceId\" AS \"Value\" FROM \"FeedbackSubmissions\"").SingleAsync()).Should().Be(WorkspaceA);
        (await db.Database.SqlQuery<Guid>($"SELECT \"SubmitterObjectId\" AS \"Value\" FROM \"FeedbackSubmissions\"").SingleAsync()).Should().Be(UserA);
    }

    [Fact]
    public async Task Post_returns_created_and_identical_retry_replays_same_receipt()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);

        var created = await PostAsync(client, "retry-key-private", "Improvement", "Private subject", "Private message");
        var replayed = await PostAsync(client, "retry-key-private", "Improvement", "Private subject", "Private message");
        var createdBody = await created.Content.ReadAsStringAsync();
        var replayedBody = await replayed.Content.ReadAsStringAsync();

        created.StatusCode.Should().Be(HttpStatusCode.Created);
        replayed.StatusCode.Should().Be(HttpStatusCode.OK);
        replayedBody.Should().Be(createdBody);
        createdBody.Should().NotContain("Private subject").And.NotContain("Private message").And.NotContain("retry-key-private");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await SubmissionCountAsync(db)).Should().Be(1);
        var audits = await db.AuditEvents.ToListAsync();
        audits.Should().ContainSingle();
        audits[0].Action.Should().Be("feedback.submitted");
        var auditJson = JsonSerializer.Serialize(audits[0]);
        auditJson.Should().NotContain("Private subject").And.NotContain("Private message").And.NotContain("retry-key-private");

        var listed = await client.GetAsync(ListUrl);
        listed.StatusCode.Should().Be(HttpStatusCode.OK);
        var listBody = await listed.Content.ReadAsStringAsync();
        listBody.Should().Contain("Private subject").And.Contain("Private message");
        (await db.AuditEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Changed_payload_with_same_retry_key_returns_409()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);

        (await PostAsync(client, "retry-key-private", "Bug", "Private subject", "Private message"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var response = await PostAsync(client, "retry-key-private", "Bug", "Changed subject", "Private message");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Should().Contain("idempotency_key_reused");
        body.Should().NotContain("Changed subject").And.NotContain("Private message").And.NotContain("retry-key-private");
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
        (await SubmissionCountAsync(db)).Should().Be(1);
        (await db.AuditEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Post_rejects_missing_and_invalid_requests_without_echoing_feedback()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);

        var emptyRequest = await PostRawAsync(client, "null", "null-request-key");
        var invalidRequest = await PostRawAsync(client, JsonSerializer.Serialize(new
        {
            category = "bug",
            subject = "Secret fixture subject",
            message = "Secret fixture message"
        }), "invalid-request-key");
        var missingKeyRequest = await PostRawAsync(client, JsonSerializer.Serialize(new
        {
            category = "General",
            subject = "Subject",
            message = "Message"
        }), null);

        emptyRequest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        invalidRequest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missingKeyRequest.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var invalidBody = await invalidRequest.Content.ReadAsStringAsync();
        invalidBody.Should().NotContain("Secret fixture subject").And.NotContain("Secret fixture message");
        await using var scope = factory.Services.CreateAsyncScope();
        (await SubmissionCountAsync(scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>())).Should().Be(0);
    }

    [Fact]
    public async Task List_returns_only_current_submitter_in_current_workspace()
    {
        using var creatorFactory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var otherSubmitterFactory = CreateFactory(new TestIdentity(TenantA, UserB, WorkspaceA));
        using var otherWorkspaceFactory = CreateFactory(new TestIdentity(TenantB, UserA, WorkspaceB));
        using var creatorClient = AuthorizedClient(creatorFactory);
        using var otherSubmitterClient = AuthorizedClient(otherSubmitterFactory);
        using var otherWorkspaceClient = AuthorizedClient(otherWorkspaceFactory);

        (await PostAsync(creatorClient, "key-a", "Bug", "A subject", "A message")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(otherSubmitterClient, "key-b", "General", "B subject", "B message")).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(otherWorkspaceClient, "key-c", "Improvement", "C subject", "C message")).StatusCode.Should().Be(HttpStatusCode.Created);

        var creatorList = await creatorClient.GetStringAsync(ListUrl);
        var otherSubmitterList = await otherSubmitterClient.GetStringAsync(ListUrl);
        var otherWorkspaceList = await otherWorkspaceClient.GetStringAsync(ListUrl);

        creatorList.Should().Contain("A subject").And.NotContain("B subject").And.NotContain("C subject");
        otherSubmitterList.Should().Contain("B subject").And.NotContain("A subject").And.NotContain("C subject");
        otherWorkspaceList.Should().Contain("C subject").And.NotContain("A subject").And.NotContain("B subject");
    }

    [Fact]
    public async Task Expired_pending_retry_returns_conflict_and_expired_rows_are_hidden()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);
        (await PostAsync(client, "expired-key", "Bug", "Expired subject", "Expired message"))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
            await db.Database.ExecuteSqlRawAsync("UPDATE \"FeedbackSubmissions\" SET \"ExpiresAt\" = NOW() - INTERVAL '1 second'");
        }

        var retry = await PostAsync(client, "expired-key", "Bug", "Expired subject", "Expired message");
        var list = await client.GetStringAsync(ListUrl);
        var retryBody = await retry.Content.ReadAsStringAsync();

        retry.StatusCode.Should().Be(HttpStatusCode.Conflict);
        retryBody.Should().Contain("idempotency_key_expired").And.NotContain("Expired subject").And.NotContain("Expired message").And.NotContain("expired-key");
        list.Should().NotContain("Expired subject").And.NotContain("Expired message");
        await using var auditScope = factory.Services.CreateAsyncScope();
        (await auditScope.ServiceProvider.GetRequiredService<WorkplaceDbContext>().AuditEvents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task List_uses_fixed_twenty_item_keyset_pages_and_rejects_malformed_cursors()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA, WorkspaceA));
        using var client = AuthorizedClient(factory);
        for (var index = 0; index < 21; index++)
        {
            (await PostAsync(client, $"key-{index}", "General", $"Subject {index}", $"Message {index}"))
                .StatusCode.Should().Be(HttpStatusCode.Created);
        }
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>();
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE \"FeedbackSubmissions\" SET \"CreatedAt\" = TIMESTAMPTZ '2026-10-08 00:00:00+00'");
        }

        var firstPage = await client.GetFromJsonAsync<FeedbackPageResponse>(ListUrl);
        firstPage!.Items.Should().HaveCount(20);
        firstPage.NextCursor.Should().NotBeNullOrEmpty();
        var secondPage = await client.GetFromJsonAsync<FeedbackPageResponse>($"{ListUrl}?cursor={Uri.EscapeDataString(firstPage.NextCursor!)}");
        secondPage!.Items.Should().ContainSingle();
        secondPage.NextCursor.Should().BeNull();

        var malformed = await client.GetAsync($"{ListUrl}?cursor=not-a-valid-cursor");
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await malformed.Content.ReadAsStringAsync()).Should().Contain("validation_failed");
    }

    private static HttpClient AuthorizedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private async Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string key,
        string category,
        string subject,
        string message,
        object? extraFields = null)
    {
        var payload = new Dictionary<string, object?>
        {
            ["category"] = category,
            ["subject"] = subject,
            ["message"] = message
        };
        if (extraFields is not null)
        {
            foreach (var item in JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(extraFields))!)
                payload[item.Key] = item.Value;
        }
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateUrl)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PostRawAsync(HttpClient client, string payload, string? key)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, CreateUrl)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        if (key is not null)
            request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static Task<int> SubmissionCountAsync(WorkplaceDbContext db) =>
        db.Database.SqlQuery<int>($"SELECT COUNT(*)::int AS \"Value\" FROM \"FeedbackSubmissions\"").SingleAsync();

    private WebApplicationFactory<Program> CreateFactory(TestIdentity identity, IReadOnlyCollection<string>? enabledModules = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id",
                ["ConnectionStrings:WorkplaceDb"] = postgres.GetConnectionString()
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.AddSingleton(identity);
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader, FixtureMembershipReader>();
                services.RemoveAll<IWorkspaceSettingsService>();
                services.AddSingleton<IWorkspaceSettingsService>(new FixtureWorkspaceSettingsService(enabledModules ?? ["feedback"]));
                services.RemoveAll<DbContextOptions<WorkplaceDbContext>>();
                services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
            });
        });

    private sealed record TestIdentity(Guid TenantId, Guid ObjectId, Guid? WorkspaceId, IReadOnlyCollection<string>? ModuleKeys = null);

    private sealed class FixtureMembershipReader(TestIdentity identity) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(identity.TenantId == tenantId && identity.ObjectId == objectId && identity.WorkspaceId is Guid workspaceId
                ? new WorkspaceMembership(workspaceId, "Test workspace", ModuleKeys: identity.ModuleKeys ?? ["feedback"])
                : null);
    }

    private sealed class FixtureWorkspaceSettingsService(IReadOnlyCollection<string> enabledModules) : IWorkspaceSettingsService
    {
        public Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new WorkspaceConfiguration(enabledModules, [], new Dictionary<string, string>(), string.Empty, "light"));

        public Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        TestIdentity identity)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[]
            {
                new Claim("oid", identity.ObjectId.ToString()),
                new Claim("tid", identity.TenantId.ToString()),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
