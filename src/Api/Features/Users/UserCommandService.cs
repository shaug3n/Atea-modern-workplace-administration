using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Groups;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public interface IUserCommandService
{
    Task<UserCommandResult> CreateAsync(WorkspaceContext context, CreateUserCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<UserCommandResult> UpdateAsync(WorkspaceContext context, string userObjectId, UpdateUserCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<UserCommandResult> SetAccountEnabledAsync(WorkspaceContext context, string userObjectId, SetAccountEnabledCommand command, string idempotencyKey, CancellationToken cancellationToken, string? reason = null);
    Task<UserCommandResult> ResetPasswordAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null);
    Task<UserCommandResult> AddGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<UserCommandResult> RemoveGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<UserCommandResult> AssignLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken);
    Task<UserCommandResult> RemoveLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class UserCommandService(
    IUserDirectoryReader directoryReader,
    IUserLifecycleCommands userCommands,
    IGroupMembershipCommands groupCommands,
    ILicenseAssignmentCommands licenseCommands,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IIdempotencyService idempotency,
    IAuditWriter auditWriter,
    IGroupCatalogReader groupCatalogReader,
    ILicenseOverviewReader licenseCatalogReader,
    Func<string>? temporaryPasswordGenerator = null,
    Func<DateTimeOffset>? utcNow = null) : IUserCommandService
{
    private readonly Func<string> temporaryPasswordGenerator = temporaryPasswordGenerator ?? GenerateTemporaryPassword;
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserCommandResult> CreateAsync(WorkspaceContext context, CreateUserCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        var authorization = await AuthorizeAsync(context, Capability.UsersCreate, cancellationToken);
        if (authorization.State != CapabilityState.Allowed)
        {
            return await DenyAsync(context, Capability.UsersCreate, "new", authorization, cancellationToken, command.Reason);
        }

        var temporaryPassword = temporaryPasswordGenerator();
        var graphRequest = new GraphUserCreateRequest(
            command.DisplayName,
            command.GivenName,
            command.Surname,
            command.UserPrincipalName,
            command.MailNickname,
            command.JobTitle,
            command.Department,
            command.OfficeLocation,
            command.MobilePhone,
            command.UsageLocation,
            new TemporaryPasswordProfile(temporaryPassword, true),
            command.AccountEnabled);

        return await ExecuteAsync(
            context,
            "users.create",
            "new",
            Capability.UsersCreate,
            command,
            idempotencyKey,
            async () =>
            {
                var result = await userCommands.CreateUserAsync(graphRequest, idempotencyKey, cancellationToken);
                return MapGraphResult(result, Capability.UsersCreate, temporaryPassword);
            },
            cancellationToken,
            command.Reason);
    }

    public Task<UserCommandResult> UpdateAsync(WorkspaceContext context, string userObjectId, UpdateUserCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.UsersUpdate,
            "users.update",
            command,
            idempotencyKey,
            () => userCommands.UpdateProfileAsync(
                userObjectId,
                new GraphUserProfileUpdate(
                    command.DisplayName,
                    command.GivenName,
                    command.Surname,
                    command.JobTitle,
                    command.Department,
                    command.OfficeLocation,
                    command.MobilePhone,
                    command.UsageLocation,
                    command.AccountEnabled),
                idempotencyKey,
                cancellationToken),
            cancellationToken,
            reason: command.Reason);
    }

    public Task<UserCommandResult> SetAccountEnabledAsync(WorkspaceContext context, string userObjectId, SetAccountEnabledCommand command, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
    {
        reason = NormalizeReason(reason ?? command.Reason);
        command = command with { Reason = reason };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.UsersDisable,
            command.Enabled ? "users.reactivate" : "users.disable",
            command,
            idempotencyKey,
            () => userCommands.SetAccountEnabledAsync(userObjectId, command.Enabled, idempotencyKey, cancellationToken),
            cancellationToken,
            reason: reason);
    }

    public Task<UserCommandResult> ResetPasswordAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
    {
        reason = NormalizeReason(reason);
        string? temporaryPassword = null;
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.UsersResetPassword,
            "users.reset_password",
            new { forceChangePasswordNextSignIn = true, reason },
            idempotencyKey,
            () =>
            {
                temporaryPassword = temporaryPasswordGenerator();
                return userCommands.ResetPasswordAsync(
                    userObjectId,
                    new TemporaryPasswordProfile(temporaryPassword, true),
                    idempotencyKey,
                    cancellationToken);
            },
            cancellationToken,
            () => temporaryPassword,
            reason);
    }

    public Task<UserCommandResult> AddGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.GroupsManageMembers,
            "users.groups.add",
            command,
            idempotencyKey,
            () => groupCommands.AddMemberAsync(command.GroupObjectId, userObjectId, idempotencyKey, cancellationToken),
            cancellationToken,
            reason: command.Reason);
    }

    public Task<UserCommandResult> RemoveGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.GroupsManageMembers,
            "users.groups.remove",
            command,
            idempotencyKey,
            () => groupCommands.RemoveMemberAsync(command.GroupObjectId, userObjectId, idempotencyKey, cancellationToken),
            cancellationToken,
            reason: command.Reason);
    }

    public Task<UserCommandResult> AssignLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.LicensesAssign,
            "users.licenses.assign",
            command,
            idempotencyKey,
            () => licenseCommands.AssignLicenseAsync(userObjectId, command, idempotencyKey, cancellationToken),
            cancellationToken,
            reason: command.Reason);
    }

    public Task<UserCommandResult> RemoveLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        command = command with { Reason = NormalizeReason(command.Reason) };
        return ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.LicensesAssign,
            "users.licenses.remove",
            command,
            idempotencyKey,
            () => licenseCommands.RemoveLicenseAsync(userObjectId, command.SkuId, idempotencyKey, cancellationToken),
            cancellationToken,
            reason: command.Reason);
    }

    private async Task<UserCommandResult> ExecuteVerifiedUserMutationAsync(
        WorkspaceContext context,
        string userObjectId,
        string capability,
        string operation,
        object requestPayload,
        string idempotencyKey,
        Func<Task<GraphOperationResult>> graphMutation,
        CancellationToken cancellationToken,
        Func<string?>? temporaryPasswordProvider = null,
        string? reason = null)
    {
        var authorization = await AuthorizeAsync(context, capability, cancellationToken);
        if (authorization.State != CapabilityState.Allowed)
        {
            return await DenyAsync(context, capability, userObjectId, authorization, cancellationToken, reason);
        }

        var user = await directoryReader.GetAsync(context, userObjectId, cancellationToken);
        if (user is null)
        {
            return new UserCommandResult(UserCommandStatus.NotFound, capability, Error: "user_not_found");
        }

        if (user.DirectoryTenantId is { } directoryTenantId && directoryTenantId != context.User.TenantId)
        {
            return new UserCommandResult(UserCommandStatus.NotFound, capability, Error: "user_not_found");
        }

        if (user.IsReadOnly && operation == "users.update")
        {
            var auditWarning = await AuditAsync(context, operation, userObjectId, UserCommandStatus.SourceOfAuthorityReadOnly, "source_of_authority_read_only", null, null, cancellationToken, reason);
            return new UserCommandResult(
                UserCommandStatus.SourceOfAuthorityReadOnly,
                capability,
                Error: "source_of_authority_read_only",
                AuditWarning: auditWarning);
        }

        var catalogFailure = await ValidateCatalogTargetAsync(context, capability, requestPayload, cancellationToken);
        if (catalogFailure is not null)
        {
            return await AuditCatalogFailureAsync(context, operation, userObjectId, catalogFailure, cancellationToken, reason);
        }

        return await ExecuteAsync(
            context,
            operation,
            userObjectId,
            capability,
            requestPayload,
            idempotencyKey,
            async () => MapGraphResult(await graphMutation(), capability, temporaryPasswordProvider?.Invoke()),
            cancellationToken,
            reason);
    }

    private async Task<UserCommandResult?> ValidateCatalogTargetAsync(WorkspaceContext context, string capability, object requestPayload, CancellationToken cancellationToken)
    {
        if (requestPayload is GroupMembershipCommand groupCommand)
        {
            var result = await groupCatalogReader.ReadGroupAsync(groupCommand.GroupObjectId, cancellationToken);
            if (result.Error is not null) return CatalogFailure(result.Error, capability, "group");
            if (result.Value is null) return new UserCommandResult(UserCommandStatus.NotFound, capability, Error: "group_not_found");
        }

        if (requestPayload is LicenseAssignmentCommand licenseCommand)
        {
            var result = await licenseCatalogReader.ReadAsync(context, new LicenseOverviewQuery(), cancellationToken);
            if (result.Error is not null) return CatalogFailure(result.Error, capability, "license");
            if (!result.Value.Any(item => string.Equals(item.SkuId, licenseCommand.SkuId, StringComparison.OrdinalIgnoreCase))) return new UserCommandResult(UserCommandStatus.NotFound, capability, Error: "license_not_found");
        }

        return null;
    }

    private static UserCommandResult CatalogFailure(GraphOperationResult error, string capability, string targetType) => error.Category switch
    {
        "not_found" => new UserCommandResult(UserCommandStatus.NotFound, capability, Error: $"{targetType}_not_found"),
        "invalid_request" or "invalid_target" or "invalid_license" => new UserCommandResult(UserCommandStatus.InvalidTarget, capability, Error: error.Category),
        _ => new UserCommandResult(UserCommandStatus.TemporarilyUnavailable, capability, Error: error.Category, GraphCorrelationId: error.CorrelationId, GraphRequestId: error.RequestId)
    };

    private async Task<UserCommandResult> AuditCatalogFailureAsync(WorkspaceContext context, string operation, string targetId, UserCommandResult result, CancellationToken cancellationToken, string? reason)
    {
        return result with { AuditWarning = await AuditAsync(context, operation, targetId, result.Status, result.Error, result.GraphCorrelationId, result.GraphRequestId, cancellationToken, reason) };
    }

    private async Task<UserCommandResult> ExecuteAsync(
        WorkspaceContext context,
        string operation,
        string targetId,
        string capability,
        object requestPayload,
        string idempotencyKey,
        Func<Task<UserCommandResult>> execute,
        CancellationToken cancellationToken,
        string? reason)
    {
        UserCommandResult? liveResult = null;
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, operation, targetId, idempotencyKey),
            requestPayload,
            async () =>
            {
                var result = await execute();
                var auditedResult = result with
                {
                    AuditWarning = await AuditAsync(context, operation, targetId, result.Status, result.Error, result.GraphCorrelationId, result.GraphRequestId, cancellationToken, reason)
                };
                liveResult = auditedResult;
                return new IdempotentOperationResult(
                    StatusCodeFor(auditedResult),
                    auditedResult.Status,
                    JsonSerializer.Serialize(auditedResult with { TemporaryCredentialNotice = null }, JsonOptions),
                    auditedResult.GraphCorrelationId,
                    auditedResult.GraphRequestId);
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused)
        {
            return new UserCommandResult(
                UserCommandStatus.IdempotencyKeyReused,
                capability,
                Error: "idempotency_key_reused");
        }

        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
        {
            return new UserCommandResult(
                UserCommandStatus.TemporarilyUnavailable,
                capability,
                Error: "idempotency_in_progress");
        }

        if (outcome.Kind == IdempotencyOutcomeKind.Replayed)
        {
            var replayed = JsonSerializer.Deserialize<UserCommandResult>(outcome.Result.SafeResultJson, JsonOptions)
                ?? new UserCommandResult(UserCommandStatus.Succeeded, capability);
            return replayed with { Replayed = true, TemporaryCredentialNotice = null };
        }

        return JsonSerializer.Deserialize<UserCommandResult>(outcome.Result.SafeResultJson, JsonOptions) is { } stored
            ? RehydrateLiveResult(stored, liveResult)
            : new UserCommandResult(UserCommandStatus.TemporarilyUnavailable, capability, Error: "idempotency_result_unavailable");
    }

    private static UserCommandResult RehydrateLiveResult(UserCommandResult stored, UserCommandResult? live) =>
        live?.TemporaryCredentialNotice is null ? stored : stored with { TemporaryCredentialNotice = live.TemporaryCredentialNotice };

    private async Task<CapabilityDecision> AuthorizeAsync(WorkspaceContext context, string capability, CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        return CapabilityEvaluator.Evaluate(snapshot, context.Membership)[capability];
    }

    private async Task<UserCommandResult> DenyAsync(
        WorkspaceContext context,
        string capability,
        string targetId,
        CapabilityDecision authorization,
        CancellationToken cancellationToken,
        string? reason)
    {
        var auditWarning = await AuditAsync(context, capability, targetId, UserCommandStatus.Denied, "capability_required", null, null, cancellationToken, reason);
        return new UserCommandResult(
            UserCommandStatus.Denied,
            capability,
            Error: "capability_required",
            Authorization: authorization,
            AuditWarning: auditWarning);
    }

    private static UserCommandResult MapGraphResult(GraphOperationResult graph, string capability, string? temporaryPassword = null)
    {
        if (graph.IsSuccess)
        {
            return new UserCommandResult(
                UserCommandStatus.Succeeded,
                capability,
                TemporaryCredentialNotice: temporaryPassword is null ? null : new TemporaryCredentialNotice(temporaryPassword, true),
                GraphCorrelationId: graph.CorrelationId,
                GraphRequestId: graph.RequestId);
        }

        var status = graph.Category switch
        {
            "conflict" => UserCommandStatus.Conflict,
            "not_found" => UserCommandStatus.NotFound,
            "invalid_request" or "invalid_target" or "invalid_license" => UserCommandStatus.InvalidTarget,
            _ => UserCommandStatus.TemporarilyUnavailable
        };
        return new UserCommandResult(status, capability, Error: graph.Category, GraphCorrelationId: graph.CorrelationId, GraphRequestId: graph.RequestId);
    }

    private async Task<string?> AuditAsync(
        WorkspaceContext context,
        string action,
        string targetId,
        string result,
        string? failureCategory,
        string? graphCorrelationId,
        string? graphRequestId,
        CancellationToken cancellationToken,
        string? reason)
    {
        try
        {
            await auditWriter.WriteAsync(
                new AuditEvent
                {
                    WorkspaceId = context.Membership.WorkspaceId,
                    TenantId = context.User.TenantId,
                    ActorTenantId = context.User.TenantId,
                    ActorObjectId = context.User.ObjectId,
                    Action = action,
                    TargetType = "user",
                    TargetId = targetId,
                    Outcome = result,
                    Timestamp = utcNow(),
                    GraphCorrelationId = graphCorrelationId,
                    GraphRequestId = graphRequestId,
                    FailureCategory = failureCategory,
                    SafeMetadataJson = SafeReasonMetadata(reason)
                },
                cancellationToken);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return "audit_persistence_failed";
        }
    }

    private static int StatusCodeFor(UserCommandResult result) => result.Status switch
    {
        UserCommandStatus.Succeeded => StatusCodes.Status200OK,
        UserCommandStatus.Conflict => StatusCodes.Status409Conflict,
        UserCommandStatus.SourceOfAuthorityReadOnly => StatusCodes.Status409Conflict,
        UserCommandStatus.IdempotencyKeyReused => StatusCodes.Status409Conflict,
        UserCommandStatus.NotFound => StatusCodes.Status404NotFound,
        UserCommandStatus.Denied => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string GenerateTemporaryPassword()
    {
        Span<byte> bytes = stackalloc byte[18];
        System.Security.Cryptography.RandomNumberGenerator.Fill(bytes);
        return $"Atea-{Convert.ToBase64String(bytes).Replace('+', 'A').Replace('/', 'z')[..18]}!";
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static string? NormalizeReason(string? reason) => string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();

    private static string SafeReasonMetadata(string? reason) =>
        reason is null ? "{}" : JsonSerializer.Serialize(new { reason }, JsonOptions);
}
