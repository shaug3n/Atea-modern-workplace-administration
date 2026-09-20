using System.Net;
using System.Reflection;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class GraphAdapterBoundaryTests
{
    [Fact]
    public void Public_mutation_adapters_expose_only_typed_operation_methods()
    {
        var publicGraphTypes = typeof(GraphUserLifecycle).Assembly.GetExportedTypes()
            .Where(type => type.Namespace == "Atea.UnifiedWorkplace.Api.Infrastructure.Graph")
            .Select(type => type.Name)
            .ToArray();

        publicGraphTypes.Should().NotContain(["IGraphMutationExecutor", "GraphMutation"]);

        var mutationServices = new[]
        {
            typeof(GraphUserLifecycle),
            typeof(GraphGroupMembershipService),
            typeof(GraphLicenseService),
            typeof(GraphRoleAndPimService)
        };

        foreach (var service in mutationServices)
        {
            var publicInstanceMethods = service.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

            publicInstanceMethods.Should().NotContain(method =>
                method.Name == "ExecuteAsync"
                || method.GetParameters().Any(parameter => parameter.ParameterType.Name == "GraphMutation"));
            publicInstanceMethods.Should().OnlyContain(method =>
                method.Name.EndsWith("Async", StringComparison.Ordinal)
                && (method.ReturnType == typeof(Task<GraphOperationResult>)
                    || method.ReturnType == typeof(Task<PimActivationGraphResult>)));
        }
    }

    [Fact]
    public async Task User_lifecycle_disables_account_with_internal_path_body_and_least_privilege_scopes()
    {
        var transport = new RecordingGraphTransport();
        var factory = new RecordingGraphClientFactory(transport);
        var lifecycle = new GraphUserLifecycle(factory);

        var result = await lifecycle.SetAccountEnabledAsync("user-1", false, "idem-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.RequestedScopes.Single().Should().Equal(GraphScopeCatalog.UserAccountWriteScopes);
        transport.Requests.Should().ContainSingle();
        var request = transport.Requests.Single();
        request.Method.Should().Be(HttpMethod.Patch);
        request.PathAndQuery.Should().Be("/v1.0/users/user-1");
        request.Headers.Should().ContainKey("Idempotency-Key").WhoseValue.Should().Be("idem-1");
        (await ReadJsonAsync(request)).RootElement.GetProperty("accountEnabled").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Group_membership_adds_member_with_internal_reference_body_and_group_scope()
    {
        var transport = new RecordingGraphTransport();
        var factory = new RecordingGraphClientFactory(transport);
        var groups = new GraphGroupMembershipService(factory);

        await groups.AddMemberAsync("group-1", "user-1", "idem-2", CancellationToken.None);

        factory.RequestedScopes.Single().Should().Equal(GraphScopeCatalog.GroupMembershipWriteScopes);
        var request = transport.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/v1.0/groups/group-1/members/$ref");
        (await ReadJsonAsync(request)).RootElement.GetProperty("@odata.id").GetString()
            .Should().Be("https://graph.microsoft.com/v1.0/directoryObjects/user-1");
    }

    [Fact]
    public async Task License_assignment_uses_license_scope_without_directory_readwrite()
    {
        var transport = new RecordingGraphTransport();
        var factory = new RecordingGraphClientFactory(transport);
        var licenses = new GraphLicenseService(factory);

        await licenses.AssignUserLicensesAsync("user-1", ["sku-1"], [], "idem-3", CancellationToken.None);

        factory.RequestedScopes.Single().Should().Equal(["LicenseAssignment.ReadWrite.All"]);
        factory.RequestedScopes.Single().Should().NotContain("Directory.ReadWrite.All");
        var request = transport.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/v1.0/users/user-1/assignLicense");
    }

    [Fact]
    public async Task Role_assignment_uses_role_management_scope_without_directory_readwrite()
    {
        var transport = new RecordingGraphTransport();
        var factory = new RecordingGraphClientFactory(transport);
        var roles = new GraphRoleAndPimService(factory);

        await roles.AssignDirectoryRoleAsync("role-definition-id", "principal-id", "/", "idem-4", CancellationToken.None);

        factory.RequestedScopes.Single().Should().Equal(["RoleManagement.ReadWrite.Directory"]);
        factory.RequestedScopes.Single().Should().NotContain("Directory.ReadWrite.All");
        var request = transport.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/v1.0/roleManagement/directory/roleAssignments");
    }

    [Fact]
    public async Task Pim_activation_uses_role_management_scope_and_returns_only_graph_reported_status()
    {
        var transport = new RecordingGraphTransport(new GraphTransportResponse(
            GraphOperationResult.Success("corr", "req"),
            """{"id":"request-1","status":"PendingApproval"}""",
            1,
            new Dictionary<string, IReadOnlyCollection<string>>()));
        var factory = new RecordingGraphClientFactory(transport);
        var roles = new GraphRoleAndPimService(factory);

        var result = await roles.ActivateDirectoryRoleAsync(
            new PimActivationGraphRequest("principal-id", "role-definition-id", "role-template-id", "/", 60, "Need access"),
            "idem-5",
            CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.RequestId.Should().Be("request-1");
        result.Status.Should().Be("PendingApproval");
        factory.RequestedScopes.Single().Should().Equal(["RoleManagement.ReadWrite.Directory"]);
        factory.RequestedScopes.Single().Should().NotContain("Directory.ReadWrite.All");
        var request = transport.Requests.Single();
        request.Method.Should().Be(HttpMethod.Post);
        request.PathAndQuery.Should().Be("/v1.0/roleManagement/directory/roleAssignmentScheduleRequests");
        request.Headers.Should().ContainKey("Idempotency-Key").WhoseValue.Should().Be("idem-5");
        var body = (await ReadJsonAsync(request)).RootElement;
        body.GetProperty("action").GetString().Should().Be("selfActivate");
        body.GetProperty("principalId").GetString().Should().Be("principal-id");
        body.GetProperty("roleDefinitionId").GetString().Should().Be("role-definition-id");
        body.GetProperty("scheduleInfo").GetProperty("expiration").GetProperty("duration").GetString().Should().Be("PT60M");
    }

    [Theory]
    [InlineData("consent_required", false, true, false, false, "consent_required")]
    [InlineData("not_authorized", false, false, true, false, "not_authorized")]
    [InlineData("unauthenticated", false, false, false, true, "unauthenticated")]
    [InlineData("temporarily_unavailable", false, false, false, false, "temporarily_unavailable")]
    public async Task Delegated_connection_probe_maps_graph_results_to_health_probe_contract(
        string category,
        bool expectedSuccess,
        bool expectedConsentRequired,
        bool expectedPermissionIncomplete,
        bool expectedConsentRevoked,
        string expectedProblem)
    {
        var transport = new RecordingGraphTransport(new GraphTransportResponse(
            new GraphOperationResult(false, category, StatusCode: category == "not_authorized" ? 403 : 401),
            "{}",
            1,
            new Dictionary<string, IReadOnlyCollection<string>>()));
        var probe = new DelegatedGraphConnectionProbe(new RecordingGraphClientFactory(transport));

        var result = await probe.ProbeAsync(Guid.NewGuid(), GraphScopeCatalog.V1DelegatedScopes, CancellationToken.None);

        result.IsSuccessful.Should().Be(expectedSuccess);
        result.ConsentRequired.Should().Be(expectedConsentRequired);
        result.PermissionIncomplete.Should().Be(expectedPermissionIncomplete);
        result.ConsentRevoked.Should().Be(expectedConsentRevoked);
        result.ProblemCategory.Should().Be(expectedProblem);
        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/me?$select=id,userPrincipalName,displayName");
    }

    [Fact]
    public async Task Delegated_connection_probe_returns_granted_scopes_on_success()
    {
        var transport = new RecordingGraphTransport(new GraphTransportResponse(
            GraphOperationResult.Success("corr", "req"),
            "{\"id\":\"user-1\"}",
            1,
            new Dictionary<string, IReadOnlyCollection<string>>()));
        var factory = new RecordingGraphClientFactory(transport);
        var probe = new DelegatedGraphConnectionProbe(factory);

        var result = await probe.ProbeAsync(Guid.NewGuid(), GraphScopeCatalog.V1DelegatedScopes, CancellationToken.None);

        result.IsSuccessful.Should().BeTrue();
        result.GrantedScopes.Should().Equal(GraphScopeCatalog.V1DelegatedScopes);
        factory.RequestedScopes.Single().Should().Equal(GraphScopeCatalog.V1DelegatedScopes);
    }

    private static async Task<JsonDocument> ReadJsonAsync(GraphRequest request)
    {
        request.Content.Should().NotBeNull();
        return JsonDocument.Parse(await request.Content!.ReadAsStringAsync());
    }

    private sealed class RecordingGraphClientFactory(RecordingGraphTransport transport) : IDelegatedGraphClientFactory
    {
        public List<IReadOnlyCollection<string>> RequestedScopes { get; } = [];

        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        {
            RequestedScopes.Add(scopes);
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingGraphTransport(GraphTransportResponse? response = null) : IGraphTransport
    {
        private readonly GraphTransportResponse response = response ?? new GraphTransportResponse(
            GraphOperationResult.Success("corr", "req"),
            "{}",
            1,
            new Dictionary<string, IReadOnlyCollection<string>>());

        public IReadOnlyCollection<string> Scopes { get; } = [];
        public List<GraphRequest> Requests { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(response);
        }
    }
}
