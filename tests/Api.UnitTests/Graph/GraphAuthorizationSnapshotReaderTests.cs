using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class GraphAuthorizationSnapshotReaderTests
{
    [Fact]
    public async Task Pim_eligibility_without_an_expiry_date_requires_activation()
    {
        var snapshot = await ReadEligibilityAsync(null);

        snapshot.DirectoryRoles.Single().Pim!.State.Should().Be(PimRequirement.ActivationRequired);
    }

    [Fact]
    public async Task Expired_pim_eligibility_is_reported_from_end_date()
    {
        var snapshot = await ReadEligibilityAsync("2000-01-01T00:00:00Z");

        snapshot.DirectoryRoles.Single().Pim!.State.Should().Be(PimRequirement.EligibilityExpired);
    }

    [Fact]
    public async Task Reuses_a_recent_snapshot_for_the_same_user_and_workspace()
    {
        var transport = new RecordingGraphTransport(
        [
            Success("{\"id\":\"user-1\"}"),
            Success("{\"value\":[]}"),
            Success("{\"value\":[]}"),
            Success("{\"value\":[]}")
        ]);
        var factory = new RecordingGraphClientFactory(transport);
        var reader = new GraphAuthorizationSnapshotReader(factory, new DelegatedScopeAvailabilityReader(factory));
        var context = new WorkspaceContext(
            new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "user@example.com", "Test user", "Member"),
            new WorkspaceMembership(Guid.NewGuid(), "Test workspace"));

        await reader.ReadAsync(context);
        await reader.ReadAsync(context);

        transport.SendCount.Should().Be(4);
    }

    [Fact]
    public async Task Probes_the_new_session_and_privileged_device_scopes()
    {
        var transport = new RecordingGraphTransport(
        [
            Success("{\"id\":\"user-1\"}"),
            Success("{\"value\":[]}"),
            Success("{\"value\":[]}"),
            Success("{\"value\":[]}")
        ]);
        var factory = new RecordingGraphClientFactory(transport);
        var reader = new GraphAuthorizationSnapshotReader(factory, new DelegatedScopeAvailabilityReader(factory));

        await reader.ReadAsync(new WorkspaceContext(
            new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "user@example.com", "Test user", "Member"),
            new WorkspaceMembership(Guid.NewGuid(), "Test workspace")));

        factory.RequestedScopes.Should().Contain(scope => scope.SequenceEqual(GraphScopeCatalog.UserSessionWriteScopes));
        factory.RequestedScopes.Should().Contain(scope => scope.SequenceEqual(GraphScopeCatalog.DevicePrivilegedOperationScopes));
    }

    private static async Task<GraphAuthorizationSnapshot> ReadEligibilityAsync(string? endDateTime)
    {
        var transport = new RecordingGraphTransport(
        [
            Success("{\"id\":\"user-1\"}"),
            Success("{\"value\":[{\"id\":\"definition-1\",\"templateId\":\"template-1\",\"displayName\":\"Test role\"}]}"),
            Success("{\"value\":[]}"),
            Success($"{{\"value\":[{{\"id\":\"eligibility-1\",\"principalId\":\"user-1\",\"roleDefinitionId\":\"definition-1\",\"directoryScopeId\":\"/\"{(endDateTime is null ? string.Empty : $",\"endDateTime\":\"{endDateTime}\"")} }}]}}")
        ]);

        var factory = new RecordingGraphClientFactory(transport);
        var reader = new GraphAuthorizationSnapshotReader(factory, new DelegatedScopeAvailabilityReader(factory));

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
        public List<IReadOnlyCollection<string>> RequestedScopes { get; } = [];

        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken) =>
            Capture(scopes);

        private Task<GraphClientLease> Capture(IReadOnlyCollection<string> scopes)
        {
            RequestedScopes.Add(scopes);
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingGraphTransport(IEnumerable<GraphTransportResponse> responses) : IGraphTransport
    {
        private readonly Queue<GraphTransportResponse> responses = new(responses);

        public int SendCount { get; private set; }

        public IReadOnlyCollection<string> Scopes => GraphScopeCatalog.AuthorizationReadScopes;

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Send());

        private GraphTransportResponse Send()
        {
            SendCount++;
            return responses.Dequeue();
        }
    }
}
