using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public interface IWorkspaceProvisioningRepository
{
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
}

public interface IWorkspaceProvisioningService
{
    Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record WorkspaceProvisioningResult(Workspace? Workspace, bool IsConflict)
{
    public static WorkspaceProvisioningResult Created(Workspace workspace) => new(workspace, false);
    public static WorkspaceProvisioningResult Conflict() => new(null, true);
}

public sealed class WorkspaceAlreadyExistsException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class WorkspaceProvisioningService(IWorkspaceProvisioningRepository repository) : IWorkspaceProvisioningService
{
    public async Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default)
    {
        if (await repository.FindByTenantIdAsync(tenantId, cancellationToken) is not null) return WorkspaceProvisioningResult.Conflict();
        try
        {
            return WorkspaceProvisioningResult.Created(await repository.CreateAsync(tenantId, displayName, cancellationToken));
        }
        catch (DbUpdateException)
        {
            return WorkspaceProvisioningResult.Conflict();
        }
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
    {
        try
        {
            return await repository.AddMembershipAsync(workspaceId, tenantObjectId, email, platformRole, isAteaOperator, cancellationToken);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceAlreadyExistsException("Membership already exists.", exception);
        }
    }

    public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => repository.GetAsync(workspaceId, cancellationToken);
}

public sealed class EfWorkspaceProvisioningRepository(WorkplaceDbContext db) : IWorkspaceProvisioningRepository
{
    public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        db.Workspaces.AsNoTracking().SingleOrDefaultAsync(workspace => workspace.Id == workspaceId, cancellationToken);

    public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        db.Workspaces.AsNoTracking().SingleOrDefaultAsync(workspace => workspace.TenantId == tenantId, cancellationToken);

    public async Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var workspace = new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName, ConnectionStatus = "awaiting_invitation", CreatedAt = now, UpdatedAt = now };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
    {
        if (!await db.Workspaces.AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken)) throw new KeyNotFoundException("Workspace not found.");
        var membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator, CreatedAt = DateTimeOffset.UtcNow };
        db.WorkspaceMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        return membership;
    }
}
