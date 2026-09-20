using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserQueryServiceTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "alex@example.com",
            "Alex Example",
            "Member"),
        new WorkspaceMembership(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Contoso Workplace",
            "member"));

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task Search_rejects_page_sizes_outside_supported_range(int pageSize)
    {
        var service = new UserQueryService(
            new RecordingDirectoryReader(),
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available(
                "user-1",
                ["Directory.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])),
            new UserContinuationTokenProtector("0123456789abcdef0123456789abcdef"));

        var act = () => service.SearchAsync(Workspace, new UserSearchRequest(PageSize: pageSize), CancellationToken.None);

        await act.Should().ThrowAsync<UserSearchValidationException>()
            .WithMessage("*pageSize*");
    }

    [Fact]
    public async Task Search_passes_filters_and_returns_opaque_continuation_without_raw_graph_link()
    {
        var reader = new RecordingDirectoryReader
        {
            Result = new PagedResult<UserSummary>(
                [new UserSummary("user-1", "Ada Lovelace", "ada@example.com", "ada@example.com", true, "Member")],
                ["corr-1"],
                "/v1.0/users?$skiptoken=raw-page-two")
        };
        var service = new UserQueryService(
            reader,
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available(
                "user-1",
                ["Directory.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])),
            new UserContinuationTokenProtector("0123456789abcdef0123456789abcdef"),
            () => DateTimeOffset.Parse("2026-09-20T12:00:00Z"));

        var response = await service.SearchAsync(Workspace, new UserSearchRequest(
            Search: "ada",
            PageSize: 25,
            AccountStatus: "enabled",
            TenantRole: "Global Reader",
            License: "ENTERPRISEPACK",
            UserType: "Member"), CancellationToken.None);

        reader.Query.Should().NotBeNull();
        reader.Query!.Search.Should().Be("ada");
        reader.Query.PageSize.Should().Be(25);
        reader.Query.AccountStatus.Should().Be("enabled");
        reader.Query.TenantRole.Should().Be("Global Reader");
        reader.Query.License.Should().Be("ENTERPRISEPACK");
        reader.Query.UserType.Should().Be("Member");
        response.Items.Should().ContainSingle(user => user.Id == "user-1");
        response.FetchedAt.Should().Be(DateTimeOffset.Parse("2026-09-20T12:00:00Z"));
        response.Freshness.Should().Be(UserDirectoryFreshness.Fresh);
        response.PartialData.Should().BeFalse();
        response.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        response.ContinuationToken.Should().NotContain("$skiptoken");
        response.ContinuationToken.Should().NotContain("/v1.0/users");
    }

    [Fact]
    public async Task Search_decodes_opaque_continuation_server_side()
    {
        var protector = new UserContinuationTokenProtector("0123456789abcdef0123456789abcdef");
        var token = protector.Protect("/v1.0/users?$skiptoken=raw-page-two");
        var reader = new RecordingDirectoryReader();
        var service = new UserQueryService(
            reader,
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available(
                "user-1",
                ["Directory.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])),
            protector);

        await service.SearchAsync(Workspace, new UserSearchRequest(ContinuationToken: token), CancellationToken.None);

        reader.Query.Should().NotBeNull();
        reader.Query!.ContinuationPath.Should().Be("/v1.0/users?$skiptoken=raw-page-two");
    }

    [Theory]
    [InlineData("not_found", UserDirectoryFreshness.Unavailable)]
    [InlineData("throttled", UserDirectoryFreshness.Stale)]
    public async Task Search_translates_graph_failures_without_raw_graph_payload(string category, string expectedFreshness)
    {
        var reader = new RecordingDirectoryReader
        {
            Result = new PagedResult<UserSummary>(
                [],
                ["corr-1"],
                null,
                new GraphOperationResult(false, category, category == "throttled" ? 429 : 404, TimeSpan.FromSeconds(30), "corr-1", "req-1"))
        };
        var service = new UserQueryService(
            reader,
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available(
                "user-1",
                ["Directory.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])),
            new UserContinuationTokenProtector("0123456789abcdef0123456789abcdef"));

        var response = await service.SearchAsync(Workspace, new UserSearchRequest(), CancellationToken.None);

        response.Freshness.Should().Be(expectedFreshness);
        response.PartialData.Should().BeTrue();
        response.Error.Should().NotBeNull();
        response.Error!.Category.Should().Be(category);
        response.Error.Message.Should().NotContain("raw graph");
    }

    [Fact]
    public async Task Search_does_not_call_graph_when_users_view_is_hidden()
    {
        var reader = new RecordingDirectoryReader();
        var service = new UserQueryService(
            reader,
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available("user-1", [], [])),
            new UserContinuationTokenProtector("0123456789abcdef0123456789abcdef"));

        var response = await service.SearchAsync(Workspace, new UserSearchRequest(), CancellationToken.None);

        reader.Calls.Should().Be(0);
        response.Items.Should().BeEmpty();
        response.Freshness.Should().Be(UserDirectoryFreshness.Unavailable);
        response.PartialData.Should().BeTrue();
        response.Error!.Category.Should().Be("capability_required");
        response.Error.State.Should().Be(CapabilityState.Hidden);
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public int Calls { get; private set; }
        public UserSearchQuery? Query { get; private set; }
        public PagedResult<UserSummary> Result { get; init; } = new([], [], null);

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            Query = query;
            return Task.FromResult(Result);
        }

        public Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken) => Task.FromResult<UserDetails?>(null);
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
