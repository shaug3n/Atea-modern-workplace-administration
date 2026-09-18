using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class WorkspaceProvisioningServiceTests
{
    [Fact]
    public async Task Unique_create_failure_is_returned_as_a_conflict_result()
    {
        var service = new WorkspaceProvisioningService(new DuplicateProvisioningRepository());

        var result = await service.CreateWorkspaceAsync(Guid.NewGuid(), "Duplicate");

        result.IsConflict.Should().BeTrue();
        result.Workspace.Should().BeNull();
    }

    [Fact]
    public async Task Non_unique_create_failure_is_not_returned_as_a_conflict()
    {
        var service = new WorkspaceProvisioningService(new FailingProvisioningRepository());

        var action = () => service.CreateWorkspaceAsync(Guid.NewGuid(), "Unavailable");

        await action.Should().ThrowAsync<WorkspaceProvisioningUnavailableException>();
    }

    [Fact]
    public async Task Service_delegates_workspace_creation_to_the_provisioning_repository_boundary()
    {
        var repository = new RecordingProvisioningRepository();
        var tenantId = Guid.NewGuid();
        var service = new WorkspaceProvisioningService(repository);

        await service.CreateWorkspaceAsync(tenantId, "Workspace");

        repository.FindByTenantIdCalls.Should().Be(1);
        repository.CreateCalls.Should().Be(1);
        repository.LastTenantId.Should().Be(tenantId);
    }

    private class DuplicateProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new WorkspaceUniqueConstraintException("duplicate tenant");
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class FailingProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new WorkspaceProvisioningDatabaseException("database unavailable");
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }

    private sealed class RecordingProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public int FindByTenantIdCalls { get; private set; }
        public int CreateCalls { get; private set; }
        public Guid LastTenantId { get; private set; }
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) { FindByTenantIdCalls++; LastTenantId = tenantId; return Task.FromResult<Workspace?>(null); }
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) { CreateCalls++; LastTenantId = tenantId; return Task.FromResult(new Workspace { Id = Guid.NewGuid(), TenantId = tenantId, DisplayName = displayName }); }
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
