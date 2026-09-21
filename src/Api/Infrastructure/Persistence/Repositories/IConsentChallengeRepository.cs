namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

public interface IConsentChallengeRepository
{
    Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default);
    Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default);
}
