using System.Data;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public enum WorkspaceAccessMutationResult
{
    Updated,
    NotFound,
    FinalAdministrator
}

public interface IWorkspaceAccessRepository
{
    Task<(IReadOnlyList<WorkspaceMembership> Memberships, IReadOnlyList<PlatformInvitation> Invitations)> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<PlatformInvitation?> GetInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> SetModuleGrantsAsync(Guid workspaceId, Guid membershipId, IReadOnlyCollection<string> moduleKeys, AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> TransferOwnershipAsync(Guid workspaceId, Guid currentOwnerObjectId, Guid newOwnerId, IReadOnlyCollection<string> currentEnabledModules, AuditEvent auditEvent, CancellationToken cancellationToken = default);
    Task<bool> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<bool> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, AuditEvent auditEvent, CancellationToken cancellationToken = default) => RevokeInvitationAsync(workspaceId, invitationId, cancellationToken);
    Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, AuditEvent auditEvent, CancellationToken cancellationToken = default) => ChangeRoleAsync(workspaceId, membershipId, role, cancellationToken);
    Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, AuditEvent auditEvent, CancellationToken cancellationToken = default) => RemoveMembershipAsync(workspaceId, membershipId, cancellationToken);
}

public sealed class WorkspaceAccessRepository(WorkplaceDbContext db, IAuditWriter auditWriter) : IWorkspaceAccessRepository
{
    public async Task<(IReadOnlyList<WorkspaceMembership> Memberships, IReadOnlyList<PlatformInvitation> Invitations)> ListAsync(
        Guid workspaceId,
        CancellationToken cancellationToken = default)
    {
        var memberships = await db.WorkspaceMemberships.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderBy(x => x.Email)
            .ToArrayAsync(cancellationToken);
        var invitations = await db.PlatformInvitations.AsNoTracking()
            .Where(x => x.WorkspaceId == workspaceId)
            .OrderByDescending(x => x.CreatedAt)
            .ToArrayAsync(cancellationToken);
        return (memberships, invitations);
    }

    public async Task<bool> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default)
        => await RevokeInvitationAsync(workspaceId, invitationId, auditEvent: null, cancellationToken);

    public async Task<bool> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, AuditEvent? auditEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var count = await db.PlatformInvitations
            .Where(x => x.WorkspaceId == workspaceId && x.Id == invitationId && x.RevokedAt == null && x.RedeemedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), cancellationToken);
        if (count != 1) return false;
        if (auditEvent is not null) await auditWriter.WriteAsync(auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<PlatformInvitation?> GetInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default) =>
        db.PlatformInvitations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == invitationId,
            cancellationToken);

    public Task<WorkspaceMembership?> GetMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default) =>
        db.WorkspaceMemberships.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == membershipId, cancellationToken);

    public async Task<WorkspaceAccessMutationResult> SetModuleGrantsAsync(Guid workspaceId, Guid membershipId, IReadOnlyCollection<string> moduleKeys, AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == membershipId && !x.IsAteaOperator, cancellationToken);
        if (membership is null) return WorkspaceAccessMutationResult.NotFound;
        membership.ModuleGrantsJson = System.Text.Json.JsonSerializer.Serialize(Atea.UnifiedWorkplace.Api.Authorization.WorkspaceModuleCatalog.Normalize(moduleKeys));
        await db.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceAccessMutationResult.Updated;
    }

    public async Task<WorkspaceAccessMutationResult> TransferOwnershipAsync(Guid workspaceId, Guid currentOwnerObjectId, Guid newOwnerId, IReadOnlyCollection<string> currentEnabledModules, AuditEvent auditEvent, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var oldOwner = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.TenantObjectId == currentOwnerObjectId && !x.IsAteaOperator, cancellationToken);
        var newOwner = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId && x.Id == newOwnerId && !x.IsAteaOperator, cancellationToken);
        if (oldOwner is null || newOwner is null || oldOwner.Id == newOwner.Id || !Atea.UnifiedWorkplace.Api.Authorization.WorkspaceModuleCatalog.IsOwner(oldOwner.PlatformRole) || !Atea.UnifiedWorkplace.Api.Authorization.WorkspaceModuleCatalog.IsCustomerAdministrator(newOwner.PlatformRole))
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkspaceAccessMutationResult.NotFound;
        }
        oldOwner.PlatformRole = "customer_admin";
        oldOwner.ModuleGrantsJson = System.Text.Json.JsonSerializer.Serialize(Atea.UnifiedWorkplace.Api.Authorization.WorkspaceModuleCatalog.Normalize(currentEnabledModules));
        newOwner.PlatformRole = "workspace_owner";
        auditEvent.TargetId = newOwner.Id.ToString("D");
        auditEvent.SafeMetadataJson = System.Text.Json.JsonSerializer.Serialize(new { previousOwnerMembershipId = oldOwner.Id, newOwnerMembershipId = newOwner.Id });
        await db.SaveChangesAsync(cancellationToken);
        await auditWriter.WriteAsync(auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceAccessMutationResult.Updated;
    }

    public Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role, remove: false, auditEvent: null, cancellationToken);

    public Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, AuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role, remove: false, auditEvent, cancellationToken);

    public Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role: null, remove: true, auditEvent: null, cancellationToken);

    public Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, AuditEvent auditEvent, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role: null, remove: true, auditEvent, cancellationToken);

    private async Task<WorkspaceAccessMutationResult> MutateMembershipAsync(
        Guid workspaceId,
        Guid membershipId,
        string? role,
        bool remove,
        AuditEvent? auditEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == membershipId,
            cancellationToken);
        if (membership is null || membership.IsAteaOperator)
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkspaceAccessMutationResult.NotFound;
        }

        if (Atea.UnifiedWorkplace.Api.Authorization.WorkspaceModuleCatalog.IsOwner(membership.PlatformRole))
        {
            await transaction.RollbackAsync(cancellationToken);
            return WorkspaceAccessMutationResult.FinalAdministrator;
        }

        if (IsCustomerAdministrator(membership.PlatformRole)
            && (remove || !string.Equals(role, "customer_admin", StringComparison.OrdinalIgnoreCase)))
        {
            var otherAdministrators = await db.WorkspaceMemberships.AsNoTracking()
                .CountAsync(x => x.WorkspaceId == workspaceId
                    && x.Id != membershipId
                    && !x.IsAteaOperator
                    && new[] { "customer_admin", "customeradmin", "admin", "workspace-manager", "owner", "workspace_owner" }.Contains(x.PlatformRole.ToLower()), cancellationToken);
            if (otherAdministrators == 0)
            {
                await transaction.RollbackAsync(cancellationToken);
                return WorkspaceAccessMutationResult.FinalAdministrator;
            }
        }

        if (remove)
        {
            db.WorkspaceMemberships.Remove(membership);
        }
        else
        {
            membership.PlatformRole = role!;
        }
        await db.SaveChangesAsync(cancellationToken);
        if (auditEvent is not null) await auditWriter.WriteAsync(auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceAccessMutationResult.Updated;
    }

    private static bool IsCustomerAdministrator(string role) => IsAdministratorRole(role);

    private static bool IsAdministratorRole(string role) => role.Equals("customer_admin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("customeradmin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("admin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("workspace-manager", StringComparison.OrdinalIgnoreCase)
        || role.Equals("owner", StringComparison.OrdinalIgnoreCase)
        || role.Equals("workspace_owner", StringComparison.OrdinalIgnoreCase);
}
