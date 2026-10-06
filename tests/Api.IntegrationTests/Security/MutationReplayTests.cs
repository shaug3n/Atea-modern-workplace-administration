using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
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

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Security;

public sealed class MutationReplayTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync();
            await SeedDatabaseAsync();
        }
        catch (DockerUnavailableException exception)
        {
            throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}");
        }
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Replaying_the_same_key_returns_the_stored_result_without_a_second_graph_mutation()
    {
        var commands = new RecordingUserCommands();
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA), commands);
        using var client = AuthenticatedClient(factory);

        var first = await SendUpdateAsync(client, "replay-key", "Ada Updated");
        var replay = await SendUpdateAsync(client, "replay-key", "Ada Updated");
        var changedPayload = await SendUpdateAsync(client, "replay-key", "Grace Hopper");
        var replayBody = await replay.Content.ReadAsStringAsync();
        var changedBody = await changedPayload.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBody.Should().Contain("\"replayed\":true");
        changedPayload.StatusCode.Should().Be(HttpStatusCode.Conflict);
        changedBody.Should().Contain("idempotency_key_reused");
        commands.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task The_same_key_is_not_replayed_across_tenants_or_workspace_actors()
    {
        var commands = new RecordingUserCommands();
        using var factoryA = CreateFactory(new TestIdentity(TenantA, UserA), commands);
        using var factoryB = CreateFactory(new TestIdentity(TenantB, UserB), commands);
        using var clientA = AuthenticatedClient(factoryA);
        using var clientB = AuthenticatedClient(factoryB);

        var tenantAResponse = await SendUpdateAsync(clientA, "shared-key", "Tenant A update");
        var tenantBResponse = await SendUpdateAsync(clientB, "shared-key", "Tenant A update");
        var tenantBBody = await tenantBResponse.Content.ReadAsStringAsync();

        tenantAResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        tenantBResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        tenantBBody.Should().NotContain("\"replayed\":true");
        commands.UpdateCalls.Should().Be(2);
    }

    private async Task SeedDatabaseAsync()
    {
        await using var db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options);
        await db.Database.MigrateAsync();
    }

    private static async Task<HttpResponseMessage> SendUpdateAsync(HttpClient client, string key, string displayName)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/user-1")
        {
            Content = new StringContent($"{{\"displayName\":\"{displayName}\"}}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private WebApplicationFactory<Program> CreateFactory(TestIdentity identity, RecordingUserCommands commands) =>
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
                services.AddSingleton<IWorkspaceMembershipReader, TenantMembershipReader>();
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader, StaticCapabilityReader>();
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton<IUserDirectoryReader, RecordingDirectoryReader>();
                services.RemoveAll<IUserLifecycleCommands>();
                services.AddSingleton<IUserLifecycleCommands>(commands);
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter, NoOpAuditWriter>();
                services.RemoveAll<DbContextOptions<WorkplaceDbContext>>();
                services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
            });
        });

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TenantB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorkspaceA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid WorkspaceB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private sealed record TestIdentity(Guid TenantId, Guid ObjectId);

    private sealed class TenantMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default)
        {
            var membership = tenantId == TenantA && objectId == UserA
                ? new WorkspaceMembership(WorkspaceA, "Tenant A Workplace", "workspace-manager", ModuleKeys: ["users", "devices", "licenses"])
                : tenantId == TenantB && objectId == UserB
                    ? new WorkspaceMembership(WorkspaceB, "Tenant B Workplace", "workspace-manager", ModuleKeys: ["users", "devices", "licenses"])
                    : null;
            return Task.FromResult(membership);
        }
    }

    private sealed class StaticCapabilityReader : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(
            GraphAuthorizationSnapshot.Available(
                "actor-1",
                ["Directory.Read.All", "User.Read.All", "User.ReadWrite.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Active, "/")]));
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) => Task.FromResult<UserDetails?>(
            new UserDetails(userObjectId, "Ada Lovelace", "ada@example.com", "ada@example.com", true, "Member"));
    }

    private sealed class RecordingUserCommands : IUserLifecycleCommands
    {
        public int UpdateCalls { get; private set; }

        public Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
        }

        public Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphOperationResult> ResetPasswordAsync(string userObjectId, TemporaryPasswordProfile passwordProfile, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, TestIdentity identity)
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
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
