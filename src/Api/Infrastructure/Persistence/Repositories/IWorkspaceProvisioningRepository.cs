using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using PlatformWorkspaceScope = Atea.UnifiedWorkplace.Api.Authorization.PlatformWorkspaceScope;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IWorkspaceProvisioningRepository
{
    Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default);
    Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default);
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
}
