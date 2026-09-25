namespace Atea.UnifiedWorkplace.Api.Authorization;

public sealed record AuthenticatedUser(
    Guid TenantId,
    Guid ObjectId,
    string UserPrincipalName,
    string DisplayName,
    string UserType,
    Guid? HomeTenantId = null);

public interface IWorkspaceMembershipReader
{
    Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default);
}

public sealed record WorkspaceMembership(
    Guid WorkspaceId,
    string WorkspaceName,
    string PlatformRole = "member",
    bool IsAteaOperator = false,
    IReadOnlyCollection<string>? ModuleKeys = null);
