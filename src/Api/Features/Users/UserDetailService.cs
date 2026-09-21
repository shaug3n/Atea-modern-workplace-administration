using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public interface IUserDetailService
{
    Task<UserDetailResult> GetDetailAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<UserDetailSection<AssignedLicense>?> GetLicensesAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<UserDetailSection<GroupMembership>?> GetGroupsAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<UserDetailSection<DirectoryRoleAssignment>?> GetRolesAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<UserDetailSection<PimEligibility>?> GetPimAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
}

public sealed class UserDetailService(
    IUserDirectoryReader directoryReader,
    IUserLicenseReader licenseReader,
    IGroupMembershipReader groupReader,
    IRoleAndPimReader roleReader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    Func<DateTimeOffset>? utcNow = null) : IUserDetailService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserDetailResult> GetDetailAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var capabilities = CapabilityEvaluator.Evaluate(snapshot, context.Membership);
        var usersView = capabilities[Capability.UsersView];
        if (usersView.State != CapabilityState.Allowed)
        {
            return new UserDetailResult(
                UserDetailStatus.NotAccessible,
                Detail: new UserDetailResponse(
                    Access(usersView, UserDirectoryFreshness.Unavailable, true, Error("capability_required", "User details cannot be read for the current capability state.", usersView.State)),
                    null,
                    EmptySection<AssignedLicense>(capabilities[Capability.LicensesAssign]),
                    EmptySection<GroupMembership>(capabilities[Capability.GroupsManageMembers]),
                    EmptySection<DirectoryRoleAssignment>(capabilities[Capability.RolesAssign]),
                    EmptySection<PimEligibility>(capabilities[Capability.PimActivate])));
        }

        var user = await ReadUserAsync(context, userObjectId, cancellationToken);
        if (user.Error is not null)
        {
            var error = UserVerificationError(user.Error, usersView.State);
            return new UserDetailResult(
                UserDetailStatus.Found,
                new UserDetailResponse(
                    Access(usersView, FreshnessFor(user.Error.Category), true, error),
                    null,
                    EmptySection<AssignedLicense>(capabilities[Capability.LicensesAssign], UserVerificationError(user.Error, capabilities[Capability.LicensesAssign].State), FreshnessFor(user.Error.Category)),
                    EmptySection<GroupMembership>(capabilities[Capability.GroupsManageMembers], UserVerificationError(user.Error, capabilities[Capability.GroupsManageMembers].State), FreshnessFor(user.Error.Category)),
                    EmptySection<DirectoryRoleAssignment>(capabilities[Capability.RolesAssign], UserVerificationError(user.Error, capabilities[Capability.RolesAssign].State), FreshnessFor(user.Error.Category)),
                    EmptySection<PimEligibility>(capabilities[Capability.PimActivate], UserVerificationError(user.Error, capabilities[Capability.PimActivate].State), FreshnessFor(user.Error.Category))));
        }

        if (user.Value is null)
        {
            return new UserDetailResult(UserDetailStatus.NotFound, Error: Error("user_not_found", "The user was removed or is no longer visible in the current tenant."));
        }

        if (user.Value.DirectoryTenantId is { } directoryTenantId && directoryTenantId != context.User.TenantId)
        {
            return new UserDetailResult(UserDetailStatus.NotFound, Error: Error("user_not_found", "The user was removed or is no longer visible in the current tenant."));
        }

        return new UserDetailResult(
            UserDetailStatus.Found,
            new UserDetailResponse(
                Access(usersView),
                user.Value,
                await ReadSectionAsync(userObjectId, capabilities[Capability.LicensesAssign], licenseReader.ReadUserLicensesAsync, cancellationToken),
                await ReadSectionAsync(userObjectId, capabilities[Capability.GroupsManageMembers], groupReader.ReadUserGroupsAsync, cancellationToken),
                await ReadSectionAsync(userObjectId, capabilities[Capability.RolesAssign], roleReader.ReadUserRoleAssignmentsAsync, cancellationToken),
                await ReadSectionAsync(userObjectId, capabilities[Capability.PimActivate], roleReader.ReadUserPimEligibilityAsync, cancellationToken)));
    }

    public Task<UserDetailSection<AssignedLicense>?> GetLicensesAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
        GetVerifiedSectionAsync(context, userObjectId, Capability.LicensesAssign, licenseReader.ReadUserLicensesAsync, cancellationToken);

    public Task<UserDetailSection<GroupMembership>?> GetGroupsAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
        GetVerifiedSectionAsync(context, userObjectId, Capability.GroupsManageMembers, groupReader.ReadUserGroupsAsync, cancellationToken);

    public Task<UserDetailSection<DirectoryRoleAssignment>?> GetRolesAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
        GetVerifiedSectionAsync(context, userObjectId, Capability.RolesAssign, roleReader.ReadUserRoleAssignmentsAsync, cancellationToken);

    public Task<UserDetailSection<PimEligibility>?> GetPimAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
        GetVerifiedSectionAsync(context, userObjectId, Capability.PimActivate, roleReader.ReadUserPimEligibilityAsync, cancellationToken);

    private async Task<UserDetailSection<T>?> GetVerifiedSectionAsync<T>(
        WorkspaceContext context,
        string userObjectId,
        string capability,
        Func<string, CancellationToken, Task<GraphReadResult<IReadOnlyList<T>>>> read,
        CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var capabilities = CapabilityEvaluator.Evaluate(snapshot, context.Membership);
        var usersView = capabilities[Capability.UsersView];
        if (usersView.State != CapabilityState.Allowed)
        {
            return EmptySection<T>(usersView, Error("capability_required", "User details cannot be read for the current capability state.", usersView.State));
        }

        var user = await ReadUserAsync(context, userObjectId, cancellationToken);
        if (user.Error is not null)
        {
            return EmptySection<T>(capabilities[capability], UserVerificationError(user.Error, capabilities[capability].State), FreshnessFor(user.Error.Category));
        }

        if (user.Value is null)
        {
            return null;
        }

        if (user.Value.DirectoryTenantId is { } directoryTenantId && directoryTenantId != context.User.TenantId)
        {
            return null;
        }

        return await ReadSectionAsync(userObjectId, capabilities[capability], read, cancellationToken);
    }

    private async Task<GraphReadResult<UserDetails?>> ReadUserAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        try
        {
            return GraphReadResult<UserDetails?>.Succeeded(await directoryReader.GetAsync(context, userObjectId, cancellationToken));
        }
        catch (GraphAdapterException exception) when (exception.Result.Category == "not_found")
        {
            return GraphReadResult<UserDetails?>.Succeeded(null);
        }
        catch (GraphAdapterException exception)
        {
            return GraphReadResult<UserDetails?>.Failed(exception.Result);
        }
    }

    private async Task<UserDetailSection<T>> ReadSectionAsync<T>(
        string userObjectId,
        CapabilityDecision authorization,
        Func<string, CancellationToken, Task<GraphReadResult<IReadOnlyList<T>>>> read,
        CancellationToken cancellationToken)
    {
        if (authorization.State == CapabilityState.Hidden)
        {
            return EmptySection<T>(authorization, Error("capability_required", "This section is not visible for the current capability state.", authorization.State));
        }

        var result = await read(userObjectId, cancellationToken);
        if (result.Error is null)
        {
            return new UserDetailSection<T>(Access(authorization), result.Value);
        }

        return new UserDetailSection<T>(
            Access(
                authorization,
                FreshnessFor(result.Error.Category),
                true,
                Error(result.Error.Category, MessageFor(result.Error.Category), authorization.State, result.Error.StatusCode, result.Error.RetryAfter)),
            []);
    }

    private UserDetailSection<T> EmptySection<T>(
        CapabilityDecision authorization,
        UserDirectoryError? error = null,
        string freshness = UserDirectoryFreshness.Unavailable) =>
        new(Access(authorization, freshness, true, error), []);

    private SectionAccessState Access(
        CapabilityDecision authorization,
        string freshness = UserDirectoryFreshness.Fresh,
        bool partialData = false,
        UserDirectoryError? error = null) =>
        new(authorization, utcNow(), freshness, partialData, error);

    private static UserDirectoryError Error(
        string category,
        string message,
        string? state = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null) =>
        new(category, message, state, statusCode, retryAfter is null ? null : (int)Math.Ceiling(retryAfter.Value.TotalSeconds));

    private static UserDirectoryError UserVerificationError(GraphOperationResult result, string state) =>
        Error(result.Category, VerificationMessageFor(result.Category), state, result.StatusCode, result.RetryAfter);

    private static string FreshnessFor(string category) => category switch
    {
        "throttled" => UserDirectoryFreshness.Stale,
        _ => UserDirectoryFreshness.Unavailable
    };

    private static string MessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to read this section.",
        "consent_required" => "Delegated Microsoft Graph consent is required to read this section.",
        "throttled" => "Microsoft Graph throttled this section request.",
        "not_found" => "The section data could not be found.",
        _ => "This section is temporarily unavailable."
    };

    private static string VerificationMessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to verify this user.",
        "consent_required" => "Delegated Microsoft Graph consent is required to verify this user.",
        "throttled" => "Microsoft Graph throttled user verification.",
        _ => "User verification is temporarily unavailable."
    };
}
