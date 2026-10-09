using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Features.Identity;

public interface IAuthenticationMethodService
{
    Task<AuthenticationMethodsResponse> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<AuthenticationMethodCommandResult> RemoveAsync(WorkspaceContext context, string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken, string? reason = null);
    Task<AuthenticationMethodCommandResult> ResetMfaAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null);
    Task<TemporaryAccessPassCommandResult> CreateTemporaryAccessPassAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null);
}

public sealed class AuthenticationMethodService(
    IAuthenticationMethodReader reader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IAuthenticationMethodCommands commands,
    IIdempotencyService idempotency,
    IAuditWriter auditWriter,
    Func<DateTimeOffset>? utcNow = null) : IAuthenticationMethodService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<AuthenticationMethodsResponse> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userObjectId);
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.AuthenticationMethodsView];
        if (authorization.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
        {
            return Response(userObjectId, authorization, [], "unavailable", true,
                new AuthenticationMethodsError("capability_required", "Authentication methods are not available for the current role or delegated permissions.", authorization.State));
        }

        var result = await reader.ReadAsync(userObjectId, cancellationToken);
        if (result.Error is not null)
        {
            return Response(userObjectId, authorization, [], result.Error.Category == "throttled" ? "stale" : "unavailable", true,
                new AuthenticationMethodsError(result.Error.Category, MessageFor(result.Error.Category), authorization.State, result.Error.StatusCode,
                    result.Error.RetryAfter is null ? null : (int)Math.Ceiling(result.Error.RetryAfter.Value.TotalSeconds)));
        }

        return Response(userObjectId, authorization, result.Value, "live", false);
    }

    public async Task<AuthenticationMethodCommandResult> RemoveAsync(WorkspaceContext context, string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
    {
        reason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(userObjectId) || string.IsNullOrWhiteSpace(methodObjectId) || string.IsNullOrWhiteSpace(methodType))
        {
            return new AuthenticationMethodCommandResult("invalid_target", Capability.AuthenticationMethodsManage, "invalid_target");
        }

        if (!AuthenticationMethodActions.CanRemove(methodType))
        {
            return new AuthenticationMethodCommandResult("invalid_target", Capability.AuthenticationMethodsManage, "method_not_removable");
        }

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.AuthenticationMethodsManage];
        if (authorization.State != CapabilityState.Allowed)
        {
            return new AuthenticationMethodCommandResult("denied", Capability.AuthenticationMethodsManage, "capability_required", authorization);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new AuthenticationMethodCommandResult("invalid_target", Capability.AuthenticationMethodsManage, "idempotency_key_required", authorization);
        }

        AuthenticationMethodCommandResult? liveResult = null;
        var operation = "users.authentication_methods.remove";
        var targetId = $"{userObjectId}:{methodObjectId}";
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, targetId, idempotencyKey),
            new { methodType, reason },
            async () =>
            {
                var graph = await commands.RemoveAsync(userObjectId, methodObjectId, methodType, idempotencyKey, cancellationToken);
                var result = graph.IsSuccess
                    ? new AuthenticationMethodCommandResult("succeeded", Capability.AuthenticationMethodsManage, Authorization: authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId)
                    : new AuthenticationMethodCommandResult(graph.Category == "not_found" ? "not_found" : "temporarily_unavailable", Capability.AuthenticationMethodsManage, graph.Category, authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId);
                liveResult = result with { AuditWarning = await AuditAsync(context, operation, targetId, result.Status, result.Error, cancellationToken, reason, result.GraphCorrelationId, result.GraphRequestId) };
                return new IdempotentOperationResult(StatusCodeFor(liveResult), liveResult.Status, JsonSerializer.Serialize(liveResult with { AuditWarning = null }, JsonOptions));
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused) return new AuthenticationMethodCommandResult("idempotency_key_reused", Capability.AuthenticationMethodsManage, "idempotency_key_reused");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress) return new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, "idempotency_in_progress");
        var stored = JsonSerializer.Deserialize<AuthenticationMethodCommandResult>(outcome.Result.SafeResultJson, JsonOptions)
            ?? new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, "idempotency_result_unavailable");
        return stored with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed, AuditWarning = liveResult?.AuditWarning };
    }

    public async Task<AuthenticationMethodCommandResult> ResetMfaAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
    {
        reason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(userObjectId))
        {
            return new AuthenticationMethodCommandResult("invalid_target", Capability.AuthenticationMethodsManage, "invalid_target");
        }

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.AuthenticationMethodsManage];
        if (authorization.State != CapabilityState.Allowed)
        {
            return new AuthenticationMethodCommandResult("denied", Capability.AuthenticationMethodsManage, "capability_required", authorization);
        }

        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return new AuthenticationMethodCommandResult("invalid_target", Capability.AuthenticationMethodsManage, "idempotency_key_required", authorization);
        }

        AuthenticationMethodCommandResult? liveResult = null;
        const string operation = "users.authentication_methods.reset_mfa";
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, userObjectId, idempotencyKey),
            new { reason },
            async () =>
            {
                var methods = await reader.ReadAsync(userObjectId, cancellationToken);
                if (methods.Error is not null)
                {
                    var readFailure = new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, methods.Error.Category, authorization);
                    liveResult = readFailure with { AuditWarning = await AuditAsync(context, operation, userObjectId, readFailure.Status, readFailure.Error, cancellationToken, reason) };
                    return new IdempotentOperationResult(StatusCodeFor(liveResult), liveResult.Status, JsonSerializer.Serialize(liveResult with { AuditWarning = null }, JsonOptions));
                }

                var removable = methods.Value.Where(method => AuthenticationMethodActions.CanRemove(method.Type)).ToArray();
                var removedCount = 0;
                string? graphCorrelationId = null;
                string? graphRequestId = null;
                foreach (var method in removable)
                {
                    var graph = await commands.RemoveAsync(userObjectId, method.Id, method.Type, $"{idempotencyKey}:{method.Id}", cancellationToken);
                    graphCorrelationId = graph.CorrelationId ?? graphCorrelationId;
                    graphRequestId = graph.RequestId ?? graphRequestId;
                    if (!graph.IsSuccess)
                    {
                        var partialFailure = new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, graph.Category, authorization, RemovedCount: removedCount, GraphCorrelationId: graphCorrelationId, GraphRequestId: graphRequestId);
                        liveResult = partialFailure with { AuditWarning = await AuditAsync(context, operation, userObjectId, partialFailure.Status, partialFailure.Error, cancellationToken, reason, graphCorrelationId, graphRequestId) };
                        return new IdempotentOperationResult(StatusCodeFor(liveResult), liveResult.Status, JsonSerializer.Serialize(liveResult with { AuditWarning = null }, JsonOptions));
                    }

                    removedCount++;
                }

                var succeeded = new AuthenticationMethodCommandResult("succeeded", Capability.AuthenticationMethodsManage, Authorization: authorization, RemovedCount: removedCount, GraphCorrelationId: graphCorrelationId, GraphRequestId: graphRequestId);
                liveResult = succeeded with { AuditWarning = await AuditAsync(context, operation, userObjectId, succeeded.Status, null, cancellationToken, reason, graphCorrelationId, graphRequestId) };
                return new IdempotentOperationResult(StatusCodeFor(liveResult), liveResult.Status, JsonSerializer.Serialize(liveResult with { AuditWarning = null }, JsonOptions));
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused) return new AuthenticationMethodCommandResult("idempotency_key_reused", Capability.AuthenticationMethodsManage, "idempotency_key_reused");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress) return new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, "idempotency_in_progress");
        var stored = JsonSerializer.Deserialize<AuthenticationMethodCommandResult>(outcome.Result.SafeResultJson, JsonOptions)
            ?? new AuthenticationMethodCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, "idempotency_result_unavailable");
        return stored with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed, AuditWarning = liveResult?.AuditWarning };
    }

    public async Task<TemporaryAccessPassCommandResult> CreateTemporaryAccessPassAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
    {
        reason = NormalizeReason(reason);
        if (string.IsNullOrWhiteSpace(userObjectId)) return new("invalid_target", Capability.AuthenticationMethodsManage, Error: "invalid_target");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return new("invalid_target", Capability.AuthenticationMethodsManage, Error: "idempotency_key_required");

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.AuthenticationMethodsManage];
        if (authorization.State != CapabilityState.Allowed)
        {
            return new("denied", Capability.AuthenticationMethodsManage, Error: "capability_required", Authorization: authorization);
        }

        TemporaryAccessPassCommandResult? liveResult = null;
        const string operation = "users.authentication_methods.temporary_access_pass";
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, userObjectId, idempotencyKey),
            new { reason },
            async () =>
            {
                var graph = await commands.CreateTemporaryAccessPassAsync(userObjectId, idempotencyKey, cancellationToken);
                var valid = graph.Error is null && !string.IsNullOrWhiteSpace(graph.TemporaryAccessPass) && !string.IsNullOrWhiteSpace(graph.Id) && graph.LifetimeInMinutes == 60 && graph.IsUsableOnce == true;
                var result = valid
                    ? new TemporaryAccessPassCommandResult("succeeded", Capability.AuthenticationMethodsManage, graph.TemporaryAccessPass, graph.Id, graph.StartDateTime, graph.LifetimeInMinutes, graph.IsUsableOnce, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId, Authorization: authorization)
                    : new TemporaryAccessPassCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, Error: graph.Error?.Category ?? "invalid_response", GraphCorrelationId: graph.Error?.CorrelationId ?? graph.CorrelationId, GraphRequestId: graph.Error?.RequestId ?? graph.RequestId, Authorization: authorization);
                liveResult = result with { AuditWarning = await AuditAsync(context, operation, userObjectId, result.Status, result.Error, cancellationToken, reason, result.GraphCorrelationId, result.GraphRequestId) };
                var safe = liveResult with { TemporaryAccessPass = null, AuditWarning = null };
                return new IdempotentOperationResult(StatusCodeFor(safe), safe.Status, JsonSerializer.Serialize(safe, JsonOptions), safe.GraphCorrelationId, safe.GraphRequestId);
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused) return new("idempotency_key_reused", Capability.AuthenticationMethodsManage, Error: "idempotency_key_reused");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress) return new("temporarily_unavailable", Capability.AuthenticationMethodsManage, Error: "idempotency_in_progress");
        var stored = JsonSerializer.Deserialize<TemporaryAccessPassCommandResult>(outcome.Result.SafeResultJson, JsonOptions)
            ?? new TemporaryAccessPassCommandResult("temporarily_unavailable", Capability.AuthenticationMethodsManage, Error: "idempotency_result_unavailable");
        if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
        {
            return stored with { Replayed = true, TemporaryAccessPass = null, Error = stored.Status == "succeeded" ? "temporary_access_pass_already_issued" : stored.Error, AuditWarning = null };
        }

        return liveResult ?? stored;
    }

    private AuthenticationMethodsResponse Response(string userObjectId, CapabilityDecision authorization, IReadOnlyList<AuthenticationMethodItem> items, string freshness, bool partialData, AuthenticationMethodsError? error = null) =>
        new(userObjectId, items, utcNow(), freshness, partialData, new AuthenticationMethodsAccess(authorization.State, authorization.ReasonCode, authorization), error);

    private async Task<string?> AuditAsync(WorkspaceContext context, string action, string targetId, string result, string? failureCategory, CancellationToken cancellationToken, string? reason = null, string? graphCorrelationId = null, string? graphRequestId = null)
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
                TargetType = "authentication_method",
                TargetId = targetId,
                Outcome = result,
                Timestamp = utcNow(),
                GraphCorrelationId = graphCorrelationId,
                GraphRequestId = graphRequestId,
                FailureCategory = failureCategory,
                SafeMetadataJson = reason is null ? "{}" : JsonSerializer.Serialize(new { reason }, JsonOptions)
            }, cancellationToken);
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return "audit_persistence_failed"; }
    }

    private static int StatusCodeFor(AuthenticationMethodCommandResult result) => result.Status switch
    {
        "succeeded" => StatusCodes.Status200OK,
        "denied" => StatusCodes.Status403Forbidden,
        "not_found" => StatusCodes.Status404NotFound,
        "invalid_target" => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static int StatusCodeFor(TemporaryAccessPassCommandResult result) => result.Status switch
    {
        "succeeded" => StatusCodes.Status200OK,
        "denied" => StatusCodes.Status403Forbidden,
        "invalid_target" => StatusCodes.Status400BadRequest,
        "idempotency_key_reused" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static string MessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to read authentication methods.",
        "consent_required" => "Delegated Microsoft Graph consent is required for authentication methods.",
        "throttled" => "Microsoft Graph throttled the authentication-method request.",
        _ => "Authentication-method data is temporarily unavailable."
    };
}
