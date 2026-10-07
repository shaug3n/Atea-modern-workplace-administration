namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IConsentChallengeRepository
{
    Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default);
    Task CreateInvitationAsync(InvitationConsentChallengeRecord challenge, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Invitation consent challenges are not supported by this repository.");
    Task<InvitationConsentChallengeRecord?> FindInvitationAsync(string stateHash, CancellationToken cancellationToken = default) =>
        Task.FromResult<InvitationConsentChallengeRecord?>(null);
    Task<bool> TryConsumeInvitationAsync(string stateHash, Guid invitationId, Guid workspaceId, Guid tenantId, Guid redeemerObjectId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}

public sealed record InvitationConsentChallengeRecord(
    string StateHash,
    Guid WorkspaceId,
    Guid TenantId,
    Guid InvitationId,
    string Purpose,
    string CorrelationId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? ConsumedAt);
