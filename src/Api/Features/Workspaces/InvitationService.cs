using System.Security.Cryptography;
using System.Text;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record InvitationCreationResult(Guid InvitationId, string InvitationUrl, DateTimeOffset ExpiresAt);

public sealed class InvitationService(IInvitationRepository repository, Uri publicBaseUri)
{
    public Task<InvitationCreationResult> CreateAsync(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
        CreateAsync(workspaceId, email, displayName, expiresAt, null, cancellationToken);

    public async Task<InvitationCreationResult> CreateAsync(Guid workspaceId, string email, string displayName, DateTimeOffset expiresAt, Guid? approvedTenantObjectId, CancellationToken cancellationToken = default)
    {
        var nonce = ToBase64Url(RandomNumberGenerator.GetBytes(32));
        var invitation = new PlatformInvitation
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceId, Email = email.Trim(), DisplayName = displayName.Trim(), ApprovedTenantObjectId = approvedTenantObjectId,
            NonceHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant(),
            ExpiresAt = expiresAt, CreatedAt = DateTimeOffset.UtcNow
        };
        await repository.CreateAsync(invitation, cancellationToken);
        return new InvitationCreationResult(invitation.Id, new Uri(publicBaseUri, $"invitations/{nonce}").ToString(), invitation.ExpiresAt);
    }

    public async Task<bool> RedeemAsync(string nonce, Guid tenantId, Guid tenantObjectId, string email, string displayName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nonce)) return false;
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
        return await repository.RedeemAsync(hash, tenantId, tenantObjectId, email, displayName, cancellationToken) is not null;
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
