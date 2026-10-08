using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.AuthenticationCampaigns;

public sealed class GraphAuthenticationCampaignsReportReaderTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace", "member"));

    [Fact]
    public async Task Reads_registration_report_pages_with_the_report_scope()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1","methodsRegistered":["passKeyDeviceBound"]}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=page2"}""", "report-page-1"),
            Success("""{"value":[{"id":"user-2","methodsRegistered":["mobilePhone"]}]}""", "report-page-2"));
        var factory = new RecordingFactory(transport);

        var result = await new GraphAuthenticationCampaignsReportReader(factory).ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Records.Select(record => record.Id).Should().Equal("user-1", "user-2");
        factory.RequestedScopes.Should().Equal("AuditLog.Read.All");
        transport.Requests.Select(request => request.PathAndQuery).Should().Equal(
            "/v1.0/reports/authenticationMethods/userRegistrationDetails",
            "/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=page2");
        transport.Requests.Should().OnlyContain(request => request.Method == HttpMethod.Get);
        result.CorrelationIds.Should().Equal("report-page-1", "report-page-2");
    }

    [Fact]
    public void Report_scope_is_included_in_capability_evaluation()
    {
        GraphScopeCatalog.CapabilityEvaluationScopes.Should().Contain("AuditLog.Read.All");
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"value":{}}""")]
    public async Task Missing_or_malformed_report_collection_is_an_invalid_response(string body)
    {
        var transport = new RecordingTransport(Success(body));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_response");
        result.Records.Should().BeEmpty();
    }

    [Theory]
    [InlineData("https://evil.example/v1.0/users")]
    [InlineData("https://graph.microsoft.com.evil.example/v1.0/users")]
    [InlineData("http://graph.microsoft.com/v1.0/users")]
    public async Task Rejects_untrusted_continuation_links_before_authenticated_transport(string nextLink)
    {
        var transport = new RecordingTransport(Success($$"""{"value":[{"id":"user-1"}],"@odata.nextLink":"{{nextLink}}"}"""));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_response");
        result.Records.Should().ContainSingle().Which.Id.Should().Be("user-1");
        result.PartialData.Should().BeTrue();
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Repeated_continuation_link_returns_invalid_response_without_looping()
    {
        const string next = "/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=repeat";
        var transport = new RecordingTransport(Success($$"""{"value":[{"id":"user-1"}],"@odata.nextLink":"{{next}}"}"""), Success($$"""{"value":[{"id":"user-2"}],"@odata.nextLink":"{{next}}"}"""));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Records.Select(record => record.Id).Should().Equal("user-1", "user-2");
        transport.Requests.Should().HaveCount(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_initial_continuation_link_is_rejected_with_partial_rows(string nextLink)
    {
        var transport = new RecordingTransport(
            Success($$"""{"value":[{"id":"user-1"}],"@odata.nextLink":"{{nextLink}}"}"""));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Records.Select(record => record.Id).Should().Equal("user-1");
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Blank_later_page_continuation_link_is_rejected_with_prior_rows(string nextLink)
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1"}],"@odata.nextLink":"/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=page2"}"""),
            Success($$"""{"value":[{"id":"user-2"}],"@odata.nextLink":"{{nextLink}}"}"""));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("invalid_response");
        result.PartialData.Should().BeTrue();
        result.Records.Select(record => record.Id).Should().Equal("user-1", "user-2");
        transport.Requests.Should().HaveCount(2);
    }

    [Fact]
    public async Task Initial_page_failure_returns_an_error_without_fabricating_rows()
    {
        var transport = new RecordingTransport(Failure("not_authorized"));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("not_authorized");
        result.Records.Should().BeEmpty();
        result.PartialData.Should().BeFalse();
    }

    [Fact]
    public async Task Later_page_failure_preserves_prior_rows_as_partial_data()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[{"id":"user-1"}],"@odata.nextLink":"/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=page2"}"""),
            Failure("temporarily_unavailable"));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("temporarily_unavailable");
        result.PartialData.Should().BeTrue();
        result.Records.Select(record => record.Id).Should().Equal("user-1");
    }

    [Fact]
    public async Task Later_page_failure_after_an_empty_page_is_still_partial()
    {
        var transport = new RecordingTransport(
            Success("""{"value":[],"@odata.nextLink":"/v1.0/reports/authenticationMethods/userRegistrationDetails?$skiptoken=page2"}"""),
            Failure("temporarily_unavailable"));

        var result = await new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, CancellationToken.None);

        result.Error!.Category.Should().Be("temporarily_unavailable");
        result.PartialData.Should().BeTrue();
        result.Records.Should().BeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_passed_to_the_transport()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var transport = new RecordingTransport(Success("""{"value":[]}"""), honorCancellation: true);

        var act = () => new GraphAuthenticationCampaignsReportReader(new RecordingFactory(transport))
            .ReadAsync(Workspace, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static GraphTransportResponse Success(string content, string? correlationId = null) =>
        new(GraphOperationResult.Success(correlationId), content, 1, new Dictionary<string, IReadOnlyCollection<string>>());

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
