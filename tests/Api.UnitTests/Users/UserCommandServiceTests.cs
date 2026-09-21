using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Groups;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserCommandServiceTests
{
    [Fact]
    public async Task Create_user_sends_only_approved_fields_and_returns_temporary_password_after_graph_success()
    {
        var graph = new RecordingUserCommands();
        var service = CreateService(userCommands: graph);

        var result = await service.CreateAsync(Workspace, new CreateUserCommand(
            "Ada Lovelace",
            "Ada",
            "Lovelace",
            "ada@example.com",
            "ada",
            "Principal Engineer",
            "Digital Workplace",
            "Oslo",
            "+47 22 00 00 00",
            "NO",
            true), "key-create", CancellationToken.None);

        result.Status.Should().Be(UserCommandStatus.Succeeded);
        result.RequiredCapability.Should().Be(Capability.UsersCreate);
        result.TemporaryCredentialNotice.Should().NotBeNull();
        result.TemporaryCredentialNotice!.TemporaryPassword.Should().NotBeNullOrWhiteSpace();
        graph.Created.Should().ContainSingle();
        graph.Created[0].PasswordProfile.ForceChangePasswordNextSignIn.Should().BeTrue();
        graph.Created[0].PasswordProfile.TemporaryPassword.Should().Be(result.TemporaryCredentialNotice.TemporaryPassword);
        graph.Created[0].GivenName.Should().Be("Ada");
        graph.Created[0].Surname.Should().Be("Lovelace");
        graph.Created[0].UsageLocation.Should().Be("NO");
    }

    [Fact]
    public async Task Update_user_rejects_source_of_authority_read_only_without_calling_graph()
    {
        var graph = new RecordingUserCommands();
        var directory = new RecordingDirectoryReader { User = CloudUser with { IsReadOnly = true, SourceOfAuthority = "on_premises_sync" } };
        var service = CreateService(directory, graph);

        var result = await service.UpdateAsync(Workspace, "user-1", new UpdateUserCommand(
            DisplayName: "Ada Updated",
            GivenName: "Ada",
            Surname: "Lovelace",
            JobTitle: "Director",
            Department: "Digital Workplace",
            OfficeLocation: "Oslo",
            MobilePhone: "+47 22 00 00 01",
            UsageLocation: "NO",
            AccountEnabled: true), "key-edit", CancellationToken.None);

        result.Status.Should().Be(UserCommandStatus.SourceOfAuthorityReadOnly);
        result.Error.Should().Be("source_of_authority_read_only");
        graph.Updated.Should().BeEmpty();
    }

    [Fact]
    public async Task Disable_and_reactivate_use_account_enabled_command_contract()
    {
        var graph = new RecordingUserCommands();
        var service = CreateService(userCommands: graph);

        var disabled = await service.SetAccountEnabledAsync(Workspace, "user-1", new SetAccountEnabledCommand(false), "key-disable", CancellationToken.None);
        var reactivated = await service.SetAccountEnabledAsync(Workspace, "user-1", new SetAccountEnabledCommand(true), "key-reactivate", CancellationToken.None);

        disabled.Status.Should().Be(UserCommandStatus.Succeeded);
        reactivated.Status.Should().Be(UserCommandStatus.Succeeded);
        graph.AccountStates.Should().Equal(("user-1", false), ("user-1", true));
    }

    [Fact]
    public async Task Group_and_license_commands_call_their_focused_graph_adapters()
    {
        var groups = new RecordingGroupCommands();
        var licenses = new RecordingLicenseCommands();
        var service = CreateService(groupCommands: groups, licenseCommands: licenses);

        await service.AddGroupAsync(Workspace, "user-1", new GroupMembershipCommand("group-1"), "key-group-add", CancellationToken.None);
        await service.RemoveGroupAsync(Workspace, "user-1", new GroupMembershipCommand("group-1"), "key-group-remove", CancellationToken.None);
        await service.AssignLicenseAsync(Workspace, "user-1", new LicenseAssignmentCommand("sku-1", ["plan-1"]), "key-license-add", CancellationToken.None);
        await service.RemoveLicenseAsync(Workspace, "user-1", new LicenseAssignmentCommand("sku-1", []), "key-license-remove", CancellationToken.None);

        groups.Added.Should().Equal(("group-1", "user-1"));
        groups.Removed.Should().Equal(("group-1", "user-1"));
        licenses.Assignments.Should().Contain(("user-1", "sku-1", true));
        licenses.Assignments.Should().Contain(("user-1", "sku-1", false));
    }

    [Fact]
    public async Task Missing_capability_denies_without_calling_graph()
    {
        var graph = new RecordingUserCommands();
        var service = CreateService(
            userCommands: graph,
            snapshot: GraphAuthorizationSnapshot.Available(
                "actor-1",
                ["Directory.Read.All", "User.Read.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]));

        var result = await service.CreateAsync(Workspace, ValidCreate, "key-missing", CancellationToken.None);

        result.Status.Should().Be(UserCommandStatus.Denied);
        result.Authorization!.State.Should().Be(CapabilityState.ReadOnly);
        graph.Created.Should().BeEmpty();
    }

    [Fact]
    public async Task Duplicate_idempotency_key_returns_stored_result_and_changed_payload_returns_conflict()
    {
        var graph = new RecordingUserCommands();
        var idempotency = new MemoryIdempotencyService();
        var service = CreateService(userCommands: graph, idempotency: idempotency);

        var first = await service.UpdateAsync(Workspace, "user-1", ValidUpdate with { JobTitle = "Engineer" }, "same-key", CancellationToken.None);
        var replay = await service.UpdateAsync(Workspace, "user-1", ValidUpdate with { JobTitle = "Engineer" }, "same-key", CancellationToken.None);
        var reused = await service.UpdateAsync(Workspace, "user-1", ValidUpdate with { JobTitle = "Director" }, "same-key", CancellationToken.None);

        first.Status.Should().Be(UserCommandStatus.Succeeded);
        replay.Status.Should().Be(UserCommandStatus.Succeeded);
        replay.Replayed.Should().BeTrue();
        reused.Status.Should().Be(UserCommandStatus.IdempotencyKeyReused);
        graph.Updated.Should().ContainSingle();
    }

    [Fact]
    public async Task Graph_conflict_maps_to_command_conflict_without_temporary_credential()
    {
        var graph = new RecordingUserCommands { Result = new GraphOperationResult(false, "conflict", 409) };
        var service = CreateService(userCommands: graph);

        var result = await service.CreateAsync(Workspace, ValidCreate, "key-conflict", CancellationToken.None);

        result.Status.Should().Be(UserCommandStatus.Conflict);
        result.Error.Should().Be("conflict");
        result.TemporaryCredentialNotice.Should().BeNull();
    }

    private static UserCommandService CreateService(
        RecordingDirectoryReader? directory = null,
        RecordingUserCommands? userCommands = null,
        RecordingGroupCommands? groupCommands = null,
        RecordingLicenseCommands? licenseCommands = null,
        GraphAuthorizationSnapshot? snapshot = null,
        IIdempotencyService? idempotency = null) =>
        new(
            directory ?? new RecordingDirectoryReader { User = CloudUser },
            userCommands ?? new RecordingUserCommands(),
            groupCommands ?? new RecordingGroupCommands(),
            licenseCommands ?? new RecordingLicenseCommands(),
            new StaticCapabilityReader(snapshot ?? AdminSnapshot),
            idempotency ?? new MemoryIdempotencyService(),
            new RecordingAuditWriter(),
            () => "Temp-Password-12345!");

    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "alex@example.com",
            "Alex Example",
            "Member"),
        new AuthorizationWorkspaceMembership(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Contoso Workplace",
            "member"));

    private static readonly UserDetails CloudUser = new(
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
        "cloud");

    private static readonly CreateUserCommand ValidCreate = new(
        "Ada Lovelace",
        "Ada",
        "Lovelace",
        "ada@example.com",
        "ada",
        "Principal Engineer",
        "Digital Workplace",
        "Oslo",
        "+47 22 00 00 00",
        "NO",
        true);

    private static readonly UpdateUserCommand ValidUpdate = new(
        "Ada Lovelace",
        "Ada",
        "Lovelace",
        "Principal Engineer",
        "Digital Workplace",
        "Oslo",
        "+47 22 00 00 00",
        "NO",
        true);

    private static readonly GraphAuthorizationSnapshot AdminSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All", "User.Create", "User.ReadWrite.All", "User.EnableDisableAccount.All", "Group.Read.All", "GroupMember.ReadWrite.All", "LicenseAssignment.ReadWrite.All"],
        [
            new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.GroupsAdministratorTemplateId, "Groups Administrator", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.LicenseAdministratorTemplateId, "License Administrator", DirectoryRoleAssignmentState.Active, "/")
        ]);

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public UserDetails? User { get; init; }

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken) => Task.FromResult(User);
    }

    private sealed class RecordingUserCommands : IUserLifecycleCommands
    {
        public GraphOperationResult Result { get; init; } = GraphOperationResult.Success("corr-1", "req-1");
        public List<GraphUserCreateRequest> Created { get; } = [];
        public List<(string UserId, GraphUserProfileUpdate Update)> Updated { get; } = [];
        public List<(string UserId, bool Enabled)> AccountStates { get; } = [];

        public Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            Created.Add(request);
            return Task.FromResult(Result);
        }

        public Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken)
        {
            Updated.Add((userObjectId, update));
            return Task.FromResult(Result);
        }

        public Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken)
        {
            AccountStates.Add((userObjectId, accountEnabled));
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingGroupCommands : IGroupMembershipCommands
    {
        public List<(string GroupId, string UserId)> Added { get; } = [];
        public List<(string GroupId, string UserId)> Removed { get; } = [];

        public Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Added.Add((groupObjectId, memberObjectId));
            return Task.FromResult(GraphOperationResult.Success());
        }

        public Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Removed.Add((groupObjectId, memberObjectId));
            return Task.FromResult(GraphOperationResult.Success());
        }
    }

    private sealed class RecordingLicenseCommands : ILicenseAssignmentCommands
    {
        public List<(string UserId, string SkuId, bool Add)> Assignments { get; } = [];

        public Task<GraphOperationResult> AssignLicenseAsync(string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken)
        {
            Assignments.Add((userObjectId, command.SkuId, true));
            return Task.FromResult(GraphOperationResult.Success());
        }

        public Task<GraphOperationResult> RemoveLicenseAsync(string userObjectId, string skuId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Assignments.Add((userObjectId, skuId, false));
            return Task.FromResult(GraphOperationResult.Success());
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
