using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserDetailServiceTests
{
    [Fact]
    public async Task License_section_verifies_user_membership_and_returns_write_capability_state_with_read_data()
    {
        var directory = new RecordingDirectoryReader
        {
            User = new UserDetails(
                "user-1",
                "Ada Lovelace",
                "ada@example.com",
                "ada@example.com",
                true,
                "Member",
                "Ada",
                "Lovelace",
                "Principal Engineer",
                "Digital Workplace",
                "Oslo",
                "+47 22 00 00 00",
                "NO",
                false,
                "cloud",
                null)
        };
        var licenses = new RecordingLicenseReader
        {
            Result = GraphReadResult<IReadOnlyList<AssignedLicense>>.Succeeded([new AssignedLicense("sku-1", "ENTERPRISEPACK", "Microsoft 365 E3")])
        };
        var service = new UserDetailService(
            directory,
            licenses,
            new EmptyGroupReader(),
            new EmptyRoleReader(),
            new StaticCapabilityReader(GraphAuthorizationSnapshot.Available(
                "actor-1",
                ["Directory.Read.All", "User.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])),
            () => DateTimeOffset.Parse("2026-09-21T08:00:00Z"));

        var section = await service.GetLicensesAsync(Workspace, "user-1", CancellationToken.None);

        directory.VerifiedUserIds.Should().Equal("user-1");
        licenses.Calls.Should().Be(1);
        section.Should().NotBeNull();
        section.Items.Should().ContainSingle(license => license.SkuPartNumber == "ENTERPRISEPACK");
        section.Access.Authorization.Capability.Should().Be(Capability.LicensesAssign);
        section.Access.Authorization.State.Should().Be(CapabilityState.ReadOnly);
        section.Access.Freshness.Should().Be(UserDirectoryFreshness.Fresh);
        section.Access.PartialData.Should().BeFalse();
    }

    [Fact]
    public async Task Detail_returns_removed_status_without_reading_sections_when_user_is_missing()
    {
        var licenses = new RecordingLicenseReader();
        var service = new UserDetailService(
            new RecordingDirectoryReader { User = null },
            licenses,
            new EmptyGroupReader(),
            new EmptyRoleReader(),
            new StaticCapabilityReader(AllowedSnapshot),
            () => DateTimeOffset.Parse("2026-09-21T08:00:00Z"));

        var detail = await service.GetDetailAsync(Workspace, "removed-user", CancellationToken.None);

        detail.Status.Should().Be(UserDetailStatus.NotFound);
        licenses.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Detail_maps_directory_consent_failure_to_safe_access_error()
    {
        var service = new UserDetailService(
            new RecordingDirectoryReader { Error = new GraphOperationResult(false, "consent_required", 403) },
            new RecordingLicenseReader(),
            new EmptyGroupReader(),
            new EmptyRoleReader(),
            new StaticCapabilityReader(AllowedSnapshot),
            () => DateTimeOffset.Parse("2026-09-21T08:00:00Z"));

        var detail = await service.GetDetailAsync(Workspace, "user-1", CancellationToken.None);

        detail.Status.Should().Be(UserDetailStatus.Found);
        detail.Detail!.User.Should().BeNull();
        detail.Detail.Access.Freshness.Should().Be(UserDirectoryFreshness.Unavailable);
        detail.Detail.Access.PartialData.Should().BeTrue();
        detail.Detail.Access.Error.Should().BeEquivalentTo(new UserDirectoryError(
            "consent_required",
            "Delegated Microsoft Graph consent is required to verify this user.",
            CapabilityState.Allowed,
            403,
            null));
        detail.Detail.Licenses.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Section_verification_throttle_returns_stale_error_without_reading_section()
    {
        var licenses = new RecordingLicenseReader();
        var service = new UserDetailService(
            new RecordingDirectoryReader { Error = new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(30)) },
            licenses,
            new EmptyGroupReader(),
            new EmptyRoleReader(),
            new StaticCapabilityReader(AllowedSnapshot),
            () => DateTimeOffset.Parse("2026-09-21T08:00:00Z"));

        var section = await service.GetLicensesAsync(Workspace, "user-1", CancellationToken.None);

        section.Should().NotBeNull();
        section!.Items.Should().BeEmpty();
        section.Access.Authorization.Capability.Should().Be(Capability.LicensesAssign);
        section.Access.Freshness.Should().Be(UserDirectoryFreshness.Stale);
        section.Access.PartialData.Should().BeTrue();
        section.Access.Error.Should().BeEquivalentTo(new UserDirectoryError(
            "throttled",
            "Microsoft Graph throttled user verification.",
            CapabilityState.Allowed,
            429,
            30));
        licenses.Calls.Should().Be(0);
    }

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

    private static readonly GraphAuthorizationSnapshot AllowedSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All", "Group.Read.All", "RoleManagement.Read.Directory", "LicenseAssignment.ReadWrite.All", "GroupMember.ReadWrite.All", "RoleManagement.ReadWrite.Directory"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public List<string> VerifiedUserIds { get; } = [];
        public UserDetails? User { get; set; }
        public GraphOperationResult? Error { get; set; }

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken)
        {
            VerifiedUserIds.Add(userObjectId);
            if (Error is not null)
            {
                throw new GraphAdapterException(Error);
            }

            return Task.FromResult(User);
        }
    }

    private sealed class RecordingLicenseReader : IUserLicenseReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<AssignedLicense>> Result { get; set; } = GraphReadResult<IReadOnlyList<AssignedLicense>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<AssignedLicense>>> ReadUserLicensesAsync(string userObjectId, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class EmptyGroupReader : IGroupMembershipReader
    {
        public Task<GraphReadResult<IReadOnlyList<GroupMembership>>> ReadUserGroupsAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<GroupMembership>>.Succeeded([]));
    }

    private sealed class EmptyRoleReader : IRoleAndPimReader
    {
        public Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded([]));

        public Task<GraphReadResult<IReadOnlyList<PimEligibility>>> ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded([]));
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
