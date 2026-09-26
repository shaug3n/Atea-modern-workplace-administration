using System.Net;
using System.Net.Http.Json;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
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

public sealed class CrossTenantAccessTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().Build();

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync();
        }
        catch (DockerUnavailableException exception)
        {
            throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}");
        }
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Client_supplied_tenant_selection_cannot_switch_the_verified_workspace()
    {
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA));
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/session?tenantId={TenantB}");
        request.Headers.Add("X-Tenant-Id", TenantB.ToString());

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain($"\"tenantId\":\"{TenantA}\"");
        body.Should().Contain($"\"id\":\"{WorkspaceA}\"");
        body.Should().NotContain(TenantB.ToString());
        body.Should().NotContain(WorkspaceB.ToString());
    }

    [Fact]
    public async Task Foreign_workspace_user_id_is_rejected_before_a_graph_mutation()
    {
        var commands = new RecordingUserCommands();
        var directory = new RecordingDirectoryReader
        {
            User = new UserDetails(
                "foreign-user",
                "Foreign User",
                "foreign@tenant-b.example",
                "foreign@tenant-b.example",
                true,
                "Member",
                DirectoryTenantId: TenantB)
        };
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA), AdminSnapshot, directory, commands);
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/foreign-user")
        {
            Content = new StringContent("{\"displayName\":\"Must not change\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", "foreign-user-mutation");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        // IUserDirectoryReader.GetAsync currently accepts only an object ID and UserDetails has no tenant/workspace identity.
        // This test is intentionally red until that contract carries verified resource scope; a foreign directory record must be 404.
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("user_not_found");
        commands.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Audit_reads_do_not_return_events_written_for_another_tenant_in_the_same_workspace_scope()
    {
        await SeedAuditEventsAsync();
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA), useDatabase: true);
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/audit/events");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("tenant-a-event");
        body.Should().NotContain("tenant-b-event");
        body.Should().NotContain("other-workspace-event");
    }

    [Fact]
    public async Task User_without_mutation_capability_cannot_update_a_verified_user()
    {
        var commands = new RecordingUserCommands();
        using var factory = CreateFactory(new TestIdentity(TenantA, UserA), ReaderSnapshot, commands: commands);
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Patch, "/api/users/user-1")
        {
            Content = new StringContent("{\"displayName\":\"Must not change\"}", Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", "unauthorized-mutation");

        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain("\"error\":\"capability_required\"");
        body.Should().Contain("\"state\":\"read_only\"");
        commands.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task Recovery_creation_grants_creator_only_until_a_second_operator_is_explicitly_granted()
    {
        using var creatorFactory = CreateFactory(new TestIdentity(TenantA, UserA), useDatabase: true, useHostedPlatformAuthorization: true);
        using var creator = AuthenticatedClient(creatorFactory);
        creator.DefaultRequestHeaders.Add("X-Test-Scopes", "platform.admin");

        var createResponse = await creator.PostAsJsonAsync("/api/platform/workspaces", new { tenantId = TenantB, displayName = "Tenant B Hosted" });
        createResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var workspace = await createResponse.Content.ReadFromJsonAsync<WorkspaceDto>();

        await using (var db = Database())
        {
            (await db.PlatformWorkspaceGrants.CountAsync(grant => grant.OperatorTenantId == TenantA && grant.OperatorObjectId == UserA && grant.WorkspaceId == workspace!.Id)).Should().Be(1);
        }

        using var secondOperatorFactory = CreateFactory(new TestIdentity(TenantA, UserB), useDatabase: true, useHostedPlatformAuthorization: true);
        using var secondOperator = AuthenticatedClient(secondOperatorFactory);
        secondOperator.DefaultRequestHeaders.Add("X-Test-Scopes", "platform.admin");

        var hidden = await secondOperator.GetAsync("/api/platform/workspaces");
        hidden.StatusCode.Should().Be(HttpStatusCode.OK);
        (await hidden.Content.ReadFromJsonAsync<List<WorkspaceDto>>()).Should().BeEmpty();

        await using (var db = Database())
        {
            db.PlatformWorkspaceGrants.Add(new PlatformWorkspaceGrant
            {
                OperatorTenantId = TenantA,
                OperatorObjectId = UserB,
                WorkspaceId = workspace!.Id,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await db.SaveChangesAsync();
        }

        var visibleAfterGrant = await secondOperator.GetAsync("/api/platform/workspaces");
        visibleAfterGrant.StatusCode.Should().Be(HttpStatusCode.OK);
        (await visibleAfterGrant.Content.ReadFromJsonAsync<List<WorkspaceDto>>()).Should().ContainSingle(item => item.Id == workspace!.Id);
    }

    [Fact]
    public async Task Guided_onboarding_grants_creator_and_rolls_back_workspace_invitation_and_grant_together()
    {
        var onboardingTenant = Guid.Parse("88888888-8888-8888-8888-888888888888");
        using var creatorFactory = CreateFactory(new TestIdentity(TenantA, UserA), useDatabase: true, useHostedPlatformAuthorization: true);
        using var creator = AuthenticatedClient(creatorFactory);
        creator.DefaultRequestHeaders.Add("X-Test-Scopes", "platform.admin");

        var onboarding = await creator.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantId = onboardingTenant,
            displayName = "Tenant B Onboarded",
            adminUpn = "owner@tenant-b.example",
            adminDisplayName = "Tenant B Owner"
        });
        onboarding.StatusCode.Should().Be(HttpStatusCode.Created);
        var workspace = await onboarding.Content.ReadFromJsonAsync<WorkspaceOnboardingResponse>();

        await using (var db = Database())
        {
            (await db.PlatformWorkspaceGrants.CountAsync(grant => grant.WorkspaceId == workspace!.Workspace.Id && grant.OperatorObjectId == UserA)).Should().Be(1);
            (await db.PlatformInvitations.CountAsync(invitation => invitation.WorkspaceId == workspace!.Workspace.Id)).Should().Be(1);
        }

        using var failingFactory = CreateFactory(new TestIdentity(TenantA, UserA), useDatabase: true, useHostedPlatformAuthorization: true, failAudit: true);
        using var failingClient = AuthenticatedClient(failingFactory);
        failingClient.DefaultRequestHeaders.Add("X-Test-Scopes", "platform.admin");
        var failedTenant = Guid.Parse("99999999-9999-9999-9999-999999999999");

        var failed = await failingClient.PostAsJsonAsync("/api/platform/workspaces/onboard", new
        {
            tenantId = failedTenant,
            displayName = "Must Roll Back",
            adminUpn = "rollback@example.com",
            adminDisplayName = "Rollback Owner"
        });
        failed.StatusCode.Should().Be(HttpStatusCode.InternalServerError);

        await using var verify = Database();
        (await verify.Workspaces.CountAsync(workspaceRow => workspaceRow.TenantId == failedTenant)).Should().Be(0);
        (await verify.PlatformInvitations.CountAsync(invitation => invitation.Workspace.TenantId == failedTenant)).Should().Be(0);
        (await verify.PlatformWorkspaceGrants.CountAsync(grant => grant.OperatorTenantId == TenantA && grant.OperatorObjectId == UserA && grant.Workspace.TenantId == failedTenant)).Should().Be(0);
    }

    private async Task SeedAuditEventsAsync()
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
                WorkspaceId = WorkspaceA,
                TenantId = TenantA,
                ActorTenantId = TenantA,
                ActorObjectId = UserA,
                Action = "tenant-a-event",
                TargetType = "user",
                TargetId = "user-a",
                Outcome = "succeeded",
                Timestamp = DateTimeOffset.Parse("2026-09-21T08:00:00Z"),
                SafeMetadataJson = "{}"
            },
            new AuditEvent
            {
                WorkspaceId = WorkspaceA,
                TenantId = TenantB,
                ActorTenantId = TenantB,
                ActorObjectId = UserB,
                Action = "tenant-b-event",
                TargetType = "user",
                TargetId = "user-b",
                Outcome = "succeeded",
                Timestamp = DateTimeOffset.Parse("2026-09-21T08:01:00Z"),
                SafeMetadataJson = "{}"
            },
            new AuditEvent
            {
                WorkspaceId = WorkspaceB,
                TenantId = TenantB,
                ActorTenantId = TenantB,
                ActorObjectId = UserB,
                Action = "other-workspace-event",
                TargetType = "user",
                TargetId = "user-b",
                Outcome = "succeeded",
                Timestamp = DateTimeOffset.Parse("2026-09-21T08:02:00Z"),
                SafeMetadataJson = "{}"
            });
        await db.SaveChangesAsync();
    }

    private WorkplaceDbContext Database() => new(new DbContextOptionsBuilder<WorkplaceDbContext>()
        .UseNpgsql(postgres.GetConnectionString())
        .Options);

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private WebApplicationFactory<Program> CreateFactory(
        TestIdentity identity,
        GraphAuthorizationSnapshot? snapshot = null,
        IUserDirectoryReader? directory = null,
        IUserLifecycleCommands? commands = null,
        bool useDatabase = false,
        bool useHostedPlatformAuthorization = false,
        bool failAudit = false) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            var configuration = new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id"
            };
            if (useDatabase)
            {
                configuration["ConnectionStrings:WorkplaceDb"] = postgres.GetConnectionString();
            }
            if (useHostedPlatformAuthorization)
            {
                configuration["PlatformAuthorization:HomeTenantId"] = TenantA.ToString();
                configuration["PlatformAuthorization:AdminObjectIds:0"] = UserA.ToString();
                configuration["PlatformAuthorization:AdminObjectIds:1"] = UserB.ToString();
            }

            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(configuration));
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
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticCapabilityReader(snapshot ?? AdminSnapshot));
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton(directory ?? new RecordingDirectoryReader());
                services.RemoveAll<IUserLifecycleCommands>();
                services.AddSingleton(commands ?? new RecordingUserCommands());
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService, MemoryIdempotencyService>();
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(failAudit ? new FailingAuditWriter() : new NoOpAuditWriter());
                if (useHostedPlatformAuthorization)
                {
                    services.AddAuthorization(options => options.AddPolicy("PlatformAdminPolicy", policy => policy
                        .AddAuthenticationSchemes(TestAuthenticationHandler.Scheme)
                        .RequireAuthenticatedUser()
                        .RequireClaim("oid")
                        .AddRequirements(new PlatformScopeRequirement("platform.admin"))));
                }
                if (useDatabase)
                {
                    services.RemoveAll<DbContextOptions<WorkplaceDbContext>>();
                    services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(postgres.GetConnectionString()));
                }
            });
        });

    private static readonly Guid TenantA = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid TenantB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid UserB = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorkspaceA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid WorkspaceB = Guid.Parse("66666666-6666-6666-6666-666666666666");

    private static readonly GraphAuthorizationSnapshot AdminSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All", "User.ReadWrite.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private static readonly GraphAuthorizationSnapshot ReaderSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed record TestIdentity(Guid TenantId, Guid ObjectId);

    private sealed class TenantMembershipReader : IWorkspaceMembershipReader
    {
        public Task<Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default)
        {
            var membership = tenantId == TenantA && objectId == UserA
                ? new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(WorkspaceA, "Tenant A Workplace", "workspace-manager", ModuleKeys: ["users", "devices", "licenses"])
                : tenantId == TenantB && objectId == UserB
                    ? new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(WorkspaceB, "Tenant B Workplace", "workspace-manager", ModuleKeys: ["users", "devices", "licenses"])
                    : null;
            return Task.FromResult(membership);
        }
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public UserDetails? User { get; set; } = new UserDetails("user-1", "Ada Lovelace", "ada@tenant-a.example", "ada@tenant-a.example", true, "Member");

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) => Task.FromResult(User);
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
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Injected audit failure.");
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
                new Claim("scp", Request.Headers["X-Test-Scopes"].ToString()),
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
