using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserSessionCommandValidation
{
    public static bool IsValidTarget(string? value) => !string.IsNullOrWhiteSpace(value) && !value.Any(character => char.IsControl(character) || character is '/' or '\\');
}

public sealed record UserSessionCommandResult(
    string Status,
    string RequiredCapability,
    string? Error = null,
    CapabilityDecision? Authorization = null,
    bool Replayed = false,
    string? AuditWarning = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public interface IUserSessionCommandService
{
    Task<UserSessionCommandResult> RevokeAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class UserSessionCommandService(
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IUserSessionCommands commands,
    IIdempotencyService idempotency,
    IAuditWriter auditWriter,
    Func<DateTimeOffset>? utcNow = null) : IUserSessionCommandService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserSessionCommandResult> RevokeAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!UserSessionCommandValidation.IsValidTarget(userObjectId)) return new("invalid_target", Capability.UsersRevokeSessions, "invalid_target");
        if (string.IsNullOrWhiteSpace(idempotencyKey)) return new("invalid_target", Capability.UsersRevokeSessions, "idempotency_key_required");
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.UsersRevokeSessions];
        if (authorization.State != CapabilityState.Allowed) return new("denied", Capability.UsersRevokeSessions, "capability_required", authorization);

        UserSessionCommandResult? live = null;
        const string operation = "users.sessions.revoke";
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, userObjectId, idempotencyKey),
            new { },
            async () =>
            {
                var graph = await commands.RevokeAsync(userObjectId, idempotencyKey, cancellationToken);
                var result = graph.IsSuccess
                    ? new UserSessionCommandResult("succeeded", Capability.UsersRevokeSessions, Authorization: authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId)
                    : new UserSessionCommandResult("temporarily_unavailable", Capability.UsersRevokeSessions, graph.Category, authorization, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId);
                live = result with { AuditWarning = await AuditAsync(context, operation, userObjectId, result.Status, result.Error, result.GraphCorrelationId, result.GraphRequestId, cancellationToken) };
                var safe = live with { AuditWarning = null };
                return new IdempotentOperationResult(StatusCodeFor(safe), safe.Status, JsonSerializer.Serialize(safe, JsonOptions), safe.GraphCorrelationId, safe.GraphRequestId);
            }, cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused) return new("idempotency_key_reused", Capability.UsersRevokeSessions, "idempotency_key_reused");
        if (outcome.Kind == IdempotencyOutcomeKind.InProgress) return new("temporarily_unavailable", Capability.UsersRevokeSessions, "idempotency_in_progress");
        var stored = JsonSerializer.Deserialize<UserSessionCommandResult>(outcome.Result.SafeResultJson, JsonOptions) ?? new("temporarily_unavailable", Capability.UsersRevokeSessions, "idempotency_result_unavailable");
        return stored with { Replayed = outcome.Kind == IdempotencyOutcomeKind.Replayed, AuditWarning = live?.AuditWarning };
    }

    private async Task<string?> AuditAsync(WorkspaceContext context, string operation, string targetId, string outcome, string? error, string? correlation, string? requestId, CancellationToken cancellationToken)
    {
        try
        {
            await auditWriter.WriteAsync(new AuditEvent
            {
                WorkspaceId = context.Membership.WorkspaceId, TenantId = context.User.TenantId, ActorTenantId = context.User.TenantId,
                ActorObjectId = context.User.ObjectId, Action = operation, TargetType = "user", TargetId = targetId,
                Outcome = outcome, FailureCategory = error, GraphCorrelationId = correlation, GraphRequestId = requestId,
                Timestamp = utcNow(), SafeMetadataJson = "{}"
            }, cancellationToken);
            return null;
        }
        catch (OperationCanceledException) { throw; }
        catch { return "audit_persistence_failed"; }
    }

    private static int StatusCodeFor(UserSessionCommandResult result) => result.Status switch
    {
        "succeeded" => StatusCodes.Status200OK, "denied" => StatusCodes.Status403Forbidden,
        "invalid_target" => StatusCodes.Status400BadRequest, "idempotency_key_reused" => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable
    };
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
