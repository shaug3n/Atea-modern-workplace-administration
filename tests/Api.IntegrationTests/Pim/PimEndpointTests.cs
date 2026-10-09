using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Encodings.Web;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Pim;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Pim;

public sealed class PimEndpointTests
{
    [Fact]
    public async Task Get_user_pim_returns_safe_status_contract_without_raw_graph_payload()
    {
        using var factory = CreateFactory(new RecordingRoleAndPimReader
        {
            Eligibility =
            [
                Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)
            ]
        });
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/users/user-1/pim");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"eligible_inactive\"");
        body.Should().Contain("\"access\":{\"authorization\":{\"capability\":\"pim.activate\"");
        body.Should().Contain("\"state\":\"pim_activation_required\"");
        body.Should().Contain("\"items\":[");
        body.Should().Contain("\"nextStep\":\"Request activation in Microsoft Entra PIM\"");
        body.Should().NotContain("access_token");
        body.Should().NotContain("raw graph");
    }

    [Fact]
    public async Task Get_user_pim_returns_not_found_without_reading_pim_when_target_user_is_missing()
    {
        var roles = new RecordingRoleAndPimReader();
        using var factory = CreateFactory(roles, directory: new RecordingDirectoryReader { User = null });
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/users/removed-user/pim");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("\"error\":\"user_not_found\"");
        roles.RoleReads.Should().Be(0);
        roles.EligibilityReads.Should().Be(0);
    }

    [Fact]
    public async Task Get_user_pim_maps_target_verification_failure_to_safe_unavailable_state_without_reading_pim()
    {
        var roles = new RecordingRoleAndPimReader();
        using var factory = CreateFactory(
            roles,
            directory: new RecordingDirectoryReader { Error = new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(30)) });
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/users/user-1/pim");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"temporarily_unavailable\"");
        body.Should().Contain("\"access\":{\"authorization\":{\"capability\":\"pim.activate\"");
        body.Should().Contain("\"freshness\":\"stale\"");
        body.Should().Contain("\"category\":\"throttled\"");
        body.Should().Contain("\"retryAfterSeconds\":30");
        roles.RoleReads.Should().Be(0);
        roles.EligibilityReads.Should().Be(0);
    }

    [Fact]
    public async Task Activation_requires_idempotency_key()
    {
        using var factory = CreateFactory(new RecordingRoleAndPimReader());
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync("/api/pim/activations", Body("""{"roleTemplateId":"role","durationMinutes":60,"justification":"Need access","confirmed":true}"""));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("idempotency_key_required");
    }

    [Fact]
    public async Task Activation_requires_confirmation_before_graph_mutation()
    {
        var activations = new RecordingPimActivationCommands();
        using var factory = CreateFactory(new RecordingRoleAndPimReader(), activations);
        using var client = AuthenticatedClient(factory);

        var response = await client.SendAsync(PostActivation("""{"roleTemplateId":"e8611ab8-c189-46e8-94e1-60213ab1f814","durationMinutes":60,"justification":"Need access","confirmed":false}""", "confirm-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        body.Should().Contain("\"status\":\"policy_blocked\"");
        body.Should().Contain("\"error\":\"confirmation_required\"");
        activations.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Activation_uses_idempotency_and_reports_graph_pending_without_false_success()
    {
        var activations = new RecordingPimActivationCommands { Result = PimActivationGraphResult.Succeeded("request-1", "PendingApproval", "corr-1", "req-1") };
        using var factory = CreateFactory(new RecordingRoleAndPimReader
        {
            Eligibility =
            [
                Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)
            ]
        }, activations);
        using var client = AuthenticatedClient(factory);

        var first = await client.SendAsync(PostActivation("""{"roleTemplateId":"e8611ab8-c189-46e8-94e1-60213ab1f814","durationMinutes":60,"justification":"Need access","confirmed":true}""", "same-pim-key"));
        var replay = await client.SendAsync(PostActivation("""{"roleTemplateId":"e8611ab8-c189-46e8-94e1-60213ab1f814","durationMinutes":60,"justification":"Need access","confirmed":true}""", "same-pim-key"));
        var body = await replay.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"status\":\"activation_pending\"");
        body.Should().Contain("\"replayed\":true");
        body.Should().Contain("\"graphCorrelationId\":\"corr-1\"");
        body.Should().NotContain("\"status\":\"active\"");
        activations.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Activation_denies_missing_capability_without_graph_mutation()
    {
        var activations = new RecordingPimActivationCommands();
        using var factory = CreateFactory(
            new RecordingRoleAndPimReader(),
            activations,
            GraphAuthorizationSnapshot.Available(
                "actor-1",
                ["Directory.Read.All", "RoleManagement.Read.Directory"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]));
        using var client = AuthenticatedClient(factory);

        var response = await client.SendAsync(PostActivation("""{"roleTemplateId":"e8611ab8-c189-46e8-94e1-60213ab1f814","durationMinutes":60,"justification":"Need access","confirmed":true}""", "denied-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain("\"status\":\"not_authorized\"");
        body.Should().Contain("\"error\":\"capability_required\"");
        activations.Requests.Should().BeEmpty();
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static StringContent Body(string payload) => new(payload, Encoding.UTF8, "application/json");

    private static HttpRequestMessage PostActivation(string payload, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/pim/activations")
        {
            Content = Body(payload)
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingRoleAndPimReader roles,
        RecordingPimActivationCommands? activations = null,
        GraphAuthorizationSnapshot? snapshot = null,
        RecordingDirectoryReader? directory = null) =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                    ["AzureAd:ClientId"] = "test-client-id",
                    ["Users:ContinuationSigningKey"] = "0123456789abcdef0123456789abcdef"
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
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader());
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticCapabilityReader(snapshot ?? EligibleSnapshot));
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton<IUserDirectoryReader>(directory ?? new RecordingDirectoryReader());
                services.RemoveAll<IRoleAndPimReader>();
                services.AddSingleton<IRoleAndPimReader>(roles);
                services.RemoveAll<IPimActivationCommands>();
                services.AddSingleton<IPimActivationCommands>(activations ?? new RecordingPimActivationCommands());
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService>(new MemoryIdempotencyService());
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter, NoOpAuditWriter>();
            });
        });

    private static PimEligibility Eligibility(string id, string roleTemplateId, string displayName, string status) =>
        new(
            id,
            roleTemplateId,
            displayName,
            status,
            Capability.PimActivate,
            true,
            false,
            false,
            true,
            480,
            new PimActivationAction("request_activation", "/api/pim/activations", "POST", Capability.PimActivate, true))
        {
            RoleDefinitionId = "role-definition-id",
            DirectoryScopeId = "/"
        };

    private static readonly GraphAuthorizationSnapshot EligibleSnapshot = GraphAuthorizationSnapshot.Available(
        "22222222-2222-2222-2222-222222222222",
        ["Directory.Read.All", "RoleManagement.Read.Directory", "RoleManagement.ReadWrite.Directory"],
        [
            new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired))
        ]);

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public UserDetails? User { get; init; } = new(
            "user-1",
            "Ada Lovelace",
            "ada@example.com",
            "ada@example.com",
            true,
            "Member",
            "Ada",
            "Lovelace",
            "Principal Engineer",
            "Digital Workplace",
            "Oslo",
            "+47 22 00 00 00",
            "NO",
            false,
            "cloud",
            null);
        public GraphOperationResult? Error { get; init; }

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
        {
            if (Error is not null)
            {
                throw new GraphAdapterException(Error);
            }

            return Task.FromResult(User);
        }
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Contoso Workplace", "member", ModuleKeys: ["users", "devices", "licenses"]));
    }

    private sealed class RecordingRoleAndPimReader : IRoleAndPimReader
    {
        public IReadOnlyList<DirectoryRoleAssignment> Roles { get; init; } = [];
        public IReadOnlyList<PimEligibility> Eligibility { get; init; } = [];
        public int RoleReads { get; private set; }
        public int EligibilityReads { get; private set; }

        public Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken)
        {
            RoleReads++;
            return Task.FromResult(GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded(Roles));
        }

        public Task<GraphReadResult<IReadOnlyList<PimEligibility>>> ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken)
        {
            EligibilityReads++;
            return Task.FromResult(GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded(Eligibility));
        }
    }

    private sealed class RecordingPimActivationCommands : IPimActivationCommands
    {
        public PimActivationGraphResult Result { get; init; } = PimActivationGraphResult.Succeeded("request-1", "PendingApproval");
        public List<PimActivationGraphRequest> Requests { get; } = [];

        public Task<PimActivationGraphResult> ActivateDirectoryRoleAsync(PimActivationGraphRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
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
                new Claim("oid", "22222222-2222-2222-2222-222222222222"),
                new Claim("tid", "11111111-1111-1111-1111-111111111111"),
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
