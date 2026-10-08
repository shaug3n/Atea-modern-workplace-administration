using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Licenses;

public sealed class LicenseHygieneGraphReaderTests
{
    private const string InitialPath = "/v1.0/users?$select=id,displayName,userPrincipalName,accountEnabled,assignedLicenses&$top=100";
    private static readonly WorkspaceContext Context = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com", "Reader", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Workspace", "member", ModuleKeys: ["licenses"]));

    [Fact]
    public async Task Reads_lean_user_projection_and_preserves_unknown_skus()
    {
        var transport = new TestTransport(_ => Success(
            """{"value":[{"id":"user-1","displayName":"Reader","userPrincipalName":"reader@example.com","accountEnabled":false,"assignedLicenses":[{"skuId":"known-sku"},{"skuId":"unknown-sku-2026"}]},{"id":"user-2","accountEnabled":true,"assignedLicenses":[]}]}"""));

        var result = await Reader(transport).ScanAsync(Context, CancellationToken.None);

        result.Completed.Should().BeTrue();
        result.PagesRead.Should().Be(1);
        result.Users.Should().HaveCount(2);
        result.Users[0].AssignedSkuIds.Should().Equal("known-sku", "unknown-sku-2026");
        result.Users[1].AssignedSkuIds.Should().BeEmpty();
        transport.Paths.Should().ContainSingle().Which.Should().Be(InitialPath);
        transport.FactoryScopes.Should().Equal(["Directory.Read.All"]);

        var empty = await Reader(new TestTransport(_ => Success("""{"value":[]}""")))
            .ScanAsync(Context, CancellationToken.None);
        empty.Users.Should().BeEmpty();
        empty.Completed.Should().BeTrue();
    }

    [Fact]
    public async Task Distinguishes_missing_from_empty_assignment_evidence()
    {
        var transport = new TestTransport(_ => Success(
            """{"value":[{"id":"missing"},{"id":"null","accountEnabled":null,"assignedLicenses":null},{"id":"malformed","accountEnabled":"false","assignedLicenses":{}},{"id":"empty","accountEnabled":false,"assignedLicenses":[]}]}"""));

        var result = await Reader(transport).ScanAsync(Context, CancellationToken.None);

        result.Users.Should().HaveCount(4);
        result.Users[0].AccountEnabled.Should().BeNull();
        result.Users[0].AssignedSkuIds.Should().BeNull();
        result.Users[0].AssignmentEvidenceMalformed.Should().BeFalse();
        result.Users[1].AccountEnabled.Should().BeNull();
        result.Users[1].AssignedSkuIds.Should().BeNull();
        result.Users[1].AssignmentEvidenceMalformed.Should().BeFalse();
        result.Users[2].AccountEnabled.Should().BeNull();
        result.Users[2].AssignedSkuIds.Should().BeNull();
        result.Users[2].AssignmentEvidenceMalformed.Should().BeTrue();
        result.Users[3].AccountEnabled.Should().BeFalse();
        result.Users[3].AssignedSkuIds.Should().BeEmpty();
        result.Users[3].AssignmentEvidenceMalformed.Should().BeFalse();
    }

    [Fact]
    public async Task No_successful_user_page_is_unavailable()
    {
        var failure = new GraphOperationResult(false, "not_authorized", 403);
        var transport = new TestTransport(_ => new GraphTransportResponse(failure, "{}", 1, EmptyHeaders));

        var result = await Reader(transport).ScanAsync(Context, CancellationToken.None);

        result.Users.Should().BeEmpty();
        result.PagesRead.Should().Be(0);
        result.Completed.Should().BeFalse();
        result.Error.Should().Be(failure);
    }

    [Fact]
    public async Task Rejects_unsafe_and_repeated_continuations()
    {
        foreach (var link in new[]
        {
            "https://evil.example/v1.0/users?$skiptoken=x",
            "http://graph.microsoft.com/v1.0/users?$skiptoken=x",
            "https://graph.microsoft.com/v1.0/groups?$skiptoken=x",
            "https://graph.microsoft.com.evil.example/v1.0/users?$skiptoken=x",
            "https://user@graph.microsoft.com/v1.0/users?$skiptoken=x"
        })
        {
            var unsafeTransport = new TestTransport(_ => Success(PageWithNext(link)));
            var unsafeResult = await Reader(unsafeTransport).ScanAsync(Context, CancellationToken.None);
            unsafeResult.Error?.Category.Should().Be("invalid_response", because: link);
            unsafeResult.Users.Should().BeEmpty();
        }

        var repeatedTransport = new TestTransport(_ => Success(PageWithNext(
            "https://graph.microsoft.com/v1.0/users?$select=id,displayName,userPrincipalName,accountEnabled,assignedLicenses&$top=100")));
        var repeated = await Reader(repeatedTransport).ScanAsync(Context, CancellationToken.None);
        repeated.Error?.Category.Should().Be("invalid_response");
        repeated.Users.Should().ContainSingle(user => user.Id == "user-1");

        var acceptedTransport = new TestTransport(index => index switch
        {
            0 => Success(PageWithNext("https://graph.microsoft.com/v1.0/users?$skiptoken=accepted")),
            _ => Success("""{"value":[{"id":"user-2"}]}""")
        });
        var accepted = await Reader(acceptedTransport).ScanAsync(Context, CancellationToken.None);
        accepted.Error.Should().BeNull();
        accepted.Completed.Should().BeTrue();
        accepted.Users.Select(user => user.Id).Should().Equal("user-1", "user-2");
        acceptedTransport.Paths.Should().Equal(
            InitialPath,
            "/v1.0/users?$skiptoken=accepted");

        var malformedTransport = new TestTransport(_ => Success("""{"value":[{"id":"user-1"},{"displayName":"missing id"}]}"""));
        var malformed = await Reader(malformedTransport).ScanAsync(Context, CancellationToken.None);
        malformed.Error?.Category.Should().Be("invalid_response");
        malformed.Users.Should().BeEmpty();

        var duplicateTransport = new TestTransport(_ => Success("""{"value":[{"id":"duplicate"},{"id":"duplicate"}]}"""));
        var duplicate = await Reader(duplicateTransport).ScanAsync(Context, CancellationToken.None);
        duplicate.Error?.Category.Should().Be("invalid_response");
        duplicate.Users.Should().BeEmpty();

        var duplicateAcrossPages = new TestTransport(index => index == 0
            ? Success(PageWithNext("https://graph.microsoft.com/v1.0/users?$skiptoken=duplicate"))
            : Success("""{"value":[{"id":"USER-1"}]}"""));
        var duplicateLater = await Reader(duplicateAcrossPages).ScanAsync(Context, CancellationToken.None);
        duplicateLater.Error?.Category.Should().Be("invalid_response");
        duplicateLater.Users.Select(user => user.Id).Should().Equal("user-1");
    }

    [Fact]
    public async Task Stops_at_record_and_page_limits()
    {
        var recordLimit = new LicenseHygieneScanLimits(2, 10, 100, TimeSpan.FromSeconds(2));
        var recordTransport = new TestTransport(index => index switch
        {
            0 => Success("""{"value":[{"id":"user-1"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=two"}"""),
            _ => Success("""{"value":[{"id":"user-2"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=three"}""")
        });
        var records = await Reader(recordTransport, recordLimit).ScanAsync(Context, CancellationToken.None);
        records.Users.Select(user => user.Id).Should().Equal("user-1", "user-2");
        records.PagesRead.Should().Be(2);
        records.Completed.Should().BeFalse();
        records.StopReason.Should().Be("record_limit");
        recordTransport.Paths.Should().HaveCount(2);

        var pageLimit = new LicenseHygieneScanLimits(10, 1, 100, TimeSpan.FromSeconds(2));
        var pageTransport = new TestTransport(_ => Success(
            """{"value":[{"id":"user-1"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=two"}"""));
        var pages = await Reader(pageTransport, pageLimit).ScanAsync(Context, CancellationToken.None);
        pages.PagesRead.Should().Be(1);
        pages.Users.Should().ContainSingle();
        pages.Completed.Should().BeFalse();
        pages.StopReason.Should().Be("page_limit");

        var exactEdgeTransport = new TestTransport(_ => Success("""{"value":[{"id":"user-1"}]}"""));
        var exactEdge = await Reader(exactEdgeTransport, new LicenseHygieneScanLimits(1, 1, 100, TimeSpan.FromSeconds(2)))
            .ScanAsync(Context, CancellationToken.None);
        exactEdge.Completed.Should().BeTrue();
        exactEdge.StopReason.Should().Be("completed");
    }

    [Fact]
    public async Task Timeout_cancels_inflight_request_and_keeps_verified_pages()
    {
        var enteredSecondPage = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondPageCancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var retryAttempts = 0;
        var transport = new TestTransport(async (index, token) =>
        {
            if (index == 0)
            {
                return Success("""{"value":[{"id":"verified"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/users?$skiptoken=two"}""");
            }

            enteredSecondPage.TrySetResult();
            retryAttempts++;
            await WaitForCancellation(token, secondPageCancelled);
            retryAttempts++;
            return Success("""{"value":[{"id":"too-late"}]}""");
        });

        var scan = Reader(transport, new LicenseHygieneScanLimits(10, 10, 100, TimeSpan.FromMilliseconds(80)))
            .ScanAsync(Context, CancellationToken.None);
        await enteredSecondPage.Task.WaitAsync(TimeSpan.FromSeconds(1));
        var result = await scan.WaitAsync(TimeSpan.FromSeconds(1));

        await secondPageCancelled.Task.WaitAsync(TimeSpan.FromSeconds(1));
        result.Users.Select(user => user.Id).Should().Equal("verified");
        result.PagesRead.Should().Be(1);
        result.Completed.Should().BeFalse();
        result.StopReason.Should().Be("time_limit");
        result.Error?.Category.Should().Be("timeout");
        retryAttempts.Should().Be(1);
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        using var cancellation = new CancellationTokenSource();
        var enteredRequest = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = new TestTransport(async (_, token) =>
        {
            enteredRequest.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("The infinite wait completed without cancellation.");
        });
        var scan = Reader(transport).ScanAsync(Context, cancellation.Token);
        await enteredRequest.Task.WaitAsync(TimeSpan.FromSeconds(1));
        cancellation.Cancel();

        var act = async () => await scan;
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static GraphLicenseHygieneUserReader Reader(
        TestTransport transport,
        LicenseHygieneScanLimits? limits = null) =>
        new(new TestFactory(transport), limits);

    private static GraphTransportResponse Success(string content) =>
        new(GraphOperationResult.Success(), content, 1, EmptyHeaders);

    private static string PageWithNext(string next) =>
        $$"""{"value":[{"id":"user-1"}],"@odata.nextLink":{{System.Text.Json.JsonSerializer.Serialize(next)}}}""";

    private static async Task<GraphTransportResponse> WaitForCancellation(
        CancellationToken cancellationToken,
        TaskCompletionSource cancelled)
    {
        using var registration = cancellationToken.Register(() => cancelled.TrySetResult());
        await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        throw new InvalidOperationException("The infinite wait completed without cancellation.");
    }

    private static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> EmptyHeaders =
        new Dictionary<string, IReadOnlyCollection<string>>();

    private sealed class TestFactory(TestTransport transport) : IDelegatedGraphClientFactory
    {
        public Task<GraphClientLease> CreateForCurrentUserAsync(
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken)
        {
            transport.FactoryScopes = scopes;
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class TestTransport : IGraphTransport
    {
        private readonly Func<int, CancellationToken, Task<GraphTransportResponse>> respond;
        private int page;

        public TestTransport(Func<int, GraphTransportResponse> respond)
        {
            this.respond = (index, _) => Task.FromResult(respond(index));
        }

        public TestTransport(Func<int, CancellationToken, Task<GraphTransportResponse>> respond)
        {
            this.respond = respond;
        }

        public IReadOnlyCollection<string> Scopes => [];
        public List<string> Paths { get; } = [];
        public IReadOnlyCollection<string> FactoryScopes { get; set; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Paths.Add(request.PathAndQuery);
            return respond(page++, cancellationToken);
        }
    }
}
