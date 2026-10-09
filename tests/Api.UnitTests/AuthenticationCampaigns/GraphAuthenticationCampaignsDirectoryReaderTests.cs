using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.AuthenticationCampaigns;

public sealed class GraphAuthenticationCampaignsDirectoryReaderTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace", "member"));

    [Fact]
    public async Task Reads_only_selected_directory_attributes_with_User_Read_All()
    {
        var transport = new RecordingTransport(Success("""{"value":[{"id":"user-1","department":"Engineering","officeLocation":"Oslo","companyName":"Contoso"}]}"""));
        var factory = new RecordingFactory(transport);

        var result = await new GraphAuthenticationCampaignsDirectoryReader(factory).ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Users["user-1"].Should().Be(new AuthenticationCampaignsDirectoryEntry("user-1", "Engineering", "Oslo", "Contoso"));
        factory.RequestedScopes.Should().Equal("User.Read.All");
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v1.0/users?$select=id,department,officeLocation,companyName&$top=999");
        transport.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
    }

    [Fact]
    public async Task Follows_directory_pages_and_preserves_rows_when_a_later_page_fails()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1","department":"Engineering"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=page2"}"""),
            Failure("temporarily_unavailable"));

        var result = await new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Users.Should().ContainKey("user-1");
        result.Users["user-1"].Department.Should().Be("Engineering");
        result.PartialData.Should().BeTrue();
        result.Error!.Category.Should().Be("temporarily_unavailable");
        transport.Requests[1].PathAndQuery.Should().Be("/v1.0/users?$skiptoken=page2");
    }

    [Fact]
    public async Task Repeated_next_link_is_rejected_without_repeating_the_request()
    {
        const string next = "/v1.0/users?$skiptoken=repeat";
        var transport = new RecordingTransport(
            Success($$"""{"value":[{"id":"user-1"}],"@odata.nextLink":"{{next}}"}"""),
            Success($$"""{"value":[{"id":"user-2"}],"@odata.nextLink":"{{next}}"}"""));

        var result = await new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Users.Keys.Should().Equal("user-1", "user-2");
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_initial_continuation_link_is_rejected_with_partial_rows(string nextLink)
    {
        var transport = new RecordingTransport(
            Success($$"""{"value":[{"id":"user-1"}],"@odata.nextLink":"{{nextLink}}"}"""));

        var result = await new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Users.Keys.Should().Equal("user-1");
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_later_page_continuation_link_is_rejected_with_prior_rows(string nextLink)
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1"}],"@odata.nextLink":"/v1.0/users?$skiptoken=page2"}"""),
            Success($$"""{"value":[{"id":"user-2"}],"@odata.nextLink":"{{nextLink}}"}"""));

        var result = await new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Users.Keys.Should().Equal("user-1", "user-2");
        transport.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Cancellation_is_passed_to_the_transport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new RecordingTransport(Success("""{"value":[]}"""), honorCancellation: true);

        var act = () => new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Requests_never_use_manager_or_per_account_authentication_method_paths()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1"}],"@odata.nextLink":"/v1.0/users?$skiptoken=page2"}"""),
            Success("""{"value":[]}"""));

        await new GraphAuthenticationCampaignsDirectoryReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        transport.Requests.Select(request => request.PathAndQuery).Should().OnlyContain(path =>
            path.StartsWith("/v1.0/users?", StringComparison.Ordinal)
            && !path.Contains("/manager", StringComparison.OrdinalIgnoreCase)
            && !path.Contains("/authentication", StringComparison.OrdinalIgnoreCase));
    }

    private static GraphTransportResponse Success(string content) =>
        new(GraphOperationResult.Success(), content, 1, new Dictionary<string, IReadOnlyCollection<string>>());

    private static GraphTransportResponse Failure(string category) =>
        new(new GraphOperationResult(false, category), string.Empty, 1, new Dictionary<string, IReadOnlyCollection<string>>());

    private sealed class RecordingFactory(RecordingTransport transport) : IDelegatedGraphClientFactory
    {
        public IReadOnlyCollection<string> RequestedScopes { get; private set; } = [];

        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        {
            RequestedScopes = scopes;
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingTransport(params GraphTransportResponse[] responses) : IGraphTransport
    {
        private int index;

        public RecordingTransport(GraphTransportResponse first, GraphTransportResponse? second = null, bool honorCancellation = false)
            : this(second is null ? [first] : [first, second])
        {
            HonorsCancellation = honorCancellation;
        }

        private bool HonorsCancellation { get; }
        public IReadOnlyCollection<string> Scopes => [];
        public List<GraphRequest> Requests { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            if (HonorsCancellation)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return Task.FromResult(responses[Math.Min(index++, responses.Length - 1)]);
        }
    }
}
