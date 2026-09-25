using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Licenses;

public sealed class LicenseAssigneeTests
{
    [Fact]
    public async Task License_only_reader_gets_paged_assignees_without_users_module()
    {
        var context = new WorkspaceContext(
            new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com", "Reader", "Member"),
            new WorkspaceMembership(Guid.NewGuid(), "Workspace", "member", ModuleKeys: ["licenses"]));
        var snapshot = GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All", "User.Read.All"],
            [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);
        var reader = new Reader();
        var service = new LicenseAssigneeService(reader, new SnapshotReader(snapshot), new UserContinuationTokenProtector("test-key"));

        var first = await service.SearchAsync(context, "11111111-1111-1111-1111-111111111111", 1, null, CancellationToken.None);
        var second = await service.SearchAsync(context, "11111111-1111-1111-1111-111111111111", 1, first.ContinuationToken, CancellationToken.None);

        first.Items.Should().ContainSingle().Which.Id.Should().Be("user-1");
        first.ContinuationToken.Should().NotBeNullOrWhiteSpace();
        second.Items.Should().ContainSingle().Which.Id.Should().Be("user-2");
        reader.Queries[0].License.Should().Be("11111111-1111-1111-1111-111111111111");
        reader.Queries[1].ContinuationPath.Should().Be("/v1.0/users?$skiptoken=next");
    }

    private sealed class Reader : IUserDirectoryReader
    {
        public List<UserSearchQuery> Queries { get; } = [];
        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken)
        {
            Queries.Add(query);
            var second = Queries.Count == 2;
            return Task.FromResult(new PagedResult<UserSummary>([new(second ? "user-2" : "user-1", "Ada", "ada@example.com", null)], [], second ? null : "/v1.0/users?$skiptoken=next"));
        }
        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
    private sealed class SnapshotReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
