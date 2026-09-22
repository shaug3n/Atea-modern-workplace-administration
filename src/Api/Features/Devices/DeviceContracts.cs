using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public sealed record DeviceSearchRequest(
    string? Search = null,
    int PageSize = 50,
    string? ComplianceState = null,
    string? OperatingSystem = null,
    string? ContinuationToken = null);

public sealed record DeviceSearchQuery(
    string? Search = null,
    int PageSize = 50,
    string? ComplianceState = null,
    string? OperatingSystem = null,
    string? ContinuationPath = null,
    string? UserObjectId = null);

public sealed record ManagedDeviceSummary(
    string Id,
    string? DeviceName,
    string? OperatingSystem,
    string? OsVersion,
    string? ComplianceState,
    string? ManagementState,
    string? ManagedDeviceOwnerType,
    DateTimeOffset? LastSyncDateTime,
    string? UserId,
    string? AzureAdDeviceId,
    string? SerialNumber,
    string? Manufacturer,
    string? Model);

public sealed record DeviceAccess(
    string State,
    string? ReasonCode = null,
    CapabilityDecision? Authorization = null);

public sealed record DeviceError(
    string Category,
    string Message,
    string? State = null,
    int? StatusCode = null,
    int? RetryAfterSeconds = null);

public sealed record DeviceDirectoryResponse(
    IReadOnlyList<ManagedDeviceSummary> Items,
    int Total,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    DeviceAccess Access,
    DeviceError? Error = null,
    string? ContinuationToken = null);

public sealed record UserAssociatedDeviceResponse(
    IReadOnlyList<ManagedDeviceSummary> Items,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    DeviceAccess Access,
    DeviceError? Error = null);

public static class DeviceActionNames
{
    public const string Sync = "sync";
    public const string RemoteLock = "remote-lock";

    public static bool TryNormalize(string? value, out string action)
    {
        action = value?.Trim().ToLowerInvariant() switch
        {
            Sync => Sync,
            RemoteLock => RemoteLock,
            _ => string.Empty
        };
        return action.Length > 0;
    }
}

public static class DeviceCommandStatus
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string InvalidTarget = "invalid_target";
    public const string NotFound = "not_found";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
}

public sealed record DeviceCommandResult(
    string Status,
    string RequiredCapability,
    string? Error = null,
    CapabilityDecision? Authorization = null,
    bool Replayed = false,
    string? AuditWarning = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public static class DeviceSearchContract
{
    public static DeviceSearchQuery Normalize(DeviceSearchRequest request)
    {
        if (request.PageSize is < 1 or > 100)
        {
            throw new DeviceSearchValidationException("pageSize must be between 1 and 100.", "pageSize");
        }

        var search = Clean(request.Search);
        if (search is not null && (search.Length > 200 || search.Any(char.IsControl)))
        {
            throw new DeviceSearchValidationException("search filter is invalid.", "search");
        }

        var complianceState = NormalizeEnum(request.ComplianceState, "complianceState", ["compliant", "noncompliant", "unknown", "inGracePeriod", "configManager"]);
        var operatingSystem = NormalizeEnum(request.OperatingSystem, "operatingSystem", ["Windows", "iOS", "Android", "macOS", "Linux"]);
        return new DeviceSearchQuery(search, request.PageSize, complianceState, operatingSystem);
    }

    private static string? NormalizeEnum(string? value, string field, IReadOnlyCollection<string> supported) =>
        Clean(value) is not { } normalized
            ? null
            : supported.FirstOrDefault(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase))
              ?? throw new DeviceSearchValidationException($"{field} filter is not supported.", field);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed class DeviceSearchValidationException(string message, string field) : Exception(message)
{
    public string Field { get; } = field;
}

public static class DeviceDirectoryFreshness
{
    public const string Live = "live";
    public const string Stale = "stale";
    public const string Unavailable = "unavailable";
}
