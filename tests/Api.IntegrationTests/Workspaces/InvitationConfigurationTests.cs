using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Workspaces;

public sealed class InvitationConfigurationTests
{
    [Fact]
    public async Task Invitation_url_uses_the_configured_local_public_base_url()
    {
        var repository = new RecordingInvitationRepository();
        var service = new InvitationService(repository, new Uri("http://localhost:5173"));

        var result = await service.CreateAsync(Guid.NewGuid(), "admin@example.com", "Admin", DateTimeOffset.UtcNow.AddHours(1));

        result.InvitationUrl.Should().StartWith("http://localhost:5173/invitations/");
    }

    private sealed class RecordingInvitationRepository : IInvitationRepository
    {
        public Task<PlatformInvitation> CreateAsync(PlatformInvitation invitation, CancellationToken cancellationToken = default) => Task.FromResult(invitation);

        public Task<InvitationRedemption?> RedeemAsync(string nonceHash, Guid tenantId, Guid tenantObjectId, string? email, string displayName, CancellationToken cancellationToken = default) => Task.FromResult<InvitationRedemption?>(null);
    }
}
