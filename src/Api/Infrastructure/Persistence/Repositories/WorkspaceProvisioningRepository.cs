using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using PlatformWorkspaceScope = Atea.UnifiedWorkplace.Api.Authorization.PlatformWorkspaceScope;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Data;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceProvisioningRepository(WorkplaceDbContext db, IAuditWriter auditWriter) : IWorkspaceProvisioningRepository
{
    public async Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default)
    {
        if (!workspaceScope.IsAll && workspaceScope.WorkspaceIds.Count == 0) return [];
        var query = db.Workspaces.AsNoTracking().Include(workspace => workspace.Memberships).AsQueryable();
        if (!workspaceScope.IsAll) query = query.Where(workspace => workspaceScope.WorkspaceIds.Contains(workspace.Id));
        return await query.OrderBy(workspace => workspace.DisplayName).ToListAsync(cancellationToken);
    }

    public async Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default)
    {
        if (!workspaceScope.IsAll && !workspaceScope.WorkspaceIds.Contains(workspaceId)) return null;
        return await db.Workspaces.AsNoTracking()
            .Where(workspace => workspace.Id == workspaceId)
            .Select(workspace => new WorkspaceAdminDetailDto(
                workspace.Id,
                workspace.TenantId,
                workspace.DisplayName,
                workspace.TenantConnection == null ? workspace.ConnectionStatus : workspace.TenantConnection.Status,
                workspace.TenantConnection == null ? null : workspace.TenantConnection.LastVerifiedAt,
                workspace.TenantConnection == null ? null : workspace.TenantConnection.LastFailureCategory,
                workspace.Memberships.OrderBy(membership => membership.Email).Select(membership => new WorkspaceMembershipDto(membership.Id, membership.TenantObjectId, membership.Email, membership.PlatformRole, membership.IsAteaOperator)).ToArray(),
                db.PlatformInvitations.Where(invitation => invitation.WorkspaceId == workspace.Id).OrderByDescending(invitation => invitation.CreatedAt).Select(invitation => new InvitationSummaryDto(invitation.Id, invitation.Email, invitation.DisplayName, invitation.ExpiresAt, invitation.RedeemedAt, invitation.Role, invitation.RevokedAt)).ToArray()))
            .SingleOrDefaultAsync(cancellationToken);
    }

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

    public async Task<Workspace> CreateWithInvitationAsync(Workspace workspace, PlatformInvitation invitation, AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        invitation.WorkspaceId = workspace.Id;
        auditEvent.WorkspaceId = workspace.Id;
        auditEvent.TenantId = workspace.TenantId;
        auditEvent.TargetId = workspace.Id.ToString("D");
        db.Workspaces.Add(workspace);
        db.PlatformInvitations.Add(invitation);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await auditWriter.WriteAsync(auditEvent, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return workspace;
        }
        catch (DbUpdateException exception) when (ContainsUniqueViolation(exception))
        {
            throw new WorkspaceUniqueConstraintException("Workspace tenant already exists.", exception);
        }
        catch (DbUpdateException exception)
        {
            throw new WorkspaceProvisioningDatabaseException("Workspace onboarding persistence is temporarily unavailable.", exception);
        }
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
        => await AddMembershipAsync(workspaceId, tenantObjectId, email, platformRole, isAteaOperator, auditEvent: null, cancellationToken);

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent? auditEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken);
        if (workspace is null) throw new KeyNotFoundException("Workspace not found.");
        var membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantObjectId = tenantObjectId, Email = email, PlatformRole = platformRole, IsAteaOperator = isAteaOperator, CreatedAt = DateTimeOffset.UtcNow };
        db.WorkspaceMemberships.Add(membership);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            if (auditEvent is not null)
            {
                auditEvent.WorkspaceId = workspaceId;
                auditEvent.TenantId = workspace.TenantId;
                auditEvent.TargetId = membership.Id.ToString("D");
                await auditWriter.WriteAsync(auditEvent, cancellationToken);
            }
            await transaction.CommitAsync(cancellationToken);
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
