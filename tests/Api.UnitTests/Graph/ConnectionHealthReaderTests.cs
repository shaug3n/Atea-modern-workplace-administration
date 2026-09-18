using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class ConnectionHealthReaderTests
{
    [Theory]
    [InlineData(true, false, false, false, "", ConnectionHealthStatus.Connected)]
    [InlineData(false, true, false, false, "", ConnectionHealthStatus.ConsentRequired)]
    [InlineData(false, false, true, false, "", ConnectionHealthStatus.PermissionIncomplete)]
    [InlineData(false, false, false, true, "", ConnectionHealthStatus.ConsentRevoked)]
    [InlineData(false, false, false, false, "connection_failed", ConnectionHealthStatus.ConnectionFailed)]
    [InlineData(false, false, false, false, "", ConnectionHealthStatus.TemporarilyUnavailable)]
    public async Task Maps_probe_results_to_safe_connection_states(bool success, bool consent, bool incomplete, bool revoked, string problem, ConnectionHealthStatus expected)
    {
        var probe = new RecordingProbe(new DelegatedConnectionProbeResult(success, ["User.Read"], consent, incomplete, revoked, string.IsNullOrEmpty(problem) ? null : problem));
        var result = await new ConnectionHealthReader(probe).ReadAsync(Guid.NewGuid());

        result.Status.Should().Be(expected);
        probe.Scopes.Should().Equal(GraphScopeCatalog.V1DelegatedScopes);
    }

    private sealed class RecordingProbe(DelegatedConnectionProbeResult result) : IDelegatedConnectionProbe
    {
        public IReadOnlyCollection<string> Scopes { get; private set; } = [];
        public Task<DelegatedConnectionProbeResult> ProbeAsync(Guid tenantId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default) { Scopes = scopes; return Task.FromResult(result); }
    }
}
