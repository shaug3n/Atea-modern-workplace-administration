using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Persistence;

public sealed class WorkspaceIsolationTests
{
    [Fact]
    public async Task Scoped_repository_rejects_every_mutation_outside_its_workspace()
    {
        var db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>().Options);
        var repository = new WorkspaceRepository(db, Guid.NewGuid());
        var foreignWorkspace = Guid.NewGuid();

        (await repository.GetAsync(foreignWorkspace)).Should().BeNull();
        await FluentActions.Invoking(() => repository.UpdateConnectionAsync(foreignWorkspace, "connected", "[]", null)).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => repository.AddMembershipAsync(foreignWorkspace, Guid.NewGuid(), "user@example.com", "member", false)).Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => repository.DeleteAsync(foreignWorkspace)).Should().ThrowAsync<UnauthorizedAccessException>();
    }
}
