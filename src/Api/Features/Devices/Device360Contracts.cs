using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public sealed record Device360GraphResult<T>(
    T? Data,
    GraphOperationResult? Error = null,
    bool PartialData = false);

public sealed record DeviceCompliancePolicyState(
    string Id,
    string? DisplayName,
    string? State,
    string? PlatformType,
    int? SettingCount,
    int? Version);

public sealed record DeviceConfigurationState(
    string Id,
    string? DisplayName,
    string? State,
    string? PlatformType,
    int? SettingCount,
    int? Version);

public sealed record DeviceConfigurationAssignmentTarget(
    string ConfigurationId,
    string? ConfigurationName,
    string AssignmentId,
    string AssignmentKind,
    string TargetType,
    string? GroupId,
    string? FilterId,
    string? FilterType);

public sealed record DeviceDetectedApp(
    string Id,
    string? DisplayName,
    string? Version,
    string Platform,
    string? Publisher);

public sealed record DeviceWindowsProtectionState
{
    public string? AntiMalwareVersion { get; init; }
    public bool? ControlledConfigurationEnabled { get; init; }
    public string? DeviceState { get; init; }
    public string? EngineVersion { get; init; }
    public bool? FullScanOverdue { get; init; }
    public bool? FullScanRequired { get; init; }
    public bool? IsVirtualMachine { get; init; }
    public DateTimeOffset? LastFullScanDateTime { get; init; }
    public string? LastFullScanSignatureVersion { get; init; }
    public DateTimeOffset? LastQuickScanDateTime { get; init; }
    public string? LastQuickScanSignatureVersion { get; init; }
    public DateTimeOffset? LastReportedDateTime { get; init; }
    public bool? MalwareProtectionEnabled { get; init; }
    public bool? NetworkInspectionSystemEnabled { get; init; }
    public string? ProductStatus { get; init; }
    public bool? QuickScanOverdue { get; init; }
    public bool? RealTimeProtectionEnabled { get; init; }
    public bool? RebootRequired { get; init; }
    public bool? SignatureUpdateOverdue { get; init; }
    public string? SignatureVersion { get; init; }
    public bool? TamperProtectionEnabled { get; init; }
}

public interface IDevice360GraphReader
{
    Task<Device360GraphResult<IReadOnlyList<DeviceCompliancePolicyState>>> ReadCompliancePolicyStatesAsync(
        string managedDeviceId,
        CancellationToken cancellationToken);

    Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationState>>> ReadDeviceConfigurationStatesAsync(
        string managedDeviceId,
        CancellationToken cancellationToken);

    Task<Device360GraphResult<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> ReadConfigurationAssignmentsAsync(
        IReadOnlyList<DeviceConfigurationState> reportedStates,
        CancellationToken cancellationToken);

    Task<Device360GraphResult<IReadOnlyList<DeviceDetectedApp>>> ReadDetectedAppsAsync(
        string managedDeviceId,
        CancellationToken cancellationToken);

    Task<Device360GraphResult<DeviceWindowsProtectionState>> ReadWindowsProtectionStateAsync(
        string managedDeviceId,
        CancellationToken cancellationToken);
}

public interface IDevice360Service
{
    Task<Device360SectionResponse<IReadOnlyList<DeviceCompliancePolicyState>>> GetCompliancePolicyStatesAsync(
        WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken);

    Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationState>>> GetDeviceConfigurationStatesAsync(
        WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken);

    Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> GetConfigurationAssignmentsAsync(
        WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken);

    Task<Device360SectionResponse<IReadOnlyList<DeviceDetectedApp>>> GetDetectedAppsAsync(
        WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken);

    Task<Device360SectionResponse<DeviceWindowsProtectionState>> GetWindowsProtectionStateAsync(
        WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken);
}

public static class Device360Status
{
    public const string Succeeded = "succeeded";
    public const string Partial = "partial";
    public const string Unsupported = "unsupported";
    public const string NoReportedPolicies = "no_reported_policies";
    public const string InvalidTarget = "invalid_target";
    public const string DeviceNotFound = "device_not_found";
    public const string CapabilityRequired = "capability_required";
    public const string MissingScope = "missing_scope";
    public const string ConsentRequired = "consent_required";
    public const string GraphForbidden = "graph_forbidden";
    public const string Throttled = "throttled";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
    public const string Failed = "failed";
}

public sealed record Device360SectionResponse<T>(
    string Status,
    T? Data = default,
    DateTimeOffset? RetrievedAt = null,
    bool PartialData = false,
    DeviceError? Error = null,
    int? RetryAfterSeconds = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);
