using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IWorkspaceProvisioningRepository
{
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
}
