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
    Task<UserCommandResult> SetAccountEnabledAsync(WorkspaceContext context, string userObjectId, SetAccountEnabledCommand command, string idempotencyKey, CancellationToken cancellationToken);
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
    Func<string>? temporaryPasswordGenerator = null,
    Func<DateTimeOffset>? utcNow = null) : IUserCommandService
{
    private readonly Func<string> temporaryPasswordGenerator = temporaryPasswordGenerator ?? GenerateTemporaryPassword;
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserCommandResult> CreateAsync(WorkspaceContext context, CreateUserCommand command, string idempotencyKey, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(context, Capability.UsersCreate, cancellationToken);
        if (authorization.State != CapabilityState.Allowed)
        {
            return await DenyAsync(context, Capability.UsersCreate, "new", authorization, cancellationToken);
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
            cancellationToken);
    }

    public Task<UserCommandResult> UpdateAsync(WorkspaceContext context, string userObjectId, UpdateUserCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
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
            cancellationToken);

    public Task<UserCommandResult> SetAccountEnabledAsync(WorkspaceContext context, string userObjectId, SetAccountEnabledCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.UsersDisable,
            command.Enabled ? "users.reactivate" : "users.disable",
            command,
            idempotencyKey,
            () => userCommands.SetAccountEnabledAsync(userObjectId, command.Enabled, idempotencyKey, cancellationToken),
            cancellationToken);

    public Task<UserCommandResult> AddGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.GroupsManageMembers,
            "users.groups.add",
            command,
            idempotencyKey,
            () => groupCommands.AddMemberAsync(command.GroupObjectId, userObjectId, idempotencyKey, cancellationToken),
            cancellationToken);

    public Task<UserCommandResult> RemoveGroupAsync(WorkspaceContext context, string userObjectId, GroupMembershipCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.GroupsManageMembers,
            "users.groups.remove",
            command,
            idempotencyKey,
            () => groupCommands.RemoveMemberAsync(command.GroupObjectId, userObjectId, idempotencyKey, cancellationToken),
            cancellationToken);

    public Task<UserCommandResult> AssignLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.LicensesAssign,
            "users.licenses.assign",
            command,
            idempotencyKey,
            () => licenseCommands.AssignLicenseAsync(userObjectId, command, idempotencyKey, cancellationToken),
            cancellationToken);

    public Task<UserCommandResult> RemoveLicenseAsync(WorkspaceContext context, string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteVerifiedUserMutationAsync(
            context,
            userObjectId,
            Capability.LicensesAssign,
            "users.licenses.remove",
            command,
            idempotencyKey,
            () => licenseCommands.RemoveLicenseAsync(userObjectId, command.SkuId, idempotencyKey, cancellationToken),
            cancellationToken);

    private async Task<UserCommandResult> ExecuteVerifiedUserMutationAsync(
        WorkspaceContext context,
        string userObjectId,
        string capability,
        string operation,
        object requestPayload,
        string idempotencyKey,
        Func<Task<GraphOperationResult>> graphMutation,
        CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(context, capability, cancellationToken);
        if (authorization.State != CapabilityState.Allowed)
        {
            return await DenyAsync(context, capability, userObjectId, authorization, cancellationToken);
        }

        var user = await directoryReader.GetAsync(userObjectId, cancellationToken);
        if (user is null)
        {
            return new UserCommandResult(UserCommandStatus.NotFound, capability, Error: "user_not_found");
        }

        if (user.IsReadOnly)
        {
            var auditWarning = await AuditAsync(context, operation, userObjectId, UserCommandStatus.SourceOfAuthorityReadOnly, "source_of_authority_read_only", null, null, cancellationToken);
            return new UserCommandResult(
                UserCommandStatus.SourceOfAuthorityReadOnly,
                capability,
                Error: "source_of_authority_read_only",
                AuditWarning: auditWarning);
        }

        return await ExecuteAsync(
            context,
            operation,
            userObjectId,
            capability,
            requestPayload,
            idempotencyKey,
            async () => MapGraphResult(await graphMutation(), capability),
            cancellationToken);
    }

    private async Task<UserCommandResult> ExecuteAsync(
        WorkspaceContext context,
        string operation,
        string targetId,
        string capability,
        object requestPayload,
        string idempotencyKey,
        Func<Task<UserCommandResult>> execute,
        CancellationToken cancellationToken)
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
                    AuditWarning = await AuditAsync(context, operation, targetId, result.Status, result.Error, result.GraphCorrelationId, result.GraphRequestId, cancellationToken)
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
        CancellationToken cancellationToken)
    {
        var auditWarning = await AuditAsync(context, capability, targetId, UserCommandStatus.Denied, "capability_required", null, null, cancellationToken);
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
        CancellationToken cancellationToken)
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
                    SafeMetadataJson = "{}"
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
}
