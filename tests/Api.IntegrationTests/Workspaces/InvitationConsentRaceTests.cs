using System.Security.Cryptography;
using System.Text;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit.Sdk;
using SignedConsentChallenge = Atea.UnifiedWorkplace.Api.Features.Workspaces.ConsentChallenge;
using ConsentChallengeEntity = Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.ConsentChallenge;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class InvitationConsentRaceTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync();
        }
        catch (DockerUnavailableException exception)
        {
            throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}");
        }

        await using var db = CreateDb();
        await db.Database.MigrateAsync();
    }

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Redeem_replay_requires_the_live_challenge_and_exact_recorded_redeemer()
    {
        var invitationId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var redeemerId = Guid.NewGuid();
        var anotherUserId = Guid.NewGuid();
        const string nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var nonceHash = Hash(nonce);
        var state = new ConsentChallengeService(new byte[32])
            .CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));
        var stateHash = ConsentChallengeService.HashState(state.Challenge);
        await SeedRedeemableInvitationAsync(invitationId, workspaceId, tenantId, nonceHash, stateHash, state);

        await using var db = CreateDb();
        var repository = new WorkspaceOnboardingRepository(db);
        var first = await repository.RedeemAsync(nonceHash, tenantId, redeemerId, "admin@example.com", "Admin", stateHash);
        var ordinaryReplay = await repository.RedeemAsync(nonceHash, tenantId, redeemerId, "admin@example.com", "Admin", null);
        var wrongIdentityReplay = await repository.RedeemAsync(nonceHash, tenantId, anotherUserId, "admin@example.com", "Admin", stateHash);
        var recovered = await repository.RedeemAsync(nonceHash, tenantId, redeemerId, "admin@example.com", "Admin", stateHash);

        first.Should().NotBeNull();
        ordinaryReplay.Should().BeNull();
        wrongIdentityReplay.Should().BeNull();
        recovered.Should().NotBeNull();
        (await db.AuditEvents.CountAsync(x => x.Action == "workspace.invitation.redeemed")).Should().Be(1);
        (await db.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == workspaceId)).Should().Be(1);
    }

    [Fact]
    public async Task Concurrent_authenticated_completion_consumes_only_once_and_revoked_invitation_fails_closed()
    {
        var invitationId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var redeemerId = Guid.NewGuid();
        var nonceHash = Hash("BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB");
        var state = new ConsentChallengeService(new byte[32])
            .CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));
        var stateHash = ConsentChallengeService.HashState(state.Challenge);
        await SeedRedeemedInvitationAsync(invitationId, workspaceId, tenantId, redeemerId, nonceHash, stateHash, state);

        await using var db1 = CreateDb();
        await using var db2 = CreateDb();
        var repository1 = new WorkspaceOnboardingRepository(db1);
        var repository2 = new WorkspaceOnboardingRepository(db2);
        var results = await Task.WhenAll(
            repository1.TryConsumeInvitationAsync(stateHash, invitationId, workspaceId, tenantId, redeemerId, DateTimeOffset.UtcNow),
            repository2.TryConsumeInvitationAsync(stateHash, invitationId, workspaceId, tenantId, redeemerId, DateTimeOffset.UtcNow));

        results.Count(result => result).Should().Be(1);

        await using var revoke = CreateDb();
        var invitation = await revoke.PlatformInvitations.SingleAsync(x => x.Id == invitationId);
        invitation.RevokedAt = DateTimeOffset.UtcNow;
        await revoke.SaveChangesAsync();
        await using var later = CreateDb();
        var replay = await new WorkspaceOnboardingRepository(later).TryConsumeInvitationAsync(
            stateHash, invitationId, workspaceId, tenantId, redeemerId, DateTimeOffset.UtcNow);
        replay.Should().BeFalse();
    }

    [Fact]
    public async Task Reissued_or_unredeemed_invitation_cannot_consume_an_old_completion_challenge()
    {
        var invitationId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var redeemerId = Guid.NewGuid();
        var oldNonceHash = Hash("CCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCCC");
        var state = new ConsentChallengeService(new byte[32])
            .CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));
        var stateHash = ConsentChallengeService.HashState(state.Challenge);
        await SeedRedeemableInvitationAsync(invitationId, workspaceId, tenantId, oldNonceHash, stateHash, state);

        await using var db = CreateDb();
        var repository = new WorkspaceOnboardingRepository(db);
        var unredeemed = await repository.TryConsumeInvitationAsync(
            stateHash, invitationId, workspaceId, tenantId, redeemerId, DateTimeOffset.UtcNow);
        unredeemed.Should().BeFalse();

        var reissued = await repository.ReissueAsync(new PlatformInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = "admin@example.com",
            DisplayName = "Admin",
            Role = "customer_admin",
            ModuleKeysJson = "[]",
            NonceHash = Hash("DDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDDD"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAt = DateTimeOffset.UtcNow
        }, invitationId, null);
        reissued.Should().BeTrue();

        var oldChallengeAfterReissue = await repository.TryConsumeInvitationAsync(
            stateHash, invitationId, workspaceId, tenantId, redeemerId, DateTimeOffset.UtcNow);
        oldChallengeAfterReissue.Should().BeFalse();
    }

    [Fact]
    public async Task Reissue_fails_if_redemption_wins_after_the_handler_read()
    {
        var invitationId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var redeemerId = Guid.NewGuid();
        const string nonce = "EEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEEE";
        var nonceHash = Hash(nonce);
        var state = new ConsentChallengeService(new byte[32])
            .CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));
        var stateHash = ConsentChallengeService.HashState(state.Challenge);
        await SeedRedeemableInvitationAsync(invitationId, workspaceId, tenantId, nonceHash, stateHash, state);

        await using var db = CreateDb();
        var repository = new WorkspaceOnboardingRepository(db);
        var redemption = await repository.RedeemAsync(nonceHash, tenantId, redeemerId, "admin@example.com", "Admin", stateHash);
        redemption.Should().NotBeNull();

        var created = await repository.ReissueAsync(new PlatformInvitation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            Email = "admin@example.com",
            DisplayName = "Admin",
            Role = "customer_admin",
            ModuleKeysJson = "[]",
            NonceHash = Hash("FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF"),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(2),
            CreatedAt = DateTimeOffset.UtcNow
        }, invitationId, null);

        created.Should().BeFalse();
        (await db.PlatformInvitations.CountAsync(x => x.WorkspaceId == workspaceId)).Should().Be(1);
        (await db.PlatformInvitations.SingleAsync(x => x.Id == invitationId)).RevokedAt.Should().BeNull();
    }

    private async Task SeedRedeemableInvitationAsync(
        Guid invitationId,
        Guid workspaceId,
        Guid tenantId,
        string nonceHash,
        string stateHash,
        SignedConsentChallenge state)
    {
        await using var db = CreateDb();
        await db.Workspaces.AddAsync(NewWorkspace(workspaceId, tenantId));
        await db.PlatformInvitations.AddAsync(NewInvitation(invitationId, workspaceId, nonceHash, state.ExpiresAt));
        await db.ConsentChallenges.AddAsync(NewChallenge(stateHash, workspaceId, tenantId, invitationId, state));
        await db.SaveChangesAsync();
    }

    private async Task SeedRedeemedInvitationAsync(
        Guid invitationId,
        Guid workspaceId,
        Guid tenantId,
        Guid redeemerId,
        string nonceHash,
        string stateHash,
        SignedConsentChallenge state)
    {
        await SeedRedeemableInvitationAsync(invitationId, workspaceId, tenantId, nonceHash, stateHash, state);
        await using var db = CreateDb();
        var invitation = await db.PlatformInvitations.SingleAsync(x => x.Id == invitationId);
        invitation.RedeemedAt = DateTimeOffset.UtcNow;
        invitation.RedeemedByTenantObjectId = redeemerId;
        db.WorkspaceMemberships.Add(new WorkspaceMembership
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            TenantObjectId = redeemerId,
            Email = "admin@example.com",
            PlatformRole = "customer_admin",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private WorkplaceDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<WorkplaceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private static Workspace NewWorkspace(Guid workspaceId, Guid tenantId) => new()
    {
        Id = workspaceId,
        TenantId = tenantId,
        DisplayName = "Race test workspace",
        ConnectionStatus = ConnectionState.AwaitingInvitation,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };

    private static PlatformInvitation NewInvitation(Guid invitationId, Guid workspaceId, string nonceHash, DateTimeOffset expiresAt) => new()
    {
        Id = invitationId,
        WorkspaceId = workspaceId,
        Email = "admin@example.com",
        DisplayName = "Admin",
        Role = "customer_admin",
        ModuleKeysJson = "[]",
        NonceHash = nonceHash,
        ExpiresAt = expiresAt,
        CreatedAt = DateTimeOffset.UtcNow
    };

    private static ConsentChallengeEntity NewChallenge(
        string stateHash,
        Guid workspaceId,
        Guid tenantId,
        Guid invitationId,
        SignedConsentChallenge state) => new ConsentChallengeEntity
    {
        StateHash = stateHash,
        WorkspaceId = workspaceId,
        TenantId = tenantId,
        InvitationId = invitationId,
        Purpose = "invitation",
        CorrelationId = state.CorrelationId,
        ExpiresAt = state.ExpiresAt
    };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
