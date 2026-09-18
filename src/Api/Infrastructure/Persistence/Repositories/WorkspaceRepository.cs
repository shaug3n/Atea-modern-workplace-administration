using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceRepository(WorkplaceDbContext db, Guid scopedWorkspaceId) : IWorkspaceRepository
{
    public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        IsInScope(workspaceId) ? db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken) : Task.FromResult<Workspace?>(null);

    public async Task<Workspace> AddAsync(Guid workspaceId, Guid tenantId, string displayName, CancellationToken cancellationToken = default)
    {
        EnsureScope(workspaceId);
        var now = DateTimeOffset.UtcNow;
        var workspace = new Workspace { Id = workspaceId, TenantId = tenantId, DisplayName = displayName, ConnectionStatus = "awaiting_invitation", CreatedAt = now, UpdatedAt = now };
        db.Workspaces.Add(workspace);
        await db.SaveChangesAsync(cancellationToken);
        return workspace;
    }

    public async Task UpdateConnectionAsync(Guid workspaceId, string status, string consentScopesJson, DateTimeOffset? lastVerifiedAt, CancellationToken cancellationToken = default)
    {
        EnsureScope(workspaceId);
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken) ?? throw new KeyNotFoundException("Workspace not found.");
        workspace.ConnectionStatus = status;
        workspace.UpdatedAt = DateTimeOffset.UtcNow;
        var connection = await db.TenantConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        if (connection is null) db.TenantConnections.Add(new TenantConnection { WorkspaceId = workspaceId, Status = status, ConsentScopesJson = consentScopesJson, LastVerifiedAt = lastVerifiedAt, UpdatedAt = DateTimeOffset.UtcNow });
        else { connection.Status = status; connection.ConsentScopesJson = consentScopesJson; connection.LastVerifiedAt = lastVerifiedAt; connection.UpdatedAt = DateTimeOffset.UtcNow; }
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
    {
        EnsureScope(workspaceId);
        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId, cancellationToken)) throw new KeyNotFoundException("Workspace not found.");
        var membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator, CreatedAt = DateTimeOffset.UtcNow };
        db.WorkspaceMemberships.Add(membership);
        await db.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, Guid tenantObjectId, CancellationToken cancellationToken = default) =>
        IsInScope(workspaceId) ? db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.TenantObjectId == tenantObjectId, cancellationToken) : Task.FromResult<WorkspaceMembership?>(null);

    public async Task DeleteAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        EnsureScope(workspaceId);
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken);
        if (workspace is null) return;
        db.Workspaces.Remove(workspace);
        await db.SaveChangesAsync(cancellationToken);
    }

    private bool IsInScope(Guid workspaceId) => workspaceId == scopedWorkspaceId;
    private void EnsureScope(Guid workspaceId) { if (!IsInScope(workspaceId)) throw new UnauthorizedAccessException("Workspace is outside the repository scope."); }
}
