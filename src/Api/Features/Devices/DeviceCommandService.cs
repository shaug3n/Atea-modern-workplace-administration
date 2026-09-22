using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public interface IDeviceCommandService
{
    Task<DeviceCommandResult> ExecuteAsync(WorkspaceContext context, string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class DeviceCommandService(
    IManagedDeviceCommands deviceCommands,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IIdempotencyService idempotency,
    IAuditWriter auditWriter,
    Func<DateTimeOffset>? utcNow = null) : IDeviceCommandService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<DeviceCommandResult> ExecuteAsync(WorkspaceContext context, string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceObjectId) || deviceObjectId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            return new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "invalid_target");
        }

        if (!DeviceActionNames.TryNormalize(action, out var normalizedAction))
        {
            return new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "invalid_action");
        }

        var operation = $"devices.{normalizedAction}";
        if (!DeviceCommandInputValidation.IsValidIdempotencyKey(idempotencyKey))
        {
            return new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "invalid_idempotency_key");
        }

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.DevicesPrivilegedManage];
        if (authorization.State != CapabilityState.Allowed)
        {
            return await DenyAsync(context, deviceObjectId, operation, authorization, cancellationToken);
        }

        DeviceCommandResult? liveResult = null;
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, deviceObjectId, idempotencyKey),
            new { action = normalizedAction },
            async () =>
            {
                var graph = await deviceCommands.ExecuteAsync(deviceObjectId, normalizedAction, idempotencyKey, cancellationToken);
                var result = MapGraphResult(graph, authorization);
                var audited = result with { AuditWarning = await AuditAsync(context, operation, deviceObjectId, result.Status, result.Error, result.GraphCorrelationId, result.GraphRequestId, cancellationToken) };
                liveResult = audited;
                return new IdempotentOperationResult(StatusCodeFor(audited), audited.Status, JsonSerializer.Serialize(audited with { AuditWarning = null }, JsonOptions), audited.GraphCorrelationId, audited.GraphRequestId);
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused)
        {
            return new DeviceCommandResult(DeviceCommandStatus.IdempotencyKeyReused, Capability.DevicesPrivilegedManage, DeviceCommandStatus.IdempotencyKeyReused);
        }

        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
        {
            return new DeviceCommandResult(DeviceCommandStatus.TemporarilyUnavailable, Capability.DevicesPrivilegedManage, "idempotency_in_progress");
        }

        var stored = JsonSerializer.Deserialize<DeviceCommandResult>(outcome.Result.SafeResultJson, JsonOptions)
            ?? new DeviceCommandResult(DeviceCommandStatus.TemporarilyUnavailable, Capability.DevicesPrivilegedManage, "idempotency_result_unavailable");
        return stored with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed, AuditWarning = liveResult?.AuditWarning };
    }

    private async Task<DeviceCommandResult> DenyAsync(WorkspaceContext context, string targetId, string operation, CapabilityDecision authorization, CancellationToken cancellationToken)
    {
        var auditWarning = await AuditAsync(context, operation, targetId, DeviceCommandStatus.Denied, "capability_required", null, null, cancellationToken);
        return new DeviceCommandResult(DeviceCommandStatus.Denied, Capability.DevicesPrivilegedManage, "capability_required", authorization, AuditWarning: auditWarning);
    }

    private static DeviceCommandResult MapGraphResult(GraphOperationResult graph, CapabilityDecision authorization)
    {
        if (graph.IsSuccess) return new DeviceCommandResult(DeviceCommandStatus.Succeeded, Capability.DevicesPrivilegedManage, Authorization: authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId);
        var status = graph.Category switch
        {
            "not_found" => DeviceCommandStatus.NotFound,
            "invalid_request" or "invalid_target" => DeviceCommandStatus.InvalidTarget,
            _ => DeviceCommandStatus.TemporarilyUnavailable
        };
        return new DeviceCommandResult(status, Capability.DevicesPrivilegedManage, graph.Category, authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId);
    }

    private async Task<string?> AuditAsync(WorkspaceContext context, string action, string targetId, string result, string? failureCategory, string? graphCorrelationId, string? graphRequestId, CancellationToken cancellationToken)
    {
        try
        {
            await auditWriter.WriteAsync(new AuditEvent
            {
                WorkspaceId = context.Membership.WorkspaceId,
                TenantId = context.User.TenantId,
                ActorTenantId = context.User.TenantId,
                ActorObjectId = context.User.ObjectId,
                Action = action,
                TargetType = "managed_device",
                TargetId = targetId,
                Outcome = result,
                Timestamp = utcNow(),
                GraphCorrelationId = graphCorrelationId,
                GraphRequestId = graphRequestId,
                FailureCategory = failureCategory,
                SafeMetadataJson = "{}"
            }, cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return "audit_persistence_failed";
        }
    }

    private static int StatusCodeFor(DeviceCommandResult result) => result.Status switch
    {
        DeviceCommandStatus.Succeeded => StatusCodes.Status200OK,
        DeviceCommandStatus.Denied => StatusCodes.Status403Forbidden,
        DeviceCommandStatus.NotFound => StatusCodes.Status404NotFound,
        DeviceCommandStatus.InvalidTarget => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
