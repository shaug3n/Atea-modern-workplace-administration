using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class GraphAuthorizationSnapshotReaderTests
{
    [Fact]
    public async Task Null_pim_status_fails_closed_in_the_authorization_snapshot()
    {
        var snapshot = await ReadEligibilityAsync("\"status\":null");

        snapshot.DirectoryRoles.Single().Pim!.State.Should().Be(CapabilityState.TemporarilyUnavailable);
    }

    [Fact]
    public async Task Blank_pim_status_fails_closed_in_the_authorization_snapshot()
    {
        var snapshot = await ReadEligibilityAsync("\"status\":\"   \"");

        snapshot.DirectoryRoles.Single().Pim!.State.Should().Be(CapabilityState.TemporarilyUnavailable);
    }

    [Fact]
    public async Task Absent_pim_status_fails_closed_in_the_authorization_snapshot()
    {
        var snapshot = await ReadEligibilityAsync(null);

        snapshot.DirectoryRoles.Single().Pim!.State.Should().Be(CapabilityState.TemporarilyUnavailable);
    }

    private static async Task<GraphAuthorizationSnapshot> ReadEligibilityAsync(string? statusProperty)
    {
        var transport = new RecordingGraphTransport(
        [
            Success("{\"id\":\"user-1\"}"),
            Success("{\"value\":[{\"id\":\"definition-1\",\"templateId\":\"template-1\",\"displayName\":\"Test role\"}]}"),
            Success("{\"value\":[]}"),
            Success($"{{\"value\":[{{\"id\":\"eligibility-1\",\"principalId\":\"user-1\",\"roleDefinitionId\":\"definition-1\",\"directoryScopeId\":\"/\"{(statusProperty is null ? string.Empty : $",{statusProperty}")} }}]}}")
        ]);

        var reader = new GraphAuthorizationSnapshotReader(new RecordingGraphClientFactory(transport));

        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        return await reader.ReadAsync(
            new WorkspaceContext(
                new AuthenticatedUser(tenantId, objectId, "user@example.com", "Test user", "Member"),
                new WorkspaceMembership(Guid.NewGuid(), "Test workspace")),
            CancellationToken.None);
    }

    private static GraphTransportResponse Success(string content) =>
        new(GraphOperationResult.Success(), content, 1, new Dictionary<string, IReadOnlyCollection<string>>());

    private sealed class RecordingGraphClientFactory(RecordingGraphTransport transport) : IDelegatedGraphClientFactory
    {
        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken) =>
            Task.FromResult(new GraphClientLease(transport, scopes));
    }

    private sealed class RecordingGraphTransport(IEnumerable<GraphTransportResponse> responses) : IGraphTransport
    {
        private readonly Queue<GraphTransportResponse> responses = new(responses);

        public IReadOnlyCollection<string> Scopes => GraphScopeCatalog.AuthorizationReadScopes;

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(responses.Dequeue());
    }
}
