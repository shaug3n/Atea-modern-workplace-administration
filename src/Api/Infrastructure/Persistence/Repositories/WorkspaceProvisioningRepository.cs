using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceProvisioningRepository(WorkplaceDbContext db) : IWorkspaceProvisioningRepository
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
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return workspace;
        }
        catch (DbUpdateException exception) when (ContainsUniqueViolation(exception))
        {
            throw new WorkspaceUniqueConstraintException("Workspace tenant already exists.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceProvisioningDatabaseException("Workspace persistence is temporarily unavailable.", exception);
        }
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
    {
        if (!await db.Workspaces.AnyAsync(workspace => workspace.Id == workspaceId, cancellationToken)) throw new KeyNotFoundException("Workspace not found.");
        var membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator, CreatedAt = DateTimeOffset.UtcNow };
        db.WorkspaceMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return membership;
        }
        catch (DbUpdateException exception) when (ContainsUniqueViolation(exception))
        {
            throw new WorkspaceUniqueConstraintException("Workspace membership already exists.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceProvisioningDatabaseException("Workspace persistence is temporarily unavailable.", exception);
        }
    }

    private static bool ContainsUniqueViolation(Exception exception) =>
        exception is PostgresException postgres && postgres.SqlState == PostgresErrorCodes.UniqueViolation ||
        exception.InnerException is not null && ContainsUniqueViolation(exception.InnerException);
}
