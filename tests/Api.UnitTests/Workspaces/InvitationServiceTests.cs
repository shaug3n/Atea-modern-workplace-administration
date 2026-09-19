using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class InvitationServiceTests
{
    [Fact]
    public async Task Creates_a_copyable_url_but_persists_only_the_hash()
    {
        var repository = new RecordingInvitationRepository();
        var service = new InvitationService(repository, new Uri("https://workplace.example"));

        var result = await service.CreateAsync(Guid.NewGuid(), "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddHours(1));

        result.InvitationUrl.Should().StartWith("https://workplace.example/invitations/");
        repository.Invitation!.NonceHash.Should().NotBe(result.InvitationUrl.Split('/').Last());
        repository.Invitation.NonceHash.Should().HaveLength(64);
        result.InvitationUrl.Should().NotContain(repository.Invitation.NonceHash);
    }

    [Fact]
    public async Task Redeeming_an_invitation_is_one_time_and_creates_membership()
    {
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var objectId = Guid.NewGuid();
        var repository = new RecordingInvitationRepository
        {
            Workspace = new Workspace { Id = workspaceId, TenantId = tenantId, ConnectionStatus = ConnectionState.AwaitingInvitation }
        };
        var service = new InvitationService(repository, new Uri("https://workplace.example"));
        var created = await service.CreateAsync(workspaceId, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddHours(1));

        var redeemed = await service.RedeemAsync(created.InvitationUrl.Split('/').Last(), tenantId, objectId, "admin@example.com", "Admin", CancellationToken.None);

        redeemed.Should().BeTrue();
        repository.Invitation!.RedeemedAt.Should().NotBeNull();
        repository.Membership!.TenantObjectId.Should().Be(objectId);
        repository.Workspace!.ConnectionStatus.Should().Be(ConnectionState.ConsentRequired);
    }

    [Fact]
    public async Task Rejects_expired_invitation_without_redeeming_it()
    {
        var repository = new RecordingInvitationRepository { Workspace = new Workspace { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() } };
        var service = new InvitationService(repository, new Uri("https://workplace.example"));
        var created = await service.CreateAsync(repository.Workspace.Id, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(-1));

        (await service.RedeemAsync(created.InvitationUrl.Split('/').Last(), repository.Workspace.TenantId, Guid.NewGuid(), "admin@example.com", "Admin")).Should().BeFalse();
        repository.Invitation!.RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task Rejects_email_and_tenant_mismatch()
    {
        var repository = new RecordingInvitationRepository { Workspace = new Workspace { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() } };
        var service = new InvitationService(repository, new Uri("https://workplace.example"));
        var created = await service.CreateAsync(repository.Workspace.Id, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(1));
        var nonce = created.InvitationUrl.Split('/').Last();

        (await service.RedeemAsync(nonce, repository.Workspace.TenantId, Guid.NewGuid(), "other@example.com", "Other")).Should().BeFalse();
        (await service.RedeemAsync(nonce, Guid.NewGuid(), Guid.NewGuid(), "admin@example.com", "Admin")).Should().BeFalse();
        repository.Invitation!.RedeemedAt.Should().BeNull();
    }

    [Fact]
    public async Task Concurrent_replay_allows_only_one_redemption()
    {
        var repository = new ConcurrentInvitationRepository(Guid.NewGuid());
        var service = new InvitationService(repository, new Uri("https://workplace.example"));
        var created = await service.CreateAsync(repository.WorkspaceId, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(1));
        var nonce = created.InvitationUrl.Split('/').Last();

        var results = await Task.WhenAll(
            service.RedeemAsync(nonce, repository.TenantId, Guid.NewGuid(), "admin@example.com", "Admin"),
            service.RedeemAsync(nonce, repository.TenantId, Guid.NewGuid(), "admin@example.com", "Admin"));

        results.Count(result => result).Should().Be(1);
        repository.MembershipCount.Should().Be(1);
    }

    [Fact]
    public async Task Redeems_when_approved_object_id_matches_even_if_email_differs()
    {
        var repository = new RecordingInvitationRepository { Workspace = new Workspace { Id = Guid.NewGuid(), TenantId = Guid.NewGuid() } };
        var service = new InvitationService(repository, new Uri("https://workplace.example"));
        var approvedObjectId = Guid.NewGuid();
        var created = await service.CreateAsync(repository.Workspace.Id, "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddMinutes(1), approvedObjectId);

        (await service.RedeemAsync(created.InvitationUrl.Split('/').Last(), repository.Workspace.TenantId, approvedObjectId, "guest@example.com", "Guest")).Should().BeTrue();
    }

    private sealed class RecordingInvitationRepository : IInvitationRepository
    {
        public PlatformInvitation? Invitation { get; set; }
        public Workspace? Workspace { get; set; }
        public WorkspaceMembership? Membership { get; set; }

        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default) { Invitation = invitation; return Task.FromResult(invitation); }
        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string email, string displayName, CancellationToken cancellationToken = default)
        {
            if (Invitation is null || Invitation.NonceHash != nonceHash || Invitation.RedeemedAt is not null || Invitation.ExpiresAt <= DateTimeOffset.UtcNow || Workspace is null || Workspace.TenantId != tenantId || (!string.Equals(Invitation.Email, email, StringComparison.OrdinalIgnoreCase) && Invitation.ApprovedTenantObjectId != tenantObjectId)) return Task.FromResult<InvitationRedemption?>(null);
            Invitation.RedeemedAt = DateTimeOffset.UtcNow;
            Membership = new WorkspaceMembership { WorkspaceId = Workspace.Id, TenantObjectId = tenantObjectId, Email = email, PlatformRole = "customer_admin" };
            Workspace.ConnectionStatus = ConnectionState.ConsentRequired;
            return Task.FromResult<InvitationRedemption?>(new InvitationRedemption(Workspace, Membership));
        }
    }

    private sealed class ConcurrentInvitationRepository(Guid tenantId) : IInvitationRepository
    {
        private readonly object gate = new();
        private PlatformInvitation? invitation;
        public Guid WorkspaceId { get; } = Guid.NewGuid();
        public Guid TenantId { get; } = tenantId;
        public int MembershipCount { get; private set; }

        public Task<PlatformInvitation> CreateAsync(PlatformInvitation value, CancellationToken cancellationToken = default) { invitation = value; return Task.FromResult(value); }
        public async Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenant, Guid objectId, string email, string displayName, CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            lock (gate)
            {
                if (invitation is null || invitation.NonceHash != nonceHash || invitation.RedeemedAt is not null || tenant != TenantId || !string.Equals(email, invitation.Email, StringComparison.OrdinalIgnoreCase)) return null;
                invitation.RedeemedAt = DateTimeOffset.UtcNow;
                MembershipCount++;
                return new InvitationRedemption(new Workspace { Id = WorkspaceId, TenantId = TenantId, ConnectionStatus = ConnectionState.ConsentRequired }, new WorkspaceMembership { WorkspaceId = WorkspaceId, TenantObjectId = objectId });
            }
        }
    }
}
