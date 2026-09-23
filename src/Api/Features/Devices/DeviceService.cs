using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.AspNetCore.DataProtection;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public interface IDeviceService
{
    Task<DeviceDirectoryResponse> SearchAsync(WorkspaceContext context, DeviceSearchRequest request, CancellationToken cancellationToken);
}

public sealed class DeviceService(
    IManagedDeviceReader reader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    DeviceContinuationTokenProtector continuationTokens,
    Func<DateTimeOffset>? utcNow = null) : IDeviceService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<DeviceDirectoryResponse> SearchAsync(WorkspaceContext context, DeviceSearchRequest request, CancellationToken cancellationToken)
    {
        var normalizedQuery = DeviceSearchContract.Normalize(request);
        var query = normalizedQuery;
        string? continuationPath = null;
        if (!string.IsNullOrWhiteSpace(request.ContinuationToken) && !continuationTokens.TryUnprotect(request.ContinuationToken, context, normalizedQuery, out continuationPath))
        {
            throw new DeviceSearchValidationException("continuationToken is invalid or expired.", "continuationToken");
        }

        if (!string.IsNullOrWhiteSpace(continuationPath))
        {
            query = normalizedQuery with { ContinuationPath = continuationPath };
        }
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.DevicesView];
        if (authorization.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
        {
            return Response(query, authorization, [], DeviceDirectoryFreshness.Unavailable, true,
                new DeviceError("capability_required", "Managed devices are not available for the current role or delegated permissions.", authorization.State));
        }

        var result = await reader.ReadAsync(query, cancellationToken);
        if (result.Error is not null)
        {
            return Response(query, authorization, [], FreshnessFor(result.Error.Category), true,
                new DeviceError(result.Error.Category, MessageFor(result.Error.Category), authorization.State, result.Error.StatusCode,
                    result.Error.RetryAfter is null ? null : (int)Math.Ceiling(result.Error.RetryAfter.Value.TotalSeconds)));
        }

        var continuationToken = string.IsNullOrWhiteSpace(result.Value.ContinuationLink)
            ? null
            : continuationTokens.Protect(context, normalizedQuery, result.Value.ContinuationLink, utcNow());
        return Response(query, authorization, result.Value.Items, DeviceDirectoryFreshness.Live, false, continuationToken: continuationToken);
    }

    private DeviceDirectoryResponse Response(DeviceSearchQuery query, CapabilityDecision authorization, IReadOnlyList<ManagedDeviceSummary> items, string freshness, bool partialData, DeviceError? error = null, string? continuationToken = null) =>
        new(items, items.Count, utcNow(), freshness, partialData, new DeviceAccess(authorization.State, authorization.ReasonCode, authorization), error, continuationToken);

    private static string FreshnessFor(string category) => category == "throttled" ? DeviceDirectoryFreshness.Stale : DeviceDirectoryFreshness.Unavailable;

    private static string MessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to read managed devices.",
        "consent_required" => "Delegated Microsoft Graph consent is required for managed devices.",
        "not_provisioned" => "Intune is not provisioned for this tenant. Enable Intune/MDM and enroll a managed device, then retry.",
        "throttled" => "Microsoft Graph throttled the managed-device request.",
        _ => "Managed-device data is temporarily unavailable."
    };
}

public sealed record DeviceContinuationCursor(
    Guid WorkspaceId,
    Guid RequesterObjectId,
    string Path,
    string QueryFingerprint,
    long ExpiresAtUnixSeconds);

public sealed class DeviceContinuationTokenProtector(IDataProtectionProvider provider)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private readonly IDataProtector protector = provider.CreateProtector("atea.unified-workplace.devices-continuation.v1");

    public string Protect(WorkspaceContext context, DeviceSearchQuery query, string path, DateTimeOffset issuedAt)
    {
        if (!IsManagedDevicesPath(path)) throw new ArgumentException("Continuation path must be a managed-devices collection path.", nameof(path));
        var cursor = new DeviceContinuationCursor(context.Membership.WorkspaceId, context.User.ObjectId, path, DeviceQueryFingerprint.Create(query), issuedAt.Add(Lifetime).ToUnixTimeSeconds());
        return protector.Protect(JsonSerializer.Serialize(cursor));
    }

    public bool TryUnprotect(string token, WorkspaceContext context, DeviceSearchQuery query, out string? path)
    {
        path = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096) return false;
        try
        {
            var cursor = JsonSerializer.Deserialize<DeviceContinuationCursor>(protector.Unprotect(token));
            if (cursor is null || !IsManagedDevicesPath(cursor.Path) || cursor.WorkspaceId != context.Membership.WorkspaceId || cursor.RequesterObjectId != context.User.ObjectId || cursor.QueryFingerprint != DeviceQueryFingerprint.Create(query) || cursor.ExpiresAtUnixSeconds <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
            path = cursor.Path;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool IsManagedDevicesPath(string path) =>
        path.Equals("/v1.0/deviceManagement/managedDevices", StringComparison.Ordinal) || path.StartsWith("/v1.0/deviceManagement/managedDevices?", StringComparison.Ordinal);
}

internal static class DeviceQueryFingerprint
{
    public static string Create(DeviceSearchQuery query) => string.Join("\n", [query.Search ?? string.Empty, query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture), query.ComplianceState ?? string.Empty, query.OperatingSystem ?? string.Empty]);
}
