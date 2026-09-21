using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceOnboardingRepository(WorkplaceDbContext db) : IOnboardingRepository, IInvitationRepository, IConsentChallengeRepository
{
    public async Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default)
    {
        db.ConsentChallenges.Add(new Entities.ConsentChallenge { StateHash = stateHash, WorkspaceId = workspaceId, TenantId = tenantId, CorrelationId = correlationId, ExpiresAt = expiresAt });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var consumed = await db.ConsentChallenges
            .Where(x => x.StateHash == stateHash && x.WorkspaceId == workspaceId && x.TenantId == tenantId && x.ConsumedAt == null && x.ExpiresAt > now)
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.ConsumedAt, now), cancellationToken);
        return consumed == 1;
    }
    public async Task<ConnectionSnapshot?> GetConnectionAsync(Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var workspace = await db.Workspaces.AsNoTracking().Include(x => x.TenantConnection).SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken);
        if (workspace is null) return null;
        var connection = workspace.TenantConnection;
        return new ConnectionSnapshot(workspace.Id, workspace.ConnectionStatus, connection?.LastVerifiedAt, ParseScopes(connection?.ConsentScopesJson), connection?.LastFailureCategory);
    }

    public async Task<ConnectionSnapshot> UpdateConnectionAsync(Guid workspaceId, string status, IReadOnlyCollection<string> consentScopes, DateTimeOffset? lastVerifiedAt, string? failureCategory, CancellationToken cancellationToken = default)
    {
        var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == workspaceId, cancellationToken) ?? throw new KeyNotFoundException("Workspace not found.");
        var now = DateTimeOffset.UtcNow;
        workspace.ConnectionStatus = status;
        workspace.UpdatedAt = now;
        var connection = await db.TenantConnections.SingleOrDefaultAsync(x => x.WorkspaceId == workspaceId, cancellationToken);
        if (connection is null)
        {
            connection = new TenantConnection { WorkspaceId = workspaceId };
            db.TenantConnections.Add(connection);
        }
        connection.Status = status;
        connection.ConsentScopesJson = JsonSerializer.Serialize(consentScopes);
        connection.LastVerifiedAt = lastVerifiedAt;
        connection.LastFailureCategory = failureCategory;
        connection.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        return new ConnectionSnapshot(workspace.Id, workspace.ConnectionStatus, connection.LastVerifiedAt, consentScopes, failureCategory);
    }

    public async Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default)
    {
        db.PlatformInvitations.Add(invitation);
        await db.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var claimed = await db.PlatformInvitations
            .Where(x => x.NonceHash == nonceHash && x.RedeemedAt == null && x.ExpiresAt > now && x.Workspace.TenantId == tenantId && (x.ApprovedTenantObjectId == tenantObjectId || (x.ApprovedTenantObjectId == null && email != null && x.Email.ToLower() == email.ToLower())))
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.RedeemedAt, now), cancellationToken);
        if (claimed != 1) return null;

        var invitation = await db.PlatformInvitations.Include(x => x.Workspace).SingleAsync(x => x.NonceHash == nonceHash, cancellationToken);
        var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == invitation.WorkspaceId && x.TenantObjectId == tenantObjectId, cancellationToken);
        if (membership is null)
        {
            membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = invitation.WorkspaceId, TenantObjectId = tenantObjectId, Email = email ?? $"object:{tenantObjectId}", PlatformRole = "customer_admin", CreatedAt = DateTimeOffset.UtcNow };
            db.WorkspaceMemberships.Add(membership);
        }
        invitation.Workspace.ConnectionStatus = Atea.UnifiedWorkplace.Api.Features.Workspaces.ConnectionState.ConsentRequired;
        invitation.Workspace.UpdatedAt = now;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new InvitationRedemption(invitation.Workspace, membership);
    }

    private static IReadOnlyCollection<string> ParseScopes(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; } catch (JsonException) { return []; }
    }
}
