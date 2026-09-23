using System.Data;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
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
    Task<bool> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, CancellationToken cancellationToken = default);
    Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceAccessRepository(WorkplaceDbContext db) : IWorkspaceAccessRepository
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
    {
        var now = DateTimeOffset.UtcNow;
        var count = await db.PlatformInvitations
            .Where(x => x.WorkspaceId == workspaceId && x.Id == invitationId && x.RevokedAt == null && x.RedeemedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), cancellationToken);
        return count == 1;
    }

    public Task<PlatformInvitation?> GetInvitationAsync(Guid workspaceId, Guid invitationId, CancellationToken cancellationToken = default) =>
        db.PlatformInvitations.AsNoTracking().SingleOrDefaultAsync(
            x => x.WorkspaceId == workspaceId && x.Id == invitationId,
            cancellationToken);

    public Task<WorkspaceAccessMutationResult> ChangeRoleAsync(Guid workspaceId, Guid membershipId, string role, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role, remove: false, cancellationToken);

    public Task<WorkspaceAccessMutationResult> RemoveMembershipAsync(Guid workspaceId, Guid membershipId, CancellationToken cancellationToken = default) =>
        MutateMembershipAsync(workspaceId, membershipId, role: null, remove: true, cancellationToken);

    private async Task<WorkspaceAccessMutationResult> MutateMembershipAsync(
        Guid workspaceId,
        Guid membershipId,
        string? role,
        bool remove,
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

        if (IsCustomerAdministrator(membership.PlatformRole)
            && (remove || !string.Equals(role, "customer_admin", StringComparison.OrdinalIgnoreCase)))
        {
            var otherAdministrators = await db.WorkspaceMemberships.AsNoTracking()
                .CountAsync(x => x.WorkspaceId == workspaceId
                    && x.Id != membershipId
                    && !x.IsAteaOperator
                    && new[] { "customer_admin", "customeradmin", "admin", "workspace-manager", "owner" }.Contains(x.PlatformRole.ToLower()), cancellationToken);
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
        await transaction.CommitAsync(cancellationToken);
        return WorkspaceAccessMutationResult.Updated;
    }

    private static bool IsCustomerAdministrator(string role) => IsAdministratorRole(role);

    private static bool IsAdministratorRole(string role) => role.Equals("customer_admin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("customeradmin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("admin", StringComparison.OrdinalIgnoreCase)
        || role.Equals("workspace-manager", StringComparison.OrdinalIgnoreCase)
        || role.Equals("owner", StringComparison.OrdinalIgnoreCase);
}
