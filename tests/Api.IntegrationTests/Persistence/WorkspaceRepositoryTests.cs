using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit.Sdk;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Persistence;

public sealed class WorkspaceRepositoryTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder().Build();
    private WorkplaceDbContext db = null!;

    public async Task InitializeAsync()
    {
        try { await postgres.StartAsync(); }
        catch (DockerUnavailableException exception) { throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}"); }
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

    [Fact]
    public async Task Invitation_redemption_is_atomic_under_concurrent_postgresql_replay()
    {
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var workspace = NewWorkspace(workspaceId, tenantId, "Concurrent invitation");
        await db.Workspaces.AddAsync(workspace);
        await db.SaveChangesAsync();

        var creator = new InvitationService(new WorkspaceOnboardingRepository(db), new Uri("http://localhost:5173"));
        var created = await creator.CreateAsync(workspaceId, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(5));
        var options = new DbContextOptionsBuilder<WorkplaceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db2 = new WorkplaceDbContext(options);
        await using var db3 = new WorkplaceDbContext(options);
        var service2 = new InvitationService(new WorkspaceOnboardingRepository(db2), new Uri("http://localhost:5173"));
        var service3 = new InvitationService(new WorkspaceOnboardingRepository(db3), new Uri("http://localhost:5173"));
        var nonce = created.InvitationUrl.Split('/').Last();

        var results = await Task.WhenAll(
            service2.RedeemAsync(nonce, tenantId, Guid.NewGuid(), "admin@example.com", "Admin"),
            service3.RedeemAsync(nonce, tenantId, Guid.NewGuid(), "admin@example.com", "Admin"));

        results.Count(result => result).Should().Be(1);
        (await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == workspaceId)).Should().Be(1);
        db.ChangeTracker.Clear();
        (await db.PlatformInvitations.SingleAsync(x => x.Id == created.InvitationId)).RedeemedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Approved_invitation_rejects_same_tenant_different_object_id_even_when_email_matches()
    {
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var approvedObjectId = Guid.NewGuid();
        var wrongObjectId = Guid.NewGuid();
        await db.Workspaces.AddAsync(NewWorkspace(workspaceId, tenantId, "Approved invitation"));
        await db.SaveChangesAsync();

        var service = new InvitationService(new WorkspaceOnboardingRepository(db), new Uri("http://localhost:5173"));
        var created = await service.CreateAsync(workspaceId, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(5), approvedObjectId);
        var nonce = created.InvitationUrl.Split('/').Last();

        var redeemed = await service.RedeemAsync(nonce, tenantId, wrongObjectId, "admin@example.com", "Impostor");

        redeemed.Should().BeFalse();
        db.ChangeTracker.Clear();
        (await db.PlatformInvitations.SingleAsync(x => x.Id == created.InvitationId)).RedeemedAt.Should().BeNull();
        (await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == workspaceId)).Should().Be(0);
    }

    [Fact]
    public async Task Theme_preferences_are_isolated_by_tenant_and_user()
    {
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var repository = new UserPreferenceRepository(db);

        await repository.SetThemeAsync(tenantA, userA, "dark");
        await repository.SetThemeAsync(tenantB, userB, "light");

        (await repository.GetThemeAsync(tenantA, userA)).Should().Be("dark");
        (await repository.GetThemeAsync(tenantA, userB)).Should().BeNull();
        (await repository.GetThemeAsync(tenantB, userA)).Should().BeNull();
        (await repository.GetThemeAsync(tenantB, userB)).Should().Be("light");
    }

    private static Workspace NewWorkspace(Guid id, Guid tenantId, string name) => new()
    {
        Id = id, TenantId = tenantId, DisplayName = name, ConnectionStatus = "awaiting_invitation",
        CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow
    };
}
