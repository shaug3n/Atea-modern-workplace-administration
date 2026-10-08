using System.Globalization;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public sealed class GraphDevice360Reader(IDelegatedGraphClientFactory clientFactory) : IDevice360GraphReader
{
    private const string PolicyStateSelect = "id,displayName,state,platformType,settingCount,version";
    private const string AssignmentSelect = "id,target";
    private const string DetectedAppSelect = "id,displayName,version,platform,publisher";
    private const string ProtectionSelect =
        "antiMalwareVersion,controlledConfigurationEnabled,deviceState,engineVersion,fullScanOverdue,fullScanRequired,isVirtualMachine,lastFullScanDateTime,lastFullScanSignatureVersion,lastQuickScanDateTime,lastQuickScanSignatureVersion,lastReportedDateTime,malwareProtectionEnabled,networkInspectionSystemEnabled,productStatus,quickScanOverdue,realTimeProtectionEnabled,rebootRequired,signatureUpdateOverdue,signatureVersion,tamperProtectionEnabled";

    public Task<Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>> ReadCompliancePolicyStatesAsync(
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(managedDeviceId))
            return Task.FromResult(Failed<IReadOnlyList<DeviceCompliancePolicyState>>("invalid_target"));

        var resourcePath =
            $"/v1.0/deviceManagement/managedDevices/{Uri.EscapeDataString(managedDeviceId)}/deviceCompliancePolicyStates";
        return ReadCollectionAsync(
            resourcePath,
            $"{resourcePath}?$select={PolicyStateSelect}",
            ConfigurationReadScopes,
            MapCompliancePolicyState,
            cancellationToken);
    }

    public Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationState>>> ReadDeviceConfigurationStatesAsync(
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(managedDeviceId))
            return Task.FromResult(Failed<IReadOnlyList<DeviceConfigurationState>>("invalid_target"));

        var resourcePath =
            $"/v1.0/deviceManagement/managedDevices/{Uri.EscapeDataString(managedDeviceId)}/deviceConfigurationStates";
        return ReadCollectionAsync(
            resourcePath,
            $"{resourcePath}?$select={PolicyStateSelect}",
            ConfigurationReadScopes,
            MapConfigurationState,
            cancellationToken);
    }

    public async Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> ReadConfigurationAssignmentsAsync(
        IReadOnlyList<DeviceConfigurationState> reportedStates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (reportedStates is null)
            return Failed<IReadOnlyList<DeviceConfigurationAssignmentTarget>>("invalid_response");

        var configurations = new List<(string Id, string? Name)>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var state in reportedStates)
        {
            if (state is null || !DeviceTarget.IsSafe(state.Id))
                return Failed<IReadOnlyList<DeviceConfigurationAssignmentTarget>>("invalid_target");

            if (seenIds.Add(state.Id))
                configurations.Add((state.Id, state.DisplayName));
        }

        if (configurations.Count == 0)
            return Succeeded<IReadOnlyList<DeviceConfigurationAssignmentTarget>>([]);

        var assignments = new List<DeviceConfigurationAssignmentTarget>();
        var hasSuccessfulConfigurationRead = false;
        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(
                GraphScopeCatalog.DeviceConfigurationReadScopes,
                cancellationToken);

            foreach (var configuration in configurations)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var resourcePath =
                    $"/v1.0/deviceManagement/deviceConfigurations/{Uri.EscapeDataString(configuration.Id)}/assignments";
                var result = await ReadCollectionThroughTransportAsync(
                    lease.Transport,
                    resourcePath,
                    $"{resourcePath}?$select={AssignmentSelect}",
                    item => MapAssignment(configuration.Id, configuration.Name, item),
                    cancellationToken);

                if (result.Error is not null)
                {
                    if (hasSuccessfulConfigurationRead || result.Data is not null)
                    {
                        if (result.Data is not null)
                            assignments.AddRange(result.Data);
                        return new Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                            assignments,
                            result.Error,
                            PartialData: true);
                    }

                    return new Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                        null,
                        result.Error,
                        result.PartialData);
                }

                hasSuccessfulConfigurationRead = true;
                if (result.Data is not null)
                    assignments.AddRange(result.Data);
            }

            return Succeeded<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(assignments);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return new Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                hasSuccessfulConfigurationRead ? assignments : null,
                GraphTokenAcquisitionErrorMapper.Map(exception),
                hasSuccessfulConfigurationRead);
        }
        catch (MsalUiRequiredException exception)
        {
            return new Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                hasSuccessfulConfigurationRead ? assignments : null,
                GraphTokenAcquisitionErrorMapper.Map(exception),
                hasSuccessfulConfigurationRead);
        }
    }

    public Task<Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>> ReadDetectedAppsAsync(
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(managedDeviceId))
            return Task.FromResult(Failed<IReadOnlyList<DeviceDetectedApp>>("invalid_target"));

        var resourcePath =
            $"/beta/deviceManagement/managedDevices/{Uri.EscapeDataString(managedDeviceId)}/detectedApps";
        return ReadCollectionAsync(
            resourcePath,
            $"{resourcePath}?$select={DetectedAppSelect}",
            GraphScopeCatalog.DeviceReadScopes,
            MapDetectedApp,
            cancellationToken);
    }

    public async Task<Device360GraphResult<DeviceWindowsProtectionState>> ReadWindowsProtectionStateAsync(
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(managedDeviceId))
            return Failed<DeviceWindowsProtectionState>("invalid_target");

        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(
                GraphScopeCatalog.DeviceReadScopes,
                cancellationToken);
            var resourcePath =
                $"/v1.0/deviceManagement/managedDevices/{Uri.EscapeDataString(managedDeviceId)}/windowsProtectionState";
            var response = await lease.Transport.SendAsync(
                new GraphRequest(HttpMethod.Get, $"{resourcePath}?$select={ProtectionSelect}"),
                cancellationToken);
            if (response.Result.Category == "not_found")
                return new Device360GraphResult<DeviceWindowsProtectionState>(null);
            if (!response.Result.IsSuccess)
                return new Device360GraphResult<DeviceWindowsProtectionState>(null, response.Result);

            using var document = JsonDocument.Parse(response.Content);
            var payload = document.RootElement;
            if (payload.ValueKind != JsonValueKind.Object)
                return Failed<DeviceWindowsProtectionState>("invalid_response");
            if (payload.TryGetProperty("value", out var value))
            {
                if (value.ValueKind == JsonValueKind.Null)
                    return new Device360GraphResult<DeviceWindowsProtectionState>(null);
                if (value.ValueKind != JsonValueKind.Object)
                    return Failed<DeviceWindowsProtectionState>("invalid_response");
                payload = value;
            }
            if (!payload.EnumerateObject().Any())
                return Failed<DeviceWindowsProtectionState>("invalid_response");

            return new Device360GraphResult<DeviceWindowsProtectionState>(MapProtectionState(payload));
        }
        catch (JsonException)
        {
            return Failed<DeviceWindowsProtectionState>("invalid_response");
        }
        catch (InvalidOperationException)
        {
            return Failed<DeviceWindowsProtectionState>("invalid_response");
        }
        catch (FormatException)
        {
            return Failed<DeviceWindowsProtectionState>("invalid_response");
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return new Device360GraphResult<DeviceWindowsProtectionState>(
                null,
                GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return new Device360GraphResult<DeviceWindowsProtectionState>(
                null,
                GraphTokenAcquisitionErrorMapper.Map(exception));
        }
    }

    private async Task<Device360GraphResult<IReadOnlyList<T>>> ReadCollectionAsync<T>(
        string resourcePath,
        string initialPath,
        IReadOnlyCollection<string> scopes,
        Func<JsonElement, T> map,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(scopes, cancellationToken);
            return await ReadCollectionThroughTransportAsync(
                lease.Transport,
                resourcePath,
                initialPath,
                map,
                cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return new Device360GraphResult<IReadOnlyList<T>>(null, GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return new Device360GraphResult<IReadOnlyList<T>>(null, GraphTokenAcquisitionErrorMapper.Map(exception));
        }
    }

    private static async Task<Device360GraphResult<IReadOnlyList<T>>> ReadCollectionThroughTransportAsync<T>(
        IGraphTransport transport,
        string resourcePath,
        string initialPath,
        Func<JsonElement, T> map,
        CancellationToken cancellationToken)
    {
        var items = new List<T>();
        var hasReadPage = false;
        var path = initialPath;
        var seenPaths = new HashSet<string>(StringComparer.Ordinal);
        try
        {
            while (path is not null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!seenPaths.Add(path))
                    return CollectionFailure(items, hasReadPage, new GraphOperationResult(false, "invalid_response"));

                var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
                if (!response.Result.IsSuccess)
                    return CollectionFailure(items, hasReadPage, response.Result);

                using var document = JsonDocument.Parse(response.Content);
                if (document.RootElement.ValueKind != JsonValueKind.Object
                    || !document.RootElement.TryGetProperty("value", out var value)
                    || value.ValueKind != JsonValueKind.Array)
                    return CollectionFailure(items, hasReadPage, new GraphOperationResult(false, "invalid_response"));

                var pageItems = value.EnumerateArray().Select(map).ToArray();
                items.AddRange(pageItems);
                hasReadPage = true;

                path = ContinuationPath(document.RootElement, resourcePath);
            }

            return Succeeded<IReadOnlyList<T>>(items);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
        {
            return CollectionFailure(items, hasReadPage, new GraphOperationResult(false, "invalid_response"));
        }
    }

    private static string? ContinuationPath(JsonElement root, string resourcePath)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var next))
            return null;
        if (next.ValueKind != JsonValueKind.String
            || !Uri.TryCreate(next.GetString(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(uri.Host, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.AbsolutePath, resourcePath, StringComparison.Ordinal)
            || string.IsNullOrEmpty(uri.Query))
            throw new InvalidOperationException("Invalid Graph continuation link.");

        return uri.PathAndQuery;
    }

    private static DeviceCompliancePolicyState MapCompliancePolicyState(JsonElement item) => new(
        RequiredString(item, "id"),
        OptionalString(item, "displayName"),
        OptionalString(item, "state"),
        OptionalString(item, "platformType"),
        OptionalInt(item, "settingCount"),
        OptionalInt(item, "version"));

    private static DeviceConfigurationState MapConfigurationState(JsonElement item) => new(
        RequiredString(item, "id"),
        OptionalString(item, "displayName"),
        OptionalString(item, "state"),
        OptionalString(item, "platformType"),
        OptionalInt(item, "settingCount"),
        OptionalInt(item, "version"));

    private static DeviceConfigurationAssignmentTarget MapAssignment(
        string configurationId,
        string? configurationName,
        JsonElement item)
    {
        if (!item.TryGetProperty("target", out var target) || target.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Assignment target is missing.");

        var odataType = OptionalString(target, "@odata.type");
        var assignmentKind = odataType?.EndsWith("exclusionGroupAssignmentTarget", StringComparison.OrdinalIgnoreCase) == true
            ? "exclude"
            : odataType is not null && (
                odataType.EndsWith("groupAssignmentTarget", StringComparison.OrdinalIgnoreCase)
                || odataType.EndsWith("allDevicesAssignmentTarget", StringComparison.OrdinalIgnoreCase)
                || odataType.EndsWith("allLicensedUsersAssignmentTarget", StringComparison.OrdinalIgnoreCase)
                || odataType.EndsWith("configurationManagerCollectionAssignmentTarget", StringComparison.OrdinalIgnoreCase))
                ? "include"
                : "unknown";

        return new DeviceConfigurationAssignmentTarget(
            configurationId,
            configurationName,
            RequiredString(item, "id"),
            assignmentKind,
            OptionalString(target, "targetType") ?? odataType ?? "unknown",
            OptionalString(target, "groupId"),
            OptionalString(target, "deviceAndAppManagementAssignmentFilterId"),
            OptionalString(target, "deviceAndAppManagementAssignmentFilterType"));
    }

    private static DeviceDetectedApp MapDetectedApp(JsonElement item) => new(
        RequiredString(item, "id"),
        OptionalString(item, "displayName"),
        OptionalString(item, "version"),
        OptionalString(item, "platform") ?? "unknown",
        OptionalString(item, "publisher"));

    private static DeviceWindowsProtectionState MapProtectionState(JsonElement payload) => new()
    {
        AntiMalwareVersion = OptionalString(payload, "antiMalwareVersion"),
        ControlledConfigurationEnabled = OptionalBoolean(payload, "controlledConfigurationEnabled"),
        DeviceState = OptionalString(payload, "deviceState"),
        EngineVersion = OptionalString(payload, "engineVersion"),
        FullScanOverdue = OptionalBoolean(payload, "fullScanOverdue"),
        FullScanRequired = OptionalBoolean(payload, "fullScanRequired"),
        IsVirtualMachine = OptionalBoolean(payload, "isVirtualMachine"),
        LastFullScanDateTime = OptionalDateTimeOffset(payload, "lastFullScanDateTime"),
        LastFullScanSignatureVersion = OptionalString(payload, "lastFullScanSignatureVersion"),
        LastQuickScanDateTime = OptionalDateTimeOffset(payload, "lastQuickScanDateTime"),
        LastQuickScanSignatureVersion = OptionalString(payload, "lastQuickScanSignatureVersion"),
        LastReportedDateTime = OptionalDateTimeOffset(payload, "lastReportedDateTime"),
        MalwareProtectionEnabled = OptionalBoolean(payload, "malwareProtectionEnabled"),
        NetworkInspectionSystemEnabled = OptionalBoolean(payload, "networkInspectionSystemEnabled"),
        ProductStatus = OptionalString(payload, "productStatus"),
        QuickScanOverdue = OptionalBoolean(payload, "quickScanOverdue"),
        RealTimeProtectionEnabled = OptionalBoolean(payload, "realTimeProtectionEnabled"),
        RebootRequired = OptionalBoolean(payload, "rebootRequired"),
        SignatureUpdateOverdue = OptionalBoolean(payload, "signatureUpdateOverdue"),
        SignatureVersion = OptionalString(payload, "signatureVersion"),
        TamperProtectionEnabled = OptionalBoolean(payload, "tamperProtectionEnabled")
    };

    private static string RequiredString(JsonElement item, string property) =>
        OptionalString(item, property) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"Graph property '{property}' is missing.");

    private static string? OptionalString(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"Graph property '{property}' is not a string.");
        return value.GetString();
    }

    private static int? OptionalInt(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.TryGetInt32(out var result)
            ? result
            : throw new InvalidOperationException($"Graph property '{property}' is not an integer.");
    }

    private static bool? OptionalBoolean(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : throw new InvalidOperationException($"Graph property '{property}' is not a Boolean.");
    }

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement item, string property)
    {
        if (!item.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        return value.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var result)
                ? result
                : throw new InvalidOperationException($"Graph property '{property}' is not a timestamp.");
    }

    private static Device360GraphResult<IReadOnlyList<T>> CollectionFailure<T>(
        IReadOnlyList<T> items,
        bool hasReadPage,
        GraphOperationResult error) =>
        new(hasReadPage ? items : null, error, hasReadPage);

    private static Device360GraphResult<T> Failed<T>(string category) =>
        new(default, new GraphOperationResult(false, category));

    private static Device360GraphResult<T> Succeeded<T>(T data) => new(data);

    private static IReadOnlyCollection<string> ConfigurationReadScopes { get; } =
        GraphScopeCatalog.DeviceReadScopes
            .Concat(GraphScopeCatalog.DeviceConfigurationReadScopes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
