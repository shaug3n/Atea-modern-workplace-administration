using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IWorkspaceRepository
{
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Workspace> AddAsync(Guid workspaceId, Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task UpdateConnectionAsync(Guid workspaceId, string status, string consentScopesJson, DateTimeOffset? lastVerifiedAt, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, Guid tenantObjectId, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record ConnectionSnapshot(Guid WorkspaceId, string ConnectionStatus, DateTimeOffset? LastVerifiedAt, IReadOnlyCollection<string> ConsentScopes, string? FailureCategory);
public sealed record InvitationRedemption(Workspace Workspace, WorkspaceMembership Membership);

public interface IOnboardingRepository
{
    Task<ConnectionSnapshot?> GetConnectionAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<ConnectionSnapshot> UpdateConnectionAsync(Guid workspaceId, string status, IReadOnlyCollection<string> consentScopes, DateTimeOffset? lastVerifiedAt, string? failureCategory, CancellationToken cancellationToken = default);
}

public interface IInvitationRepository
{
    Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default);
    Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string email, string displayName, CancellationToken cancellationToken = default);
}
