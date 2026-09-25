using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public interface IDeviceRecoveryService
{
    Task<RecoveryResult<IReadOnlyList<BitlockerRecoveryMetadata>>> GetBitlockerMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken);
    Task<RecoveryResult<LapsMetadata>> GetLapsMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken);
    Task<RecoveryResult<BitlockerSecret>> RevealBitlockerAsync(WorkspaceContext context, string deviceId, string keyId, string reason, CancellationToken cancellationToken);
    Task<RecoveryResult<LapsSecret>> RevealLapsAsync(WorkspaceContext context, string deviceId, string reason, CancellationToken cancellationToken);
}

public sealed class DeviceRecoveryService(
    IManagedDeviceDetailReader detailReader,
    IGraphDeviceRecoveryReader recoveryReader,
    IGraphAuthorizationSnapshotReader authorizationReader,
    IAuditWriter auditWriter) : IDeviceRecoveryService
{
    public async Task<RecoveryResult<IReadOnlyList<BitlockerRecoveryMetadata>>> GetBitlockerMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(deviceId)) return new("invalid_target");
        try
        {
            var target = await detailReader.GetAsync(deviceId, cancellationToken);
            if (TargetProblem<IReadOnlyList<BitlockerRecoveryMetadata>>(target) is { } problem) return problem;
            var snapshot = await authorizationReader.ReadAsync(context, cancellationToken);
            var useSecretScope = HasScope(snapshot, "BitlockerKey.Read.All");
            if (ScopeProblem<IReadOnlyList<BitlockerRecoveryMetadata>>(snapshot, "BitlockerKey.ReadBasic.All", "BitlockerKey.Read.All") is { } scopeProblem) return scopeProblem;
            var result = await recoveryReader.ListBitlockerAsync(target.Value!.AzureAdDeviceId!, useSecretScope, cancellationToken);
            if (result.Error is not null) return GraphProblem<IReadOnlyList<BitlockerRecoveryMetadata>>(result.Error, snapshot, false);
            return result.Value.Count == 0 ? new("recovery_not_found") : new("succeeded", result.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new("temporarily_unavailable"); }
    }

    public async Task<RecoveryResult<LapsMetadata>> GetLapsMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(deviceId)) return new("invalid_target");
        try
        {
            var target = await detailReader.GetAsync(deviceId, cancellationToken);
            if (TargetProblem<LapsMetadata>(target) is { } problem) return problem;
            var snapshot = await authorizationReader.ReadAsync(context, cancellationToken);
            var useSecretScope = HasScope(snapshot, "DeviceLocalCredential.Read.All");
            if (ScopeProblem<LapsMetadata>(snapshot, "DeviceLocalCredential.ReadBasic.All", "DeviceLocalCredential.Read.All") is { } scopeProblem) return scopeProblem;
            var result = await recoveryReader.GetLapsMetadataAsync(target.Value!.AzureAdDeviceId!, useSecretScope, cancellationToken);
            return result.Error is not null ? GraphProblem<LapsMetadata>(result.Error, snapshot, false) : new("succeeded", result.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch { return new("temporarily_unavailable"); }
    }

    public async Task<RecoveryResult<BitlockerSecret>> RevealBitlockerAsync(WorkspaceContext context, string deviceId, string keyId, string reason, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason)) return new("reason_required");
        if (!DeviceTarget.IsSafe(deviceId) || !DeviceTarget.IsSafe(keyId)) return new("invalid_target");
        RecoveryResult<BitlockerSecret> outcome;
        string? entraId = null;
        try
        {
            var target = await detailReader.GetAsync(deviceId, cancellationToken);
            if (TargetProblem<BitlockerSecret>(target) is { } problem) outcome = problem;
            else
            {
                entraId = target.Value!.AzureAdDeviceId;
                var snapshot = await authorizationReader.ReadAsync(context, cancellationToken);
                if (ScopeProblem<BitlockerSecret>(snapshot, "BitlockerKey.Read.All") is { } scopeProblem) outcome = scopeProblem;
                else
                {
                    // A caller-supplied key ID never authorizes a cross-device read.
                    var keys = await recoveryReader.ListBitlockerAsync(entraId!, true, cancellationToken);
                    if (keys.Error is not null) outcome = GraphProblem<BitlockerSecret>(keys.Error, snapshot, true);
                    else if (!keys.Value.Any(key => string.Equals(key.Id, keyId, StringComparison.OrdinalIgnoreCase))) outcome = new("recovery_not_found");
                    else
                    {
                        var secret = await recoveryReader.GetBitlockerAsync(keyId, cancellationToken);
                        outcome = secret.Error is not null
                            ? GraphProblem<BitlockerSecret>(secret.Error, snapshot, true)
                            : new("succeeded", secret.Value, GraphCorrelationId: secret.CorrelationId, GraphRequestId: secret.RequestId);
                    }
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { outcome = new("temporarily_unavailable"); }
        return await AuditRevealAsync(context, deviceId, entraId, keyId, "bitlocker", reason.Trim(), outcome, cancellationToken);
    }

    public async Task<RecoveryResult<LapsSecret>> RevealLapsAsync(WorkspaceContext context, string deviceId, string reason, CancellationToken cancellationToken)
    {
        if (!ValidReason(reason)) return new("reason_required");
        if (!DeviceTarget.IsSafe(deviceId)) return new("invalid_target");
        RecoveryResult<LapsSecret> outcome;
        string? entraId = null;
        try
        {
            var target = await detailReader.GetAsync(deviceId, cancellationToken);
            if (TargetProblem<LapsSecret>(target) is { } problem) outcome = problem;
            else
            {
                entraId = target.Value!.AzureAdDeviceId;
                var snapshot = await authorizationReader.ReadAsync(context, cancellationToken);
                if (ScopeProblem<LapsSecret>(snapshot, "DeviceLocalCredential.Read.All") is { } scopeProblem) outcome = scopeProblem;
                else
                {
                    var secret = await recoveryReader.GetLapsSecretAsync(entraId!, cancellationToken);
                    outcome = secret.Error is not null
                        ? GraphProblem<LapsSecret>(secret.Error, snapshot, true, lapsSecret: true)
                        : new("succeeded", secret.Value, GraphCorrelationId: secret.CorrelationId, GraphRequestId: secret.RequestId);
                }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch { outcome = new("temporarily_unavailable"); }
        return await AuditRevealAsync(context, deviceId, entraId, null, "windows_laps", reason.Trim(), outcome, cancellationToken);
    }

    private async Task<RecoveryResult<T>> AuditRevealAsync<T>(WorkspaceContext context, string managedId, string? entraId, string? recordId, string type, string reason, RecoveryResult<T> outcome, CancellationToken cancellationToken)
    {
        try
        {
            await auditWriter.WriteAsync(new AuditEvent
            {
                WorkspaceId = context.Membership.WorkspaceId,
                TenantId = context.User.TenantId,
                ActorTenantId = context.User.TenantId,
                ActorObjectId = context.User.ObjectId,
                Action = "devices.recovery.reveal",
                TargetType = "managed_device",
                TargetId = managedId,
                Outcome = outcome.Status,
                Timestamp = DateTimeOffset.UtcNow,
                GraphCorrelationId = outcome.GraphCorrelationId,
                GraphRequestId = outcome.GraphRequestId,
                FailureCategory = outcome.Status == "succeeded" ? null : outcome.Status,
                SafeMetadataJson = JsonSerializer.Serialize(new { reason, secretType = type, entraDeviceId = entraId, recoveryRecordId = recordId })
            }, cancellationToken);
            return outcome;
        }
        catch (OperationCanceledException) { throw; }
        catch { return new RecoveryResult<T>("audit_unavailable"); }
    }

    private static bool ValidReason(string? reason) => !string.IsNullOrWhiteSpace(reason) && reason.Trim().Length <= 500 && !reason.Any(char.IsControl);

    private static RecoveryResult<T>? TargetProblem<T>(GraphReadResult<ManagedDeviceSummary?> result) =>
        result.Error is not null ? GraphProblem<T>(result.Error, null, false)
        : result.Value is null ? new("device_not_found")
        : string.IsNullOrWhiteSpace(result.Value.AzureAdDeviceId) ? new("entra_device_missing")
        : null;

    private static bool HasScope(GraphAuthorizationSnapshot snapshot, string scope) => snapshot.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    private static RecoveryResult<T>? ScopeProblem<T>(GraphAuthorizationSnapshot snapshot, params string[] alternatives)
    {
        if (!snapshot.IsAvailable || alternatives.Any(scope => HasScope(snapshot, scope))) return null;
        var hasConsentProblem = alternatives.Any(scope => snapshot.ScopeProblems?.TryGetValue(scope, out var category) == true && category == "consent_required");
        var hasTransientProblem = alternatives.Any(scope => snapshot.ScopeProblems?.TryGetValue(scope, out var category) == true && category == "temporarily_unavailable");
        return new(hasConsentProblem ? "consent_required" : hasTransientProblem ? "temporarily_unavailable" : "missing_scope", Error: string.Join(", ", alternatives));
    }

    private static RecoveryResult<T> GraphProblem<T>(GraphOperationResult error, GraphAuthorizationSnapshot? snapshot, bool reveal, bool lapsSecret = false)
    {
        var status = error.Category switch
        {
            "not_found" or "recovery_not_found" => "recovery_not_found",
            "not_authorized" => "graph_forbidden",
            "consent_required" => "consent_required",
            "throttled" => "throttled",
            "invalid_target" => "invalid_target",
            _ => "temporarily_unavailable"
        };
        var eligible = reveal && status == "graph_forbidden" && snapshot?.DirectoryRoles.Any(role =>
            role.AssignmentState == DirectoryRoleAssignmentState.Eligible &&
            (role.RoleTemplateId == EntraRoleCatalog.CloudDeviceAdministratorTemplateId || role.RoleTemplateId == EntraRoleCatalog.IntuneAdministratorTemplateId ||
             (!lapsSecret && role.RoleTemplateId == EntraRoleCatalog.GlobalReaderTemplateId))) == true;
        return new(status, Guidance: eligible ? "pim_activation_required" : null,
            RetryAfterSeconds: error.RetryAfter is null ? null : (int)Math.Ceiling(error.RetryAfter.Value.TotalSeconds),
            GraphCorrelationId: error.CorrelationId, GraphRequestId: error.RequestId);
    }
}
