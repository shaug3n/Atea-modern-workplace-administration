using System.Net;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Devices;

public sealed class Device360ServiceTests
{
    private const string ManagedDeviceId = "managed-device-123";

    [Fact]
    public async Task denied_devices_view_stops_before_graph()
    {
        var reader = new RecordingReader();
        var service = CreateService(reader, Snapshot() with { DirectoryRoles = [] });

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.CapabilityRequired);
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task invalid_managed_device_target_stops_before_graph()
    {
        var reader = new RecordingReader();
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), "managed/device", CancellationToken.None);

        response.Status.Should().Be(Device360Status.InvalidTarget);
        reader.Calls.Should().Be(0);
        reader.DetailCalls.Should().Be(0);
    }

    [Fact]
    public async Task optional_configuration_scope_is_independent_of_devices_view()
    {
        var reader = new RecordingReader();
        var service = CreateService(reader, Snapshot(configurationRead: false));

        var apps = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);
        var protection = await service.GetWindowsProtectionStateAsync(Context(), ManagedDeviceId, CancellationToken.None);
        var policies = await service.GetCompliancePolicyStatesAsync(Context(), ManagedDeviceId, CancellationToken.None);
        var configurations = await service.GetDeviceConfigurationStatesAsync(Context(), ManagedDeviceId, CancellationToken.None);
        var assignments = await service.GetConfigurationAssignmentsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        apps.Status.Should().Be(Device360Status.Succeeded);
        protection.Status.Should().Be(Device360Status.Succeeded);
        policies.Status.Should().Be(Device360Status.MissingScope);
        configurations.Status.Should().Be(Device360Status.MissingScope);
        assignments.Status.Should().Be(Device360Status.MissingScope);
        reader.DetectedAppCalls.Should().Be(1);
        reader.ProtectionCalls.Should().Be(1);
        reader.ComplianceCalls.Should().Be(0);
        reader.ConfigurationCalls.Should().Be(0);
        reader.AssignmentCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("consent_required", Device360Status.ConsentRequired)]
    [InlineData("temporarily_unavailable", Device360Status.TemporarilyUnavailable)]
    public async Task consent_and_transient_scope_probe_failures_are_distinct(string problem, string expected)
    {
        var reader = new RecordingReader();
        var service = CreateService(reader, Snapshot(configurationRead: false, configurationProblem: problem));

        var response = await service.GetDeviceConfigurationStatesAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(expected);
        reader.ConfigurationCalls.Should().Be(0);
    }

    [Fact]
    public async Task empty_reported_policy_list_is_not_no_assignments()
    {
        var reader = new RecordingReader { ConfigurationStates = [] };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetConfigurationAssignmentsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.NoReportedPolicies);
        response.Data.Should().BeEmpty();
        reader.ConfigurationCalls.Should().Be(1);
        reader.AssignmentCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("not_authorized", Device360Status.GraphForbidden)]
    [InlineData("throttled", Device360Status.Throttled)]
    public async Task graph_denial_and_throttle_are_not_empty_success(string category, string expected)
    {
        var reader = new RecordingReader
        {
            ComplianceResult = new Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>(
                null, new GraphOperationResult(false, category, category == "throttled" ? 429 : 403))
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetCompliancePolicyStatesAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(expected);
        response.Data.Should().BeNull();
        response.Error.Should().NotBeNull();
    }

    [Theory]
    [InlineData(400, "temporarily_unavailable", Device360Status.Failed)]
    [InlineData(503, "temporarily_unavailable", Device360Status.TemporarilyUnavailable)]
    [InlineData(429, "throttled", Device360Status.Throttled)]
    public async Task graph_status_classifies_errors_by_upstream_status(int statusCode, string expectedCategory, string expected)
    {
        var graphError = GraphErrorMapper.FromResponse(new HttpResponseMessage((HttpStatusCode)statusCode));
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(null, graphError)
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        graphError.Category.Should().Be(expectedCategory);
        graphError.StatusCode.Should().Be(statusCode);
        response.Status.Should().Be(expected);
    }

    [Fact]
    public async Task graph_transport_error_without_status_remains_temporarily_unavailable()
    {
        var graphError = GraphErrorMapper.FromException(new HttpRequestException());
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(null, graphError)
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        graphError.StatusCode.Should().BeNull();
        response.Status.Should().Be(Device360Status.TemporarilyUnavailable);
    }

    [Fact]
    public async Task partial_graph_pages_keep_data_and_partial_status()
    {
        var data = new DeviceDetectedApp("app-1", "App", "1.0", "windows", null);
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(
                [data], new GraphOperationResult(false, "temporarily_unavailable", 502, CorrelationId: "corr", RequestId: "req"), PartialData: true)
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.Partial);
        response.Data.Should().ContainSingle().Which.Should().Be(data);
        response.PartialData.Should().BeTrue();
        response.Error!.Category.Should().Be("temporarily_unavailable");
        response.GraphCorrelationId.Should().Be("corr");
        response.GraphRequestId.Should().Be("req");
    }

    [Fact]
    public async Task relationship_not_found_is_verified_against_the_core_device()
    {
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(
                null, new GraphOperationResult(false, "not_found", 404)),
            CoreDevice = new ManagedDeviceSummary(ManagedDeviceId, "Device", null, null, null, null, null, null, null, null, null, null, null)
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.Unsupported);
        reader.DetailCalls.Should().Be(1);
    }

    [Fact]
    public async Task relationship_not_found_preserves_core_verification_denial()
    {
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(
                null, new GraphOperationResult(false, "not_found", 404)),
            DetailResult = GraphReadResult<ManagedDeviceSummary?>.Failed(
                new GraphOperationResult(false, "not_authorized", 403, CorrelationId: "core-corr", RequestId: "core-req"))
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.GraphForbidden);
        response.Error!.Category.Should().Be("not_authorized");
        response.GraphCorrelationId.Should().Be("core-corr");
        response.GraphRequestId.Should().Be("core-req");
    }

    [Fact]
    public async Task relationship_not_found_preserves_core_verification_transient_failure()
    {
        var reader = new RecordingReader
        {
            DetectedAppsResult = new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>(
                null, new GraphOperationResult(false, "not_found", 404)),
            DetailResult = GraphReadResult<ManagedDeviceSummary?>.Failed(
                new GraphOperationResult(false, "temporarily_unavailable", 503))
        };
        var service = CreateService(reader, Snapshot());

        var response = await service.GetDetectedAppsAsync(Context(), ManagedDeviceId, CancellationToken.None);

        response.Status.Should().Be(Device360Status.TemporarilyUnavailable);
        response.Error!.Category.Should().Be("temporarily_unavailable");
    }

    private static Device360Service CreateService(RecordingReader reader, GraphAuthorizationSnapshot snapshot) =>
        new(reader, new StaticAuthorizationReader(snapshot), reader, () => DateTimeOffset.Parse("2026-10-08T12:00:00Z"));

    private static GraphAuthorizationSnapshot Snapshot(
        bool deviceRead = true,
        bool configurationRead = true,
        string? configurationProblem = null)
    {
        var scopeAvailability = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            ["DeviceManagementManagedDevices.Read.All"] = deviceRead,
            ["DeviceManagementConfiguration.Read.All"] = configurationRead
        };
        var scopeProblems = configurationProblem is null
            ? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["DeviceManagementConfiguration.Read.All"] = configurationProblem
            };

        return GraphAuthorizationSnapshot.Available(
            "actor-1",
            deviceRead ? GraphScopeCatalog.DeviceReadScopes : [],
            [new DirectoryRoleSnapshot(EntraRoleCatalog.IntuneAdministratorTemplateId, "Intune Administrator", DirectoryRoleAssignmentState.Active, "/")],
            scopeAvailability: scopeAvailability,
            scopeProblems: scopeProblems);
    }

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member", null),
        new AuthorizationWorkspaceMembership(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Workspace", "member"));

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class RecordingReader : IDevice360GraphReader, IManagedDeviceDetailReader
    {
        public int Calls => ComplianceCalls + ConfigurationCalls + AssignmentCalls + DetectedAppCalls + ProtectionCalls;
        public int ComplianceCalls { get; private set; }
        public int ConfigurationCalls { get; private set; }
        public int AssignmentCalls { get; private set; }
        public int DetectedAppCalls { get; private set; }
        public int ProtectionCalls { get; private set; }
        public int DetailCalls { get; private set; }
        public IReadOnlyList<DeviceConfigurationState> ConfigurationStates { get; set; } =
            [new DeviceConfigurationState("configuration-1", "Configuration", "compliant", "windows", 1, 1)];
        public ManagedDeviceSummary? CoreDevice { get; set; }
        public GraphReadResult<ManagedDeviceSummary?>? DetailResult { get; set; }
        public Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>? ComplianceResult { get; set; }
        public Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>? DetectedAppsResult { get; set; }

        public Task<Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>> ReadCompliancePolicyStatesAsync(string managedDeviceId, CancellationToken cancellationToken)
        {
            ComplianceCalls++;
            return Task.FromResult(ComplianceResult ?? new Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>([]));
        }

        public Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationState>>> ReadDeviceConfigurationStatesAsync(string managedDeviceId, CancellationToken cancellationToken)
        {
            ConfigurationCalls++;
            return Task.FromResult(new Device360GraphResult<IReadOnlyList<DeviceConfigurationState>>(ConfigurationStates));
        }

        public Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> ReadConfigurationAssignmentsAsync(IReadOnlyList<DeviceConfigurationState> reportedStates, CancellationToken cancellationToken)
        {
            AssignmentCalls++;
            return Task.FromResult(new Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>([]));
        }

        public Task<Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>> ReadDetectedAppsAsync(string managedDeviceId, CancellationToken cancellationToken)
        {
            DetectedAppCalls++;
            return Task.FromResult(DetectedAppsResult ?? new Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>([]));
        }

        public Task<Device360GraphResult<DeviceWindowsProtectionState>> ReadWindowsProtectionStateAsync(string managedDeviceId, CancellationToken cancellationToken)
        {
            ProtectionCalls++;
            return Task.FromResult(new Device360GraphResult<DeviceWindowsProtectionState>(new DeviceWindowsProtectionState()));
        }

        public Task<GraphReadResult<ManagedDeviceSummary?>> GetAsync(string deviceObjectId, CancellationToken cancellationToken)
        {
            DetailCalls++;
            return Task.FromResult(DetailResult ?? GraphReadResult<ManagedDeviceSummary?>.Succeeded(CoreDevice));
        }
    }
}
