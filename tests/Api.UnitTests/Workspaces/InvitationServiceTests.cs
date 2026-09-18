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

    private sealed class RecordingInvitationRepository : IInvitationRepository
    {
        public PlatformInvitation? Invitation { get; set; }
        public Workspace? Workspace { get; set; }
        public WorkspaceMembership? Membership { get; set; }

        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default) { Invitation = invitation; return Task.FromResult(invitation); }
        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string email, string displayName, CancellationToken cancellationToken = default)
        {
            if (Invitation is null || Invitation.NonceHash != nonceHash || Invitation.RedeemedAt is not null || Workspace is null || Workspace.TenantId != tenantId) return Task.FromResult<InvitationRedemption?>(null);
            Invitation.RedeemedAt = DateTimeOffset.UtcNow;
            Membership = new WorkspaceMembership { WorkspaceId = Workspace.Id, TenantObjectId = tenantObjectId, Email = email, PlatformRole = "customer_admin" };
            Workspace.ConnectionStatus = ConnectionState.ConsentRequired;
            return Task.FromResult<InvitationRedemption?>(new InvitationRedemption(Workspace, Membership));
        }
    }
}
