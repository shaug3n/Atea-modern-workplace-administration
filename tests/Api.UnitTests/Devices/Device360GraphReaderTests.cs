using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Devices;

public sealed class Device360GraphReaderTests
{
    private const string ComplianceSelect = "id,displayName,state,platformType,settingCount,version";
    private const string ConfigurationSelect = "id,displayName,state,platformType,settingCount,version";
    private const string AssignmentSelect = "id,target";
    private const string DetectedAppSelect = "id,displayName,version,platform,publisher";
    private const string ProtectionSelect = "antiMalwareVersion,controlledConfigurationEnabled,deviceState,engineVersion,fullScanOverdue,fullScanRequired,isVirtualMachine,lastFullScanDateTime,lastFullScanSignatureVersion,lastQuickScanDateTime,lastQuickScanSignatureVersion,lastReportedDateTime,malwareProtectionEnabled,networkInspectionSystemEnabled,productStatus,quickScanOverdue,realTimeProtectionEnabled,rebootRequired,signatureUpdateOverdue,signatureVersion,tamperProtectionEnabled";

    [Fact]
    public async Task compliance_states_are_targeted_to_the_managed_device()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"policy-1","displayName":"Baseline","state":"compliant","platformType":"windows10AndLater","settingCount":8,"version":3}]}"""));
        var factory = new RecordingFactory(transport);

        var result = await new GraphDevice360Reader(factory).ReadCompliancePolicyStatesAsync("managed device-1", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Data.Should().ContainSingle().Which.Should().Be(new DeviceCompliancePolicyState(
            "policy-1", "Baseline", "compliant", "windows10AndLater", 8, 3));
        factory.RequestedScopes.Should().BeEquivalentTo(
            GraphScopeCatalog.DeviceReadScopes.Concat(GraphScopeCatalog.DeviceConfigurationReadScopes));
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            $"/v1.0/deviceManagement/managedDevices/managed%20device-1/deviceCompliancePolicyStates?$select={ComplianceSelect}");
    }

    [Fact]
    public async Task configuration_states_preserve_future_state_values()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"configuration-1","displayName":"Wi-Fi","state":"futureState","platformType":"futurePlatform","settingCount":null,"version":2}]}"""));
        var factory = new RecordingFactory(transport);

        var result = await new GraphDevice360Reader(factory)
            .ReadDeviceConfigurationStatesAsync("managed-device-2", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Data.Should().ContainSingle().Which.Should().Be(new DeviceConfigurationState(
            "configuration-1", "Wi-Fi", "futureState", "futurePlatform", null, 2));
        factory.RequestedScopes.Should().BeEquivalentTo(
            GraphScopeCatalog.DeviceReadScopes.Concat(GraphScopeCatalog.DeviceConfigurationReadScopes));
        transport.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            $"/v1.0/deviceManagement/managedDevices/managed-device-2/deviceConfigurationStates?$select={ConfigurationSelect}");
    }

    [Fact]
    public async Task assignment_reads_only_use_reported_policy_ids()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"assignment-1","target":{"@odata.type":"#microsoft.graph.exclusionGroupAssignmentTarget","targetType":"group","groupId":"group-1","deviceAndAppManagementAssignmentFilterId":"filter-1","deviceAndAppManagementAssignmentFilterType":"include"}}]}"""),
            Success("""{"value":[{"id":"assignment-2","target":{"@odata.type":"#microsoft.graph.groupAssignmentTarget","targetType":"group","groupId":"group-2"}}]}"""));
        var factory = new RecordingFactory(transport);
        var reportedStates = new DeviceConfigurationState[]
        {
            new("configuration a", "A", "compliant", "windows", null, 1),
            new("configuration a", "A duplicate", "compliant", "windows", null, 1),
            new("configuration-b", "B", "nonCompliant", "windows", null, 2)
        };

        var result = await new GraphDevice360Reader(factory)
            .ReadConfigurationAssignmentsAsync(reportedStates, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Data.Should().HaveCount(2);
        result.Data![0].Should().Be(new DeviceConfigurationAssignmentTarget(
            "configuration a", "A", "assignment-1", "exclude", "group", "group-1", "filter-1", "include"));
        result.Data[1].Should().Be(new DeviceConfigurationAssignmentTarget(
            "configuration-b", "B", "assignment-2", "include", "group", "group-2", null, null));
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            $"/v1.0/deviceManagement/deviceConfigurations/configuration%20a/assignments?$select={AssignmentSelect}",
            $"/v1.0/deviceManagement/deviceConfigurations/configuration-b/assignments?$select={AssignmentSelect}");
        factory.RequestedScopes.Should().Equal(GraphScopeCatalog.DeviceConfigurationReadScopes);
        factory.CreateCalls.Should().Be(1);
    }

    [Fact]
    public async Task detected_apps_use_the_per_device_relation_and_follow_all_pages()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"app-1","displayName":"App One","version":"1.0","platform":"windows","publisher":"Publisher One"}],"@odata.nextLink":"https://graph.microsoft.com/beta/deviceManagement/managedDevices/managed-device-3/detectedApps?$skiptoken=next"}"""),
            Success("""{"value":[{"id":"app-2","displayName":"App Two","version":null,"platform":"android","publisher":null}]}"""));
        var factory = new RecordingFactory(transport);

        var result = await new GraphDevice360Reader(factory).ReadDetectedAppsAsync("managed-device-3", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Data.Should().HaveCount(2);
        result.Data![1].Should().Be(new DeviceDetectedApp("app-2", "App Two", null, "android", null));
        factory.RequestedScopes.Should().Equal(GraphScopeCatalog.DeviceReadScopes);
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            $"/beta/deviceManagement/managedDevices/managed-device-3/detectedApps?$select={DetectedAppSelect}",
            "/beta/deviceManagement/managedDevices/managed-device-3/detectedApps?$skiptoken=next");
    }

    [Fact]
    public async Task protection_mapping_preserves_nullable_fields_and_source_time()
    {
        var transport = new RecordingTransport(
            Success("""{"antiMalwareVersion":"4.2","controlledConfigurationEnabled":null,"deviceState":"unknownFutureValue","engineVersion":null,"fullScanOverdue":false,"fullScanRequired":true,"isVirtualMachine":null,"lastFullScanDateTime":"2026-10-07T14:15:16+02:00","lastFullScanSignatureVersion":null,"lastQuickScanDateTime":null,"lastQuickScanSignatureVersion":null,"lastReportedDateTime":"2026-10-08T10:11:12Z","malwareProtectionEnabled":true,"networkInspectionSystemEnabled":null,"productStatus":"serviceNotRunning","quickScanOverdue":null,"realTimeProtectionEnabled":false,"rebootRequired":null,"signatureUpdateOverdue":null,"signatureVersion":"sig-7","tamperProtectionEnabled":null}"""));
        var factory = new RecordingFactory(transport);

        var result = await new GraphDevice360Reader(factory).ReadWindowsProtectionStateAsync("managed-device-4", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Data.Should().Be(new DeviceWindowsProtectionState
        {
            AntiMalwareVersion = "4.2",
            ControlledConfigurationEnabled = null,
            DeviceState = "unknownFutureValue",
            EngineVersion = null,
            FullScanOverdue = false,
            FullScanRequired = true,
            IsVirtualMachine = null,
            LastFullScanDateTime = DateTimeOffset.Parse("2026-10-07T14:15:16+02:00"),
            LastReportedDateTime = DateTimeOffset.Parse("2026-10-08T10:11:12Z"),
            MalwareProtectionEnabled = true,
            ProductStatus = "serviceNotRunning",
            RealTimeProtectionEnabled = false,
            SignatureVersion = "sig-7"
        });
        factory.RequestedScopes.Should().Equal(GraphScopeCatalog.DeviceReadScopes);
        transport.Requests.Should().ContainSingle().Which.PathAndQuery.Should().Be(
            $"/v1.0/deviceManagement/managedDevices/managed-device-4/windowsProtectionState?$select={ProtectionSelect}");

        var missingResource = await new GraphDevice360Reader(new RecordingFactory(
            new RecordingTransport(Success("""{"value":{}}"""))))
            .ReadWindowsProtectionStateAsync("managed-device-4", CancellationToken.None);
        missingResource.Data.Should().BeNull();
        missingResource.Error!.Category.Should().Be("invalid_response");
    }

    [Fact]
    public async Task protection_not_found_preserves_the_graph_error()
    {
        var notFound = new GraphOperationResult(
            false,
            "not_found",
            404,
            CorrelationId: "original-correlation",
            RequestId: "original-request");
        var transport = new RecordingTransport(
            new GraphTransportResponse(notFound, """{"error":"not found"}""", 1, EmptyHeaders));

        var result = await new GraphDevice360Reader(new RecordingFactory(transport))
            .ReadWindowsProtectionStateAsync("managed-device-4", CancellationToken.None);

        result.Data.Should().BeNull();
        result.Error.Should().Be(notFound);
    }

    [Fact]
    public async Task unsafe_id_dispatches_no_graph_request()
    {
        var transport = new RecordingTransport();
        var factory = new RecordingFactory(transport);
        var reader = new GraphDevice360Reader(factory);

        var compliance = await reader.ReadCompliancePolicyStatesAsync("device/one", CancellationToken.None);
        var apps = await reader.ReadDetectedAppsAsync("device?one", CancellationToken.None);
        var assignments = await reader.ReadConfigurationAssignmentsAsync(
            [new DeviceConfigurationState("configuration%2Fone", null, null, null, null, null)],
            CancellationToken.None);

        compliance.Error!.Category.Should().Be("invalid_target");
        apps.Error!.Category.Should().Be("invalid_target");
        assignments.Error!.Category.Should().Be("invalid_target");
        transport.Requests.Should().BeEmpty();
        factory.CreateCalls.Should().Be(0);
    }

    [Fact]
    public async Task malformed_or_failed_graph_pages_are_not_empty_success()
    {
        var empty = await new GraphDevice360Reader(new RecordingFactory(
            new RecordingTransport(Success("""{"value":[]}"""))))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        empty.Error.Should().BeNull();
        empty.Data.Should().BeEmpty();
        empty.PartialData.Should().BeFalse();

        var malformed = new GraphDevice360Reader(new RecordingFactory(
            new RecordingTransport(Success("{}"))));
        var malformedResult = await malformed.ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        malformedResult.Data.Should().BeNull();
        malformedResult.Error!.Category.Should().Be("invalid_response");

        var invalidJson = await new GraphDevice360Reader(new RecordingFactory(
            new RecordingTransport(Success("""{"value":["""))))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        invalidJson.Data.Should().BeNull();
        invalidJson.Error!.Category.Should().Be("invalid_response");

        var denied = new GraphOperationResult(false, "not_authorized", 403, CorrelationId: "original-correlation", RequestId: "original-request");
        var firstPageFailure = await new GraphDevice360Reader(new RecordingFactory(
            new RecordingTransport(new GraphTransportResponse(denied, """{"error":"denied"}""", 1, EmptyHeaders))))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        firstPageFailure.Data.Should().BeNull();
        firstPageFailure.Error.Should().Be(denied);

        var partialTransport = new RecordingTransport(
            Success("""{"value":[{"id":"app-1","displayName":"App One","version":"1","platform":"windows","publisher":null}],"@odata.nextLink":"https://graph.microsoft.com/beta/deviceManagement/managedDevices/managed-device-5/detectedApps?$skiptoken=next"}"""),
            new GraphTransportResponse(denied, """{"error":"denied"}""", 1, EmptyHeaders));
        var partial = await new GraphDevice360Reader(new RecordingFactory(partialTransport))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        partial.Data.Should().ContainSingle().Which.Id.Should().Be("app-1");
        partial.PartialData.Should().BeTrue();
        partial.Error.Should().Be(denied);

        var unsafeContinuation = new RecordingTransport(
            Success("""{"value":[],"@odata.nextLink":"https://attacker.example/beta/deviceManagement/managedDevices/managed-device-5/detectedApps?$skiptoken=next"}"""));
        var unsafeResult = await new GraphDevice360Reader(new RecordingFactory(unsafeContinuation))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        unsafeResult.Data.Should().BeEmpty();
        unsafeResult.Error!.Category.Should().Be("invalid_response");
        unsafeResult.PartialData.Should().BeTrue();
        unsafeContinuation.Requests.Should().ContainSingle();

        var wrongResourceContinuation = new RecordingTransport(
            Success("""{"value":[],"@odata.nextLink":"https://graph.microsoft.com/beta/deviceManagement/managedDevices/another-device/detectedApps?$skiptoken=next"}"""));
        var wrongResourceResult = await new GraphDevice360Reader(new RecordingFactory(wrongResourceContinuation))
            .ReadDetectedAppsAsync("managed-device-5", CancellationToken.None);
        wrongResourceResult.Error!.Category.Should().Be("invalid_response");
        wrongResourceContinuation.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task cancellation_is_not_converted_into_a_graph_error()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new RecordingTransport();

        var act = () => new GraphDevice360Reader(new RecordingFactory(transport))
            .ReadCompliancePolicyStatesAsync("managed-device-6", cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        transport.Requests.Should().BeEmpty();
    }

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyHeaders { get; } =
        new Dictionary<string, IReadOnlyCollection<string>>();

    private static GraphTransportResponse Success(string content) =>
        new(GraphOperationResult.Success(), content, 1, EmptyHeaders);

    private sealed class RecordingFactory(RecordingTransport transport) : IDelegatedGraphClientFactory
    {
        public IReadOnlyCollection<string> RequestedScopes { get; private set; } = [];
        public int CreateCalls { get; private set; }

        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        {
            CreateCalls++;
            RequestedScopes = scopes.ToArray();
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingTransport(params GraphTransportResponse[] responses) : IGraphTransport
    {
        private int index;
        public IReadOnlyCollection<string> Scopes => [];
        public List<GraphRequest> Requests { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses[Math.Min(index++, responses.Length - 1)]);
        }
    }
}
