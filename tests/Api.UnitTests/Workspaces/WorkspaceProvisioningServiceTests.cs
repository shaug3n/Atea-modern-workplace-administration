using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PlatformWorkspaceScope = Atea.UnifiedWorkplace.Api.Authorization.PlatformWorkspaceScope;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class WorkspaceProvisioningServiceTests
{
    [Fact]
    public async Task List_returns_only_workspaces_in_the_authorized_scope_with_memberships()
    {
        var inScope = new Workspace { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), DisplayName = "In scope" };
        inScope.Memberships.Add(new WorkspaceMembership { Id = Guid.NewGuid(), WorkspaceId = inScope.Id, Email = "member@example.com", PlatformRole = "member" });
        var repository = new ReadRecordingProvisioningRepository([inScope]);
        var service = CreateService(repository);
        var scope = new PlatformWorkspaceScope(false, new HashSet<Guid> { inScope.Id });

        var result = await service.ListAsync(scope);

        result.Should().ContainSingle().Which.Memberships.Should().ContainSingle();
        repository.LastScope.Should().BeSameAs(scope);
    }

    [Fact]
    public async Task Get_admin_detail_returns_safe_invitation_metadata_without_nonce_hash_or_url()
    {
        var workspaceId = Guid.NewGuid();
        var detail = new WorkspaceAdminDetailDto(
            workspaceId,
            Guid.NewGuid(),
            "Workspace",
            "connected",
            DateTimeOffset.UtcNow,
            null,
            [new WorkspaceMembershipDto(Guid.NewGuid(), Guid.NewGuid(), "member@example.com", "member", false)],
            [new InvitationSummaryDto(Guid.NewGuid(), "invitee@example.com", "Invitee", DateTimeOffset.UtcNow.AddDays(1), null)]);
        var service = CreateService(new ReadRecordingProvisioningRepository([], detail));

        var result = await service.GetAdminDetailAsync(workspaceId, new PlatformWorkspaceScope(false, new HashSet<Guid> { workspaceId }));

        result.Should().BeEquivalentTo(detail);
        typeof(InvitationSummaryDto).GetProperties().Select(property => property.Name)
            .Should().NotContain(new[] { "NonceHash", "InvitationUrl" });
    }

    [Fact]
    public async Task Unique_create_failure_is_returned_as_a_conflict_result()
    {
        var service = CreateService(new DuplicateProvisioningRepository());

        var result = await service.CreateWorkspaceAsync(Guid.NewGuid(), "Duplicate");

        result.IsConflict.Should().BeTrue();
        result.Workspace.Should().BeNull();
    }

    [Fact]
    public async Task Non_unique_create_failure_is_not_returned_as_a_conflict()
    {
        var service = CreateService(new FailingProvisioningRepository());

        var action = () => service.CreateWorkspaceAsync(Guid.NewGuid(), "Unavailable");

        await action.Should().ThrowAsync<WorkspaceProvisioningUnavailableException>();
    }

    [Fact]
    public async Task Service_delegates_workspace_creation_to_the_provisioning_repository_boundary()
    {
        var repository = new RecordingProvisioningRepository();
        var tenantId = Guid.NewGuid();
        var service = CreateService(repository);

        await service.CreateWorkspaceAsync(tenantId, "Workspace");

        repository.FindByTenantIdCalls.Should().Be(1);
        repository.CreateCalls.Should().Be(1);
        repository.LastTenantId.Should().Be(tenantId);
    }

    [Fact]
    public async Task Unique_membership_failure_is_translated_to_the_existing_conflict_exception()
    {
        var service = CreateService(new UniqueMembershipRepository());

        var action = () => service.AddMembershipAsync(Guid.NewGuid(), Guid.NewGuid(), "member@example.com", "member", false);

        await action.Should().ThrowAsync<WorkspaceAlreadyExistsException>();
    }

    [Fact]
    public async Task Non_unique_membership_failure_is_translated_to_unavailable()
    {
        var service = CreateService(new FailingMembershipRepository());

        var action = () => service.AddMembershipAsync(Guid.NewGuid(), Guid.NewGuid(), "member@example.com", "member", false);

        await action.Should().ThrowAsync<WorkspaceProvisioningUnavailableException>();
    }

    private class DuplicateProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>([]);
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceAdminDetailDto?>(null);
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new WorkspaceUniqueConstraintException("duplicate tenant");
        public Task<Workspace> CreateWithInvitationAsync(Workspace workspace, PlatformInvitation invitation, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public virtual Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) => AddMembershipAsync(workspaceId, tenantObjectId, email, platformRole, isAteaOperator, cancellationToken);
    }

    private sealed class FailingProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>([]);
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceAdminDetailDto?>(null);
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new WorkspaceProvisioningDatabaseException("database unavailable");
        public Task<Workspace> CreateWithInvitationAsync(Workspace workspace, PlatformInvitation invitation, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new WorkspaceProvisioningDatabaseException("database unavailable");
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class RecordingProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Workspace>>([]);
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceAdminDetailDto?>(null);
        public int FindByTenantIdCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public Guid LastTenantId { get; private set; }
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) { FindByTenantIdCalls++; LastTenantId = tenantId; return Task.FromResult<Workspace?>(null); }
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) { CreateCalls++; LastTenantId = tenantId; return Task.FromResult(new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName }); }
        public Task<Workspace> CreateWithInvitationAsync(Workspace workspace, PlatformInvitation invitation, AuditEvent auditEvent, CancellationToken cancellationToken = default) => Task.FromResult(workspace);
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class UniqueMembershipRepository : DuplicateProvisioningRepository
    {
        public override Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new WorkspaceUniqueConstraintException("duplicate membership");
    }

    private sealed class FailingMembershipRepository : DuplicateProvisioningRepository
    {
        public override Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new WorkspaceProvisioningDatabaseException("database unavailable");
    }

    private sealed class ReadRecordingProvisioningRepository(IReadOnlyList<Workspace> workspaces, WorkspaceAdminDetailDto? detail = null) : IWorkspaceProvisioningRepository
    {
        public PlatformWorkspaceScope? LastScope { get; private set; }
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<Workspace> CreateWithInvitationAsync(Workspace workspace, PlatformInvitation invitation, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, AuditEvent auditEvent, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) { LastScope = workspaceScope; return Task.FromResult(workspaces); }
        public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) { LastScope = workspaceScope; return Task.FromResult(detail); }
    }

    private static WorkspaceProvisioningService CreateService(IWorkspaceProvisioningRepository repository) => new(repository, new InvitationService(new NoopInvitationRepository(), new Uri("http://localhost:5173/")));

    private sealed class NoopInvitationRepository : IInvitationRepository
    {
        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default) => Task.FromResult(invitation);
        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default) => Task.FromResult<InvitationRedemption?>(null);
    }
}
