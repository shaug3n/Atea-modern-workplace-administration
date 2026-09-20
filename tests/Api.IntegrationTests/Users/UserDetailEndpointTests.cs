using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Users;

public sealed class UserDetailEndpointTests
{
    [Fact]
    public async Task User_detail_endpoint_returns_complete_permitted_detail_without_raw_graph_payloads()
    {
        var graph = DetailGraphFixture.Permitted();
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/user-1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"displayName\":\"Ada Lovelace\"");
        body.Should().Contain("\"jobTitle\":\"Principal Engineer\"");
        body.Should().Contain("\"skuPartNumber\":\"ENTERPRISEPACK\"");
        body.Should().Contain("\"displayName\":\"Workplace Operators\"");
        body.Should().Contain("\"assignmentState\":\"active\"");
        body.Should().Contain("\"requiredCapability\":\"pim.activate\"");
        body.Should().Contain("\"method\":\"POST\"");
        body.Should().Contain("\"state\":\"allowed\"");
        body.Should().Contain("\"freshness\":\"fresh\"");
        body.Should().NotContain("@odata");
        body.Should().NotContain("Authorization");
        graph.DirectoryReader.VerifiedUserIds.Should().Contain("user-1");
    }

    [Fact]
    public async Task Section_endpoints_return_independent_authorization_states_without_blanketing_the_user()
    {
        var graph = DetailGraphFixture.Permitted(snapshot: GraphAuthorizationSnapshot.Available(
            "actor-1",
            ["Directory.Read.All", "User.Read.All", "Group.Read.All", "RoleManagement.Read.Directory", "RoleManagement.ReadWrite.Directory"],
            [
                new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/"),
                new DirectoryRoleSnapshot(EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.MfaRequired, "/api/pim/activations")),
                new DirectoryRoleSnapshot(EntraRoleCatalog.LicenseAdministratorTemplateId, "License Administrator", DirectoryRoleAssignmentState.Active, "/")
            ]));
        graph.GroupReader.Result = GraphReadResult<IReadOnlyList<GroupMembership>>.Failed(new GraphOperationResult(false, "not_authorized", 403));
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var licenses = await client.GetStringAsync("/api/users/user-1/licenses");
        var groups = await client.GetStringAsync("/api/users/user-1/groups");
        var pim = await client.GetStringAsync("/api/users/user-1/pim");

        licenses.Should().Contain("\"capability\":\"licenses.assign\"");
        licenses.Should().Contain("\"state\":\"consent_required\"");
        groups.Should().Contain("\"category\":\"not_authorized\"");
        groups.Should().Contain("\"statusCode\":403");
        pim.Should().Contain("\"capability\":\"pim.activate\"");
        pim.Should().Contain("\"state\":\"pim_mfa_required\"");
        pim.Should().Contain("\"requiresMfa\":true");
    }

    [Fact]
    public async Task User_detail_endpoint_returns_not_found_for_removed_or_cross_tenant_user()
    {
        var graph = DetailGraphFixture.Permitted();
        graph.DirectoryReader.User = null;
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/removed-user");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("user_not_found");
        graph.LicenseReader.Calls.Should().Be(0);
        graph.GroupReader.Calls.Should().Be(0);
        graph.RoleReader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Section_endpoint_marks_stale_data_when_graph_throttles()
    {
        var graph = DetailGraphFixture.Permitted();
        graph.LicenseReader.Result = GraphReadResult<IReadOnlyList<AssignedLicense>>.Failed(new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(30)));
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/user-1/licenses");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"freshness\":\"stale\"");
        body.Should().Contain("\"partialData\":true");
        body.Should().Contain("\"category\":\"throttled\"");
        body.Should().Contain("\"retryAfterSeconds\":30");
    }

    [Fact]
    public async Task User_detail_endpoint_maps_directory_consent_failure_without_throwing()
    {
        var graph = DetailGraphFixture.Permitted();
        graph.DirectoryReader.Error = new GraphOperationResult(false, "consent_required", 403);
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/user-1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"user\":null");
        body.Should().Contain("\"freshness\":\"unavailable\"");
        body.Should().Contain("\"partialData\":true");
        body.Should().Contain("\"category\":\"consent_required\"");
        body.Should().Contain("\"statusCode\":403");
        body.Should().NotContain("raw graph");
    }

    [Fact]
    public async Task Section_endpoint_maps_throttled_directory_verification_without_reading_section()
    {
        var graph = DetailGraphFixture.Permitted();
        graph.DirectoryReader.Error = new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(30));
        using var factory = CreateFactory(graph);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/user-1/licenses");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"freshness\":\"stale\"");
        body.Should().Contain("\"partialData\":true");
        body.Should().Contain("\"category\":\"throttled\"");
        body.Should().Contain("\"retryAfterSeconds\":30");
        graph.LicenseReader.Calls.Should().Be(0);
    }

    private static WebApplicationFactory<Program> CreateFactory(DetailGraphFixture graph) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
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
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(graph.CapabilityReader);
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton<IUserDirectoryReader>(graph.DirectoryReader);
                services.RemoveAll<IUserLicenseReader>();
                services.AddSingleton<IUserLicenseReader>(graph.LicenseReader);
                services.RemoveAll<IGroupMembershipReader>();
                services.AddSingleton<IGroupMembershipReader>(graph.GroupReader);
                services.RemoveAll<IRoleAndPimReader>();
                services.AddSingleton<IRoleAndPimReader>(graph.RoleReader);
            });
        });

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class DetailGraphFixture(
        StaticCapabilityReader capabilityReader,
        RecordingDirectoryReader directoryReader,
        RecordingLicenseReader licenseReader,
        RecordingGroupReader groupReader,
        RecordingRoleReader roleReader)
    {
        public StaticCapabilityReader CapabilityReader { get; } = capabilityReader;
        public RecordingDirectoryReader DirectoryReader { get; } = directoryReader;
        public RecordingLicenseReader LicenseReader { get; } = licenseReader;
        public RecordingGroupReader GroupReader { get; } = groupReader;
        public RecordingRoleReader RoleReader { get; } = roleReader;

        public static DetailGraphFixture Permitted(GraphAuthorizationSnapshot? snapshot = null)
        {
            var user = new UserDetails(
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

            return new DetailGraphFixture(
                new StaticCapabilityReader(snapshot ?? GraphAuthorizationSnapshot.Available(
                    "actor-1",
                    ["Directory.Read.All", "User.Read.All", "Group.Read.All", "RoleManagement.Read.Directory", "LicenseAssignment.ReadWrite.All", "GroupMember.ReadWrite.All", "RoleManagement.ReadWrite.Directory"],
                    [
                        new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/"),
                        new DirectoryRoleSnapshot(EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", DirectoryRoleAssignmentState.Active, "/")
                    ])),
                new RecordingDirectoryReader { User = user },
                new RecordingLicenseReader { Result = GraphReadResult<IReadOnlyList<AssignedLicense>>.Succeeded([new AssignedLicense("sku-1", "ENTERPRISEPACK", "Microsoft 365 E3")]) },
                new RecordingGroupReader { Result = GraphReadResult<IReadOnlyList<GroupMembership>>.Succeeded([new GroupMembership("group-1", "Workplace Operators", "workplace-operators", true, [])]) },
                new RecordingRoleReader
                {
                    Roles = GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded([new DirectoryRoleAssignment("role-1", EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", "active", "/")]),
                    Pim = GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded([new PimEligibility("eligibility-1", EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", "eligible_inactive", "pim.activate", true, false, true, true, 480, new PimActivationAction("request_activation", "/api/pim/activations", "POST", "pim.activate", true))])
                });
        }
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public List<string> VerifiedUserIds { get; } = [];
        public UserDetails? User { get; set; }
        public GraphOperationResult? Error { get; set; }

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken)
        {
            VerifiedUserIds.Add(userObjectId);
            if (Error is not null)
            {
                throw new GraphAdapterException(Error);
            }

            return Task.FromResult(User);
        }
    }

    private sealed class RecordingLicenseReader : IUserLicenseReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<AssignedLicense>> Result { get; set; } = GraphReadResult<IReadOnlyList<AssignedLicense>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<AssignedLicense>>> ReadUserLicensesAsync(string userObjectId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingGroupReader : IGroupMembershipReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<GroupMembership>> Result { get; set; } = GraphReadResult<IReadOnlyList<GroupMembership>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<GroupMembership>>> ReadUserGroupsAsync(string userObjectId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingRoleReader : IRoleAndPimReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>> Roles { get; set; } = GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded([]);
        public GraphReadResult<IReadOnlyList<PimEligibility>> Pim { get; set; } = GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Roles);
        }

        public Task<GraphReadResult<IReadOnlyList<PimEligibility>>> ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(Pim);
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "Contoso Workplace", "member"));
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
                new Claim("oid", ObjectId.ToString()),
                new Claim("tid", TenantId.ToString()),
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
