using System.Text.Json;
using System.Data;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public sealed class WorkspaceOnboardingRepository(WorkplaceDbContext db) : IOnboardingRepository, IInvitationRepository, IInvitationReadRepository, IConsentChallengeRepository
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

    public async Task CreateInvitationAsync(InvitationConsentChallengeRecord challenge, CancellationToken cancellationToken = default)
    {
        db.ConsentChallenges.Add(new ConsentChallenge
        {
            StateHash = challenge.StateHash,
            WorkspaceId = challenge.WorkspaceId,
            TenantId = challenge.TenantId,
            InvitationId = challenge.InvitationId,
            Purpose = challenge.Purpose,
            CorrelationId = challenge.CorrelationId,
            ExpiresAt = challenge.ExpiresAt
        });
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<InvitationConsentChallengeRecord?> FindInvitationAsync(string stateHash, CancellationToken cancellationToken = default)
    {
        return await db.ConsentChallenges.AsNoTracking()
            .Where(x => x.StateHash == stateHash)
            .Select(x => new InvitationConsentChallengeRecord(
                x.StateHash,
                x.WorkspaceId,
                x.TenantId,
                x.InvitationId ?? Guid.Empty,
                x.Purpose,
                x.CorrelationId,
                x.ExpiresAt,
                x.ConsumedAt,
                x.Invitation == null ? null : x.Invitation.RedeemedAt,
                x.Invitation == null ? null : x.Invitation.RedeemedByTenantObjectId,
                x.Invitation == null ? null : x.Invitation.RevokedAt,
                x.Invitation == null ? null : x.Invitation.ExpiresAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> TryConsumeInvitationAsync(
        string stateHash,
        Guid invitationId,
        Guid workspaceId,
        Guid tenantId,
        Guid redeemerObjectId,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        return await db.ConsentChallenges
            .Where(x => x.StateHash == stateHash &&
                x.InvitationId == invitationId &&
                x.WorkspaceId == workspaceId &&
                x.TenantId == tenantId &&
                x.Purpose == "invitation" &&
                x.ConsumedAt == null &&
                x.ExpiresAt > now &&
                x.Invitation != null &&
                x.Invitation.RedeemedByTenantObjectId == redeemerObjectId &&
                x.Invitation.RedeemedAt != null &&
                x.Invitation.RevokedAt == null &&
                x.Invitation.ExpiresAt > now &&
                x.Invitation.Workspace.TenantId == tenantId)
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.ConsumedAt, now), cancellationToken) == 1;
    }

    public async Task<InvitationLookup?> FindByNonceHashAsync(string nonceHash, CancellationToken cancellationToken = default)
    {
        return await db.PlatformInvitations.AsNoTracking()
            .Where(x => x.NonceHash == nonceHash)
            .Select(x => new InvitationLookup(
                x.Id,
                x.WorkspaceId,
                x.Workspace.DisplayName,
                x.Workspace.TenantId,
                x.Role,
                x.ExpiresAt,
                x.RedeemedAt != null,
                x.RevokedAt != null,
                x.RedeemedByTenantObjectId))
            .SingleOrDefaultAsync(cancellationToken);
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
        => await CreateAsync(invitation, auditEvent: null, cancellationToken);

    public async Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, AuditEvent? auditEvent, CancellationToken cancellationToken = default)
    {
        var transaction = db.Database.CurrentTransaction;
        var ownsTransaction = transaction is null;
        if (ownsTransaction) transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        await using var ownedTransaction = ownsTransaction ? transaction : null;
        var normalizedEmail = invitation.Email.Trim().ToLowerInvariant();
        var now = DateTimeOffset.UtcNow;
        await db.PlatformInvitations
            .Where(x => x.WorkspaceId == invitation.WorkspaceId
                && x.Email.ToLower() == normalizedEmail
                && x.RedeemedAt == null
                && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), cancellationToken);

        invitation.Email = invitation.Email.Trim();
        db.PlatformInvitations.Add(invitation);
        if (auditEvent is not null)
        {
            auditEvent.WorkspaceId = invitation.WorkspaceId;
            auditEvent.TenantId = await db.Workspaces.Where(x => x.Id == invitation.WorkspaceId).Select(x => x.TenantId).SingleAsync(cancellationToken);
            auditEvent.TargetId = invitation.Id.ToString("D");
            db.AuditEvents.Add(auditEvent);
        }
        await db.SaveChangesAsync(cancellationToken);
        if (ownsTransaction) await transaction!.CommitAsync(cancellationToken);
        return invitation;
    }

    public async Task<bool> ReissueAsync(
        PlatformInvitation invitation,
        Guid replacedInvitationId,
        AuditEvent? auditEvent,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var revoked = await db.PlatformInvitations
            .Where(x => x.Id == replacedInvitationId &&
                x.WorkspaceId == invitation.WorkspaceId &&
                x.RedeemedAt == null &&
                x.RevokedAt == null)
            .ExecuteUpdateAsync(updates => updates.SetProperty(x => x.RevokedAt, now), cancellationToken);
        if (revoked != 1) return false;

        await CreateAsync(invitation, auditEvent, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public Task<InvitationRedemption?> RedeemAsync(
        string nonceHash,
        Guid tenantId,
        Guid tenantObjectId,
        string? email,
        string displayName,
        CancellationToken cancellationToken = default) =>
        RedeemAsync(nonceHash, tenantId, tenantObjectId, email, displayName, invitationStateHash: null, cancellationToken);

    public async Task<InvitationRedemption?> RedeemAsync(
        string nonceHash,
        Guid tenantId,
        Guid tenantObjectId,
        string? email,
        string displayName,
        string? invitationStateHash,
        CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;

        if (invitationStateHash is not null)
        {
            var challengeMatchesInvitation = await db.ConsentChallenges.AnyAsync(x =>
                x.StateHash == invitationStateHash &&
                x.Purpose == "invitation" &&
                x.ConsumedAt == null &&
                x.ExpiresAt > now &&
                x.InvitationId != null &&
                x.Invitation != null &&
                x.Invitation.NonceHash == nonceHash &&
                x.Invitation.Id == x.InvitationId &&
                x.Invitation.WorkspaceId == x.WorkspaceId &&
                x.TenantId == tenantId &&
                x.Invitation.Workspace.TenantId == tenantId &&
                x.Invitation.RevokedAt == null &&
                x.Invitation.ExpiresAt > now,
                cancellationToken);
            if (!challengeMatchesInvitation) return null;
        }

        var claimed = await db.PlatformInvitations
            .Where(x => x.NonceHash == nonceHash && x.RedeemedAt == null && x.RevokedAt == null && x.ExpiresAt > now && x.Workspace.TenantId == tenantId && (x.ApprovedTenantObjectId == tenantObjectId || (x.ApprovedTenantObjectId == null && email != null && x.Email.ToLower() == email.Trim().ToLower())))
            .ExecuteUpdateAsync(updates => updates
                .SetProperty(x => x.RedeemedAt, now)
                .SetProperty(x => x.RedeemedByTenantObjectId, tenantObjectId), cancellationToken);
        if (claimed != 1)
        {
            if (invitationStateHash is null) return null;

            var recoveredInvitation = await db.PlatformInvitations.AsNoTracking()
                .Include(x => x.Workspace)
                .SingleOrDefaultAsync(x =>
                    x.NonceHash == nonceHash &&
                    x.RedeemedAt != null &&
                    x.RedeemedByTenantObjectId == tenantObjectId &&
                    x.RevokedAt == null &&
                    x.ExpiresAt > now &&
                    x.Workspace.TenantId == tenantId,
                    cancellationToken);
            if (recoveredInvitation is null) return null;

            var challengeStillValid = await db.ConsentChallenges.AnyAsync(x =>
                x.StateHash == invitationStateHash &&
                x.Purpose == "invitation" &&
                x.ConsumedAt == null &&
                x.ExpiresAt > now &&
                x.InvitationId == recoveredInvitation.Id &&
                x.WorkspaceId == recoveredInvitation.WorkspaceId &&
                x.TenantId == tenantId &&
                x.Invitation != null &&
                x.Invitation.RedeemedByTenantObjectId == tenantObjectId &&
                x.Invitation.RevokedAt == null &&
                x.Invitation.ExpiresAt > now &&
                x.Invitation.Workspace.TenantId == tenantId,
                cancellationToken);
            if (!challengeStillValid) return null;

            var existingMembership = await db.WorkspaceMemberships.AsNoTracking()
                .SingleOrDefaultAsync(x => x.WorkspaceId == recoveredInvitation.WorkspaceId && x.TenantObjectId == tenantObjectId, cancellationToken);
            return existingMembership is null
                ? null
                : new InvitationRedemption(recoveredInvitation.Workspace, existingMembership);
        }

        var invitation = await db.PlatformInvitations.Include(x => x.Workspace).SingleAsync(x => x.NonceHash == nonceHash, cancellationToken);
        await db.PlatformInvitations
            .Where(x => x.WorkspaceId == invitation.WorkspaceId
                && x.Id != invitation.Id
                && x.Email.ToLower() == invitation.Email.ToLower()
                && x.RedeemedAt == null
                && x.RevokedAt == null)
            .ExecuteUpdateAsync(update => update.SetProperty(x => x.RevokedAt, now), cancellationToken);
        var membership = await db.WorkspaceMemberships.SingleOrDefaultAsync(x => x.WorkspaceId == invitation.WorkspaceId && x.TenantObjectId == tenantObjectId, cancellationToken);
        if (membership is null)
        {
            membership = new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = invitation.WorkspaceId, TenantObjectId = tenantObjectId, Email = email ?? $"object:{tenantObjectId}", PlatformRole = invitation.Role, ModuleGrantsJson = invitation.ModuleKeysJson, CreatedAt = DateTimeOffset.UtcNow };
            db.WorkspaceMemberships.Add(membership);
        }
        else if (!membership.IsAteaOperator)
        {
            membership.PlatformRole = invitation.Role;
            membership.ModuleGrantsJson = invitation.ModuleKeysJson;
        }

        db.AuditEvents.Add(new AuditEvent
        {
            Id = Guid.NewGuid(), WorkspaceId = invitation.WorkspaceId, TenantId = tenantId,
            ActorTenantId = tenantId, ActorObjectId = tenantObjectId,
            Action = "workspace.invitation.redeemed", TargetType = "invitation", TargetId = invitation.Id.ToString("D"),
            Outcome = "success", Timestamp = now,
            SafeMetadataJson = JsonSerializer.Serialize(new { resource = "invitation", role = invitation.Role })
        });
        if (invitation.Workspace.ConnectionStatus == Atea.UnifiedWorkplace.Api.Features.Workspaces.ConnectionState.AwaitingInvitation)
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
