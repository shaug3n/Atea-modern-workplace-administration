using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Persistence;

public sealed class WorkspaceRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().Build();
    private WorkplaceDbContext db = null!;

    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        db = new WorkplaceDbContext(new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql(postgres.GetConnectionString())
            .Options);
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await db.DisposeAsync();
        await postgres.DisposeAsync();
    }

    [Fact]
    public async Task Repository_scope_cannot_read_update_or_delete_another_workspace()
    {
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        await db.Workspaces.AddRangeAsync(
            NewWorkspace(workspaceA, Guid.NewGuid(), "A"),
            NewWorkspace(workspaceB, Guid.NewGuid(), "B"));
        await db.SaveChangesAsync();
        var repository = new WorkspaceRepository(db, workspaceA);

        (await repository.GetAsync(workspaceB)).Should().BeNull();
        await FluentActions.Invoking(() => repository.UpdateConnectionAsync(workspaceB, "connected", "User.Read", DateTimeOffset.UtcNow))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        await FluentActions.Invoking(() => repository.DeleteAsync(workspaceB))
            .Should().ThrowAsync<UnauthorizedAccessException>();
        (await db.Workspaces.FindAsync(workspaceB)).Should().NotBeNull();
        (await db.Workspaces.SingleAsync(w => w.Id == workspaceB)).ConnectionStatus.Should().Be("awaiting_invitation");
    }

    [Fact]
    public async Task Membership_lookup_is_scoped_to_workspace()
    {
        var workspaceA = Guid.NewGuid();
        var workspaceB = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        await db.Workspaces.AddRangeAsync(
            NewWorkspace(workspaceA, Guid.NewGuid(), "A"),
            NewWorkspace(workspaceB, Guid.NewGuid(), "B"));
        await db.WorkspaceMemberships.AddAsync(new WorkspaceMembership
        {
            Id = Guid.NewGuid(), WorkspaceId = workspaceB, TenantObjectId = objectId,
            Email = "b@example.com", PlatformRole = "member", CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var repository = new WorkspaceRepository(db, workspaceA);

        (await repository.GetMembershipAsync(workspaceB, objectId)).Should().BeNull();
    }

    private static Workspace NewWorkspace(Guid id, Guid tenantId, string name) => new()
    {
        Id = id, TenantId = tenantId, DisplayName = name, ConnectionStatus = "awaiting_invitation",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
}
