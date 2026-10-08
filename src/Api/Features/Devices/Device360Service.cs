using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public sealed class Device360Service(
    IDevice360GraphReader reader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IManagedDeviceDetailReader detailReader,
    Func<DateTimeOffset>? utcNow = null) : IDevice360Service
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<Device360SectionResponse<IReadOnlyList<DeviceCompliancePolicyState>>> GetCompliancePolicyStatesAsync(
        WorkspaceContext context,
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var gate = await PrepareAsync(context, managedDeviceId, requiresConfigurationScope: true, cancellationToken);
        if (gate is not null)
            return Create<IReadOnlyList<DeviceCompliancePolicyState>>(gate);

        var result = await reader.ReadCompliancePolicyStatesAsync(managedDeviceId, cancellationToken);
        return await ConvertAsync(result, managedDeviceId, verifyDeviceOnNotFound: true, cancellationToken);
    }

    public async Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationState>>> GetDeviceConfigurationStatesAsync(
        WorkspaceContext context,
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var gate = await PrepareAsync(context, managedDeviceId, requiresConfigurationScope: true, cancellationToken);
        if (gate is not null)
            return Create<IReadOnlyList<DeviceConfigurationState>>(gate);

        var result = await reader.ReadDeviceConfigurationStatesAsync(managedDeviceId, cancellationToken);
        return await ConvertAsync(result, managedDeviceId, verifyDeviceOnNotFound: true, cancellationToken);
    }

    public async Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> GetConfigurationAssignmentsAsync(
        WorkspaceContext context,
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var gate = await PrepareAsync(context, managedDeviceId, requiresConfigurationScope: true, cancellationToken);
        if (gate is not null)
            return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(gate);

        var states = await reader.ReadDeviceConfigurationStatesAsync(managedDeviceId, cancellationToken);
        if (states.Error is not null && states.Data is null)
        {
            var stateFailure = await ConvertAsync(states, managedDeviceId, verifyDeviceOnNotFound: true, cancellationToken);
            return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                stateFailure.Status, partialData: stateFailure.PartialData, error: stateFailure.Error, retryAfterSeconds: stateFailure.RetryAfterSeconds,
                graphCorrelationId: stateFailure.GraphCorrelationId, graphRequestId: stateFailure.GraphRequestId);
        }

        if (states.Data is null)
            return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(Device360Status.Unsupported);

        if (states.Data.Count == 0)
        {
            if (states.Error is not null)
                return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                    Device360Status.Partial, data: [], partialData: true, error: ToDeviceError(states.Error),
                    graphCorrelationId: states.Error.CorrelationId, graphRequestId: states.Error.RequestId,
                    retryAfterSeconds: RetryAfterSeconds(states.Error));
            return Create(Device360Status.NoReportedPolicies, data: (IReadOnlyList<DeviceConfigurationAssignmentTarget>)[]);
        }

        var assignments = await reader.ReadConfigurationAssignmentsAsync(states.Data, cancellationToken);
        if (assignments.Data is null)
        {
            var assignmentFailure = await ConvertAsync(assignments, managedDeviceId, verifyDeviceOnNotFound: false, cancellationToken);
            return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
                assignmentFailure.Status, partialData: assignmentFailure.PartialData, error: assignmentFailure.Error, retryAfterSeconds: assignmentFailure.RetryAfterSeconds,
                graphCorrelationId: assignmentFailure.GraphCorrelationId, graphRequestId: assignmentFailure.GraphRequestId);
        }

        var isPartial = states.PartialData || assignments.PartialData || states.Error is not null || assignments.Error is not null;
        var error = assignments.Error ?? states.Error;
        return Create<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(
            isPartial ? Device360Status.Partial : Device360Status.Succeeded,
            assignments.Data,
            partialData: isPartial,
            error: error is null ? null : ToDeviceError(error),
            graphCorrelationId: error?.CorrelationId,
            graphRequestId: error?.RequestId,
            retryAfterSeconds: RetryAfterSeconds(error));
    }

    public async Task<Device360SectionResponse<IReadOnlyList<DeviceDetectedApp>>> GetDetectedAppsAsync(
        WorkspaceContext context,
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var gate = await PrepareAsync(context, managedDeviceId, requiresConfigurationScope: false, cancellationToken);
        if (gate is not null)
            return Create<IReadOnlyList<DeviceDetectedApp>>(gate);

        var result = await reader.ReadDetectedAppsAsync(managedDeviceId, cancellationToken);
        return await ConvertAsync(result, managedDeviceId, verifyDeviceOnNotFound: true, cancellationToken);
    }

    public async Task<Device360SectionResponse<DeviceWindowsProtectionState>> GetWindowsProtectionStateAsync(
        WorkspaceContext context,
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var gate = await PrepareAsync(context, managedDeviceId, requiresConfigurationScope: false, cancellationToken);
        if (gate is not null)
            return Create<DeviceWindowsProtectionState>(gate);

        var result = await reader.ReadWindowsProtectionStateAsync(managedDeviceId, cancellationToken);
        return await ConvertAsync(result, managedDeviceId, verifyDeviceOnNotFound: true, cancellationToken);
    }

    private async Task<string?> PrepareAsync(
        WorkspaceContext context,
        string managedDeviceId,
        bool requiresConfigurationScope,
        CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(managedDeviceId))
            return Device360Status.InvalidTarget;

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var capability = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.DevicesView];
        if (capability.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
            return CapabilityStatus(snapshot, capability);

        if (requiresConfigurationScope)
        {
            var scopeStatus = ConfigurationScopeStatus(snapshot);
            if (scopeStatus is not null)
                return scopeStatus;
        }

        return null;
    }

    private async Task<Device360SectionResponse<T>> ConvertAsync<T>(
        Device360GraphResult<T> result,
        string managedDeviceId,
        bool verifyDeviceOnNotFound,
        CancellationToken cancellationToken)
    {
        if (result.Error is null)
        {
            if (result.Data is null)
                return Create<T>(Device360Status.Unsupported);

            return Create(
                result.PartialData ? Device360Status.Partial : Device360Status.Succeeded,
                result.Data,
                partialData: result.PartialData);
        }

        if (verifyDeviceOnNotFound && IsNotFound(result.Error))
            return await ResolveRelationshipNotFoundAsync<T>(managedDeviceId, cancellationToken);

        if (result.PartialData || result.Data is not null)
        {
            return Create<T>(
                Device360Status.Partial,
                result.Data,
                partialData: true,
                error: ToDeviceError(result.Error),
                graphCorrelationId: result.Error.CorrelationId,
                graphRequestId: result.Error.RequestId,
                retryAfterSeconds: RetryAfterSeconds(result.Error));
        }

        return Create<T>(
            StatusFor(result.Error),
            error: ToDeviceError(result.Error),
            graphCorrelationId: result.Error.CorrelationId,
            graphRequestId: result.Error.RequestId,
            retryAfterSeconds: RetryAfterSeconds(result.Error));
    }

    private async Task<Device360SectionResponse<T>> ResolveRelationshipNotFoundAsync<T>(
        string managedDeviceId,
        CancellationToken cancellationToken)
    {
        var verification = await detailReader.GetAsync(managedDeviceId, cancellationToken);
        if (verification.Error is not null)
        {
            if (IsNotFound(verification.Error))
                return Create<T>(Device360Status.DeviceNotFound, error: ToDeviceError(verification.Error));

            return Create<T>(
                StatusFor(verification.Error),
                error: ToDeviceError(verification.Error),
                graphCorrelationId: verification.Error.CorrelationId,
                graphRequestId: verification.Error.RequestId,
                retryAfterSeconds: RetryAfterSeconds(verification.Error));
        }

        return verification.Value is null
            ? Create<T>(Device360Status.DeviceNotFound)
            : Create<T>(Device360Status.Unsupported);
    }

    private string? ConfigurationScopeStatus(GraphAuthorizationSnapshot snapshot)
    {
        var scope = GraphScopeCatalog.DeviceConfigurationReadScopes[0];
        var hasScope = snapshot.ScopeAvailability is not null
            && snapshot.ScopeAvailability.TryGetValue(scope, out var available)
                ? available
                : snapshot.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase);
        if (hasScope)
            return null;

        if (snapshot.ScopeProblems is not null && snapshot.ScopeProblems.TryGetValue(scope, out var problem))
        {
            if (string.Equals(problem, Device360Status.ConsentRequired, StringComparison.OrdinalIgnoreCase))
                return Device360Status.ConsentRequired;
            if (string.Equals(problem, Device360Status.TemporarilyUnavailable, StringComparison.OrdinalIgnoreCase))
                return Device360Status.TemporarilyUnavailable;
        }

        return Device360Status.MissingScope;
    }

    private static string CapabilityStatus(GraphAuthorizationSnapshot snapshot, CapabilityDecision capability)
    {
        if (capability.State == CapabilityState.ConsentRequired || snapshot.ConsentRequired)
            return Device360Status.ConsentRequired;
        if (capability.State == CapabilityState.TemporarilyUnavailable)
            return Device360Status.TemporarilyUnavailable;
        return Device360Status.CapabilityRequired;
    }

    private static string StatusFor(GraphOperationResult error)
    {
        if (error.Category == Device360Status.InvalidTarget)
            return Device360Status.InvalidTarget;
        if (error.StatusCode == StatusCodes.Status429TooManyRequests
            || string.Equals(error.Category, "throttled", StringComparison.OrdinalIgnoreCase))
            return Device360Status.Throttled;
        if (error.StatusCode == StatusCodes.Status403Forbidden
            || error.StatusCode == StatusCodes.Status401Unauthorized
            || error.Category is "not_authorized" or "graph_forbidden" or "forbidden")
            return Device360Status.GraphForbidden;
        if (error.Category == Device360Status.ConsentRequired)
            return Device360Status.ConsentRequired;
        if (error.Category == Device360Status.MissingScope)
            return Device360Status.MissingScope;
        if (error.Category is "unsupported" or "not_supported" or "operation_not_supported")
            return Device360Status.Unsupported;
        if (IsNotFound(error))
            return Device360Status.Unsupported;
        if (error.StatusCode is >= 500 or 408
            || error.Category is "temporarily_unavailable" or "timeout" or "service_unavailable")
            return Device360Status.TemporarilyUnavailable;
        return Device360Status.Failed;
    }

    private static bool IsNotFound(GraphOperationResult result) =>
        result.StatusCode == StatusCodes.Status404NotFound
        || string.Equals(result.Category, "not_found", StringComparison.OrdinalIgnoreCase);

    private static DeviceError ToDeviceError(GraphOperationResult result) =>
        new(
            result.Category,
            result.Category switch
            {
                "not_authorized" or "graph_forbidden" or "forbidden" => "Microsoft Graph denied this device section read.",
                "consent_required" => "Delegated Microsoft Graph consent is required for this device section.",
                "throttled" => "Microsoft Graph throttled this device section read.",
                _ => "This device section could not be read from Microsoft Graph."
            },
            StatusCode: result.StatusCode,
            RetryAfterSeconds: RetryAfterSeconds(result));

    private static int? RetryAfterSeconds(GraphOperationResult? result) =>
        result?.RetryAfter is null ? null : (int)Math.Ceiling(result.RetryAfter.Value.TotalSeconds);

    private Device360SectionResponse<T> Create<T>(
        string status,
        T? data = default,
        bool partialData = false,
        DeviceError? error = null,
        string? graphCorrelationId = null,
        string? graphRequestId = null,
        int? retryAfterSeconds = null) =>
        new(status, data, utcNow(), partialData, error, retryAfterSeconds, graphCorrelationId, graphRequestId);
}
