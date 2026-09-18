using Atea.UnifiedWorkplace.Api.Features.Workspaces;
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

    private sealed class DuplicateProvisioningRepository : IWorkspaceProvisioningRepository
    {
        public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace?> FindByTenantIdAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<Workspace?>(null);
        public Task<Workspace> CreateAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default) => throw new DbUpdateException("duplicate key");
        public Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default) => throw new NotImplementedException();
    }
}
