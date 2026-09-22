using Atea.UnifiedWorkplace.Api.Features.Devices;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IManagedDeviceCommands
{
    Task<GraphOperationResult> ExecuteAsync(string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class GraphManagedDeviceCommands(IDelegatedGraphClientFactory clientFactory) : IManagedDeviceCommands
{
    public Task<GraphOperationResult> ExecuteAsync(string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceObjectId) || deviceObjectId.Any(character => char.IsControl(character) || character is '/' or '\\') || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult(new GraphOperationResult(false, "invalid_request"));
        }

        if (!DeviceActionNames.TryNormalize(action, out var normalizedAction))
        {
            return Task.FromResult(new GraphOperationResult(false, "invalid_request"));
        }

        return GraphMutationExecutor.ExecuteAsync(
            clientFactory,
            new ManagedDeviceActionMutation(deviceObjectId, GraphActionFor(normalizedAction), GraphBodyFor(normalizedAction)),
            idempotencyKey,
            cancellationToken);
    }

    private static string GraphActionFor(string action) => action switch
    {
        DeviceActionNames.Sync => "syncDevice",
        DeviceActionNames.RemoteLock => "remoteLock",
        DeviceActionNames.Restart => "rebootNow",
        DeviceActionNames.Retire => "retire",
        DeviceActionNames.Wipe => "wipe",
        _ => throw new ArgumentOutOfRangeException(nameof(action))
    };

    private static object? GraphBodyFor(string action) =>
        action == DeviceActionNames.Wipe
            ? new { keepEnrollmentData = false, keepUserData = false, persistEsimDataPlan = false }
            : null;
}

internal sealed record ManagedDeviceActionMutation(string DeviceObjectId, string Action, object? ActionBody) : JsonGraphMutation(GraphScopeCatalog.DevicePrivilegedOperationScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => $"/v1.0/deviceManagement/managedDevices/{Uri.EscapeDataString(DeviceObjectId)}/{Action}";
    internal override object? Body => ActionBody;
}
