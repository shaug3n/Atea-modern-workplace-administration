using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewGraphReaderTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace"));

    [Fact]
    public async Task ReadUserCountAsync_uses_the_selected_single_user_read_scope_and_count_query()
    {
        var transport = new RecordingTransport(Response("{\"@odata.count\":42}"));
        var factory = new RecordingFactory(transport);

        var result = await new OverviewGraphReader(factory).ReadUserCountAsync(
            Workspace,
            "User.Read.All",
            CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.Should().Be(42);
        factory.RequestedScopes.Should().ContainSingle().Which.Should().Equal("User.Read.All");
        transport.Requests.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new GraphRequest(HttpMethod.Get, "/v1.0/users?$count=true&$top=1",
                Headers: new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" }));
    }

    [Fact]
    public async Task ReadLicenseCountsAsync_uses_directory_read_all_for_both_counts()
    {
        var transport = new RecordingTransport(
            Response("{\"@odata.count\":42}"),
            Response("{\"@odata.count\":17}"));
        var factory = new RecordingFactory(transport);

        var result = await new OverviewGraphReader(factory).ReadLicenseCountsAsync(Workspace, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.Should().Be(new OverviewLicenseCounts(42, 17));
        factory.RequestedScopes.Should().ContainSingle().Which.Should().Equal("Directory.Read.All");
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v1.0/users?$count=true&$top=1",
            "/v1.0/users?$filter=assignedLicenses/$count%20ne%200&$count=true&$top=1");
        transport.Requests.Should().OnlyContain(request =>
            request.Headers != null && request.Headers.GetValueOrDefault("ConsistencyLevel") == "eventual");
    }

    [Theory]
    [InlineData("{}", "{\"@odata.count\":1}")]
    [InlineData("{\"@odata.count\":\"12\"}", "{\"@odata.count\":1}")]
    [InlineData("{\"@odata.count\":-1}", "{\"@odata.count\":0}")]
    [InlineData("{\"@odata.count\":1}", "{\"@odata.count\":2}")]
    public async Task Invalid_or_inconsistent_graph_counts_are_not_presented_as_zero(string total, string assigned)
    {
        var transport = new RecordingTransport(Response(total), Response(assigned));

        var result = await new OverviewGraphReader(new RecordingFactory(transport))
            .ReadLicenseCountsAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_response");
        result.Error.IsSuccess.Should().BeFalse();
    }

    private static GraphTransportResponse Response(string content) =>
        new(GraphOperationResult.Success(), content, 1, new Dictionary<string, IReadOnlyCollection<string>>());

    private sealed class RecordingFactory(RecordingTransport transport) : IDelegatedGraphClientFactory
    {
        public List<IReadOnlyCollection<string>> RequestedScopes { get; } = [];

        public Task<GraphClientLease> CreateForCurrentUserAsync(
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken)
        {
            RequestedScopes.Add(scopes.ToArray());
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingTransport(params GraphTransportResponse[] responses) : IGraphTransport
    {
        private int index;
        public IReadOnlyCollection<string> Scopes => [];
        public List<GraphRequest> Requests { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses[index++]);
        }
    }
}
