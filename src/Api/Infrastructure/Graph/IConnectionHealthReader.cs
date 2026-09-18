namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IConnectionHealthReader
{
    Task<ConnectionHealthReadResult> ReadAsync(Guid tenantId, CancellationToken cancellationToken = default);
}

public sealed record ConnectionHealthReadResult(ConnectionHealthStatus Status, IReadOnlyCollection<string> GrantedScopes, string? ProblemCategory = null);

public enum ConnectionHealthStatus
{
    Connected,
    ConsentRequired,
    PermissionIncomplete,
    ConsentRevoked,
    ConnectionFailed,
    TemporarilyUnavailable
}

public interface IDelegatedConnectionProbe
{
    Task<DelegatedConnectionProbeResult> ProbeAsync(Guid tenantId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default);
}

public sealed record DelegatedConnectionProbeResult(bool IsSuccessful, IReadOnlyCollection<string> GrantedScopes, bool ConsentRequired = false, bool PermissionIncomplete = false, bool ConsentRevoked = false, string? ProblemCategory = null);
