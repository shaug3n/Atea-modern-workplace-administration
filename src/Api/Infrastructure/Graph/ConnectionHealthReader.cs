namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class ConnectionHealthReader(IDelegatedConnectionProbe probe) : IConnectionHealthReader
{
    public async Task<ConnectionHealthReadResult> ReadAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        try
        {
            var result = await probe.ProbeAsync(tenantId, GraphScopeCatalog.V1DelegatedScopes, cancellationToken);
            var status = result.ConsentRevoked ? ConnectionHealthStatus.ConsentRevoked
                : result.ConsentRequired ? ConnectionHealthStatus.ConsentRequired
                : result.PermissionIncomplete ? ConnectionHealthStatus.PermissionIncomplete
                : string.Equals(result.ProblemCategory, "connection_failed", StringComparison.Ordinal) ? ConnectionHealthStatus.ConnectionFailed
                : result.IsSuccessful ? ConnectionHealthStatus.Connected
                : ConnectionHealthStatus.TemporarilyUnavailable;
            return new ConnectionHealthReadResult(status, result.GrantedScopes, result.ProblemCategory);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ConnectionHealthReadResult(ConnectionHealthStatus.TemporarilyUnavailable, [], "temporarily_unavailable");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return new ConnectionHealthReadResult(ConnectionHealthStatus.TemporarilyUnavailable, [], "temporarily_unavailable");
        }
    }
}

public sealed class UnconfiguredDelegatedConnectionProbe : IDelegatedConnectionProbe
{
    public Task<DelegatedConnectionProbeResult> ProbeAsync(Guid tenantId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) =>
        Task.FromResult(new DelegatedConnectionProbeResult(false, [], ProblemCategory: "temporarily_unavailable"));
}
