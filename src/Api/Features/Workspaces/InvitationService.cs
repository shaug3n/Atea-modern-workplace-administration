using System.Security.Cryptography;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record InvitationCreationResult(Guid InvitationId, string InvitationUrl, DateTimeOffset ExpiresAt);
public sealed record PreparedInvitation(PlatformInvitation Invitation, string InvitationUrl);

public sealed class InvitationService(
    IInvitationRepository repository,
    Uri publicBaseUri,
    ConsentChallengeService? challengeService = null)
{
    public Task<InvitationCreationResult> CreateAsync(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        CreateAsync(workspaceId, email, displayName, expiresAt, null, cancellationToken);

    public Task<InvitationCreationResult> CreateAsync(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, Guid? approvedTenantObjectId, CancellationToken cancellationToken = default) =>
        CreateForRoleAsync(workspaceId, email, displayName, expiresAt, "customer_admin", approvedTenantObjectId, cancellationToken);

    public async Task<InvitationCreationResult> CreateForRoleAsync(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, string role, Guid? approvedTenantObjectId = null, CancellationToken cancellationToken = default, AuditEvent? auditEvent = null, IReadOnlyCollection<string>? moduleKeys = null)
    {
        var prepared = PrepareForRole(workspaceId, email, displayName, expiresAt, role, approvedTenantObjectId, moduleKeys);
        var invitation = prepared.Invitation;
        if (auditEvent is not null) auditEvent.TargetId = invitation.Id.ToString("D");
        await (auditEvent is null
            ? repository.CreateAsync(invitation, cancellationToken)
            : repository.CreateAsync(invitation, auditEvent, cancellationToken));
        return new InvitationCreationResult(invitation.Id, prepared.InvitationUrl, invitation.ExpiresAt);
    }

    public PreparedInvitation PrepareForRole(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, string role, Guid? approvedTenantObjectId = null, IReadOnlyCollection<string>? moduleKeys = null)
    {
        if (role is not ("member" or "customer_admin" or "workspace_owner")) throw new ArgumentOutOfRangeException(nameof(role));
        var nonce = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = new PlatformInvitation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Email = email.Trim(), DisplayName = displayName.Trim(), ApprovedTenantObjectId = approvedTenantObjectId, Role = role,
            NonceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant(),
            ExpiresAt = expiresAt, CreatedAt = DateTimeOffset.UtcNow,
            ModuleKeysJson = System.Text.Json.JsonSerializer.Serialize(WorkspaceModuleCatalog.Normalize(moduleKeys))
        };
        return new PreparedInvitation(invitation, new Uri(publicBaseUri, $"invitations/{nonce}").ToString());
    }

    public async Task<InvitationRedemption?> RedeemDetailedAsync(string nonce, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default)
        => await RedeemDetailedAsync(nonce, tenantId, tenantObjectId, email, displayName, challenge: null, cancellationToken);

    public async Task<InvitationRedemption?> RedeemDetailedAsync(
        string nonce,
        Guid tenantId,
        Guid tenantObjectId,
        string? email,
        string displayName,
        string? challenge,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidNonce(nonce) || tenantId == Guid.Empty || tenantObjectId == Guid.Empty || challenge is { Length: > 4096 }) return null;
        string? stateHash = null;
        if (challenge is not null)
        {
            if (challengeService is null ||
                !challengeService.TryReadInvitation(challenge, out var payload) ||
                payload.TenantId != tenantId)
            {
                return null;
            }

            stateHash = ConsentChallengeService.HashState(challenge);
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
        return await repository.RedeemAsync(hash, tenantId, tenantObjectId, email, displayName, stateHash, cancellationToken);
    }

    public async Task<bool> RedeemAsync(string nonce, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default) =>
        await RedeemDetailedAsync(nonce, tenantId, tenantObjectId, email, displayName, cancellationToken) is not null;

    private static bool IsValidNonce(string nonce) => nonce.Length == 43 && nonce.All(character => char.IsLetterOrDigit(character) || character is '-' or '_');

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
