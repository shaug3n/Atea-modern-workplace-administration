using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public sealed class UserAssociatedDeviceService(
    IManagedDeviceReader reader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    Func<DateTimeOffset>? utcNow = null)
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserAssociatedDeviceResponse> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.DevicesView];
        if (authorization.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
        {
            return Response(authorization, DeviceDirectoryFreshness.Unavailable, true,
                new DeviceError("capability_required", "Associated managed-device data is not available for the current role or delegated permissions.", authorization.State));
        }

        var result = await reader.ReadForUserAsync(userObjectId, cancellationToken);
        if (result.Error is not null)
        {
            return Response(authorization, DeviceDirectoryFreshness.Unavailable, true,
                new DeviceError(result.Error.Category, MessageFor(result.Error.Category), authorization.State, result.Error.StatusCode,
                    result.Error.RetryAfter is null ? null : (int)Math.Ceiling(result.Error.RetryAfter.Value.TotalSeconds)));
        }

        return Response(authorization, DeviceDirectoryFreshness.Live, false, items: result.Value);
    }

    private UserAssociatedDeviceResponse Response(
        CapabilityDecision authorization,
        string freshness,
        bool partialData,
        DeviceError? error = null,
        IReadOnlyList<ManagedDeviceSummary>? items = null) =>
        new(items ?? [], utcNow(), freshness, partialData, new DeviceAccess(authorization.State, authorization.ReasonCode, authorization), error);

    private static string MessageFor(string category) => category switch
    {
        "association_query_unsupported" => "Microsoft Graph does not support the targeted associated-device query for this tenant.",
        "consent_required" => "Delegated Microsoft Graph consent is required for associated managed devices.",
        "not_authorized" => "The signed-in user is not authorized to read associated managed devices.",
        _ => "Associated managed-device data is temporarily unavailable."
    };
}
