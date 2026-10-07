using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class ConsentChallengeTests
{
    [Fact]
    public void Creates_unique_verifiable_challenges_without_tokens()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var first = service.Create(workspaceId, tenantId);
        var second = service.Create(workspaceId, tenantId);

        first.Challenge.Should().NotBe(second.Challenge);
        first.Challenge.Should().NotBe("delegated_consent_required");
        first.Challenge.Should().NotContain("token");
        service.Validate(first.Challenge, first.CorrelationId, workspaceId, tenantId).Should().BeTrue();
    }

    [Fact]
    public void Rejects_cross_workspace_and_cross_tenant_validation()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var challenge = service.Create(workspaceId, tenantId);

        service.Validate(challenge.Challenge, challenge.CorrelationId, Guid.NewGuid(), tenantId).Should().BeFalse();
        service.Validate(challenge.Challenge, challenge.CorrelationId, workspaceId, Guid.NewGuid()).Should().BeFalse();
    }

    [Fact]
    public void Unconfigured_service_fails_closed()
    {
        var service = new ConsentChallengeService((string?)null);
        service.IsConfigured.Should().BeFalse();
        FluentActions.Invoking(() => service.Create(Guid.NewGuid(), Guid.NewGuid())).Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task Validates_and_consumes_a_challenge_once_without_exposing_payload()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var challenge = service.Create(workspaceId, tenantId);

        var repository = new RecordingRepository();
        (await service.TryValidateAndConsumeAsync(challenge.Challenge, workspaceId, tenantId, repository)).Should().BeTrue();
        repository.Consumed.Should().Be(1);
        (await service.TryValidateAndConsumeAsync(challenge.Challenge, workspaceId, tenantId, repository)).Should().BeFalse();
    }

    [Fact]
    public async Task Rejects_tampered_and_cross_tenant_challenges_without_consuming_valid_state()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var challenge = service.Create(workspaceId, tenantId);

        var repository = new RecordingRepository();
        (await service.TryValidateAndConsumeAsync($"{challenge.Challenge}x", workspaceId, tenantId, repository)).Should().BeFalse();
        (await service.TryValidateAndConsumeAsync(challenge.Challenge, workspaceId, Guid.NewGuid(), repository)).Should().BeFalse();
        (await service.TryValidateAndConsumeAsync(challenge.Challenge, workspaceId, tenantId, repository)).Should().BeTrue();
    }

    [Fact]
    public void Invitation_challenge_expires_at_twenty_minutes_or_invitation_expiry()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;
        var challenge = service.CreateInvitation(workspaceId, tenantId, invitationId, before.AddDays(1));
        var after = DateTimeOffset.UtcNow;

        challenge.ExpiresAt.ToUnixTimeSeconds().Should().BeInRange(
            before.AddMinutes(20).ToUnixTimeSeconds(),
            after.AddMinutes(20).ToUnixTimeSeconds());

        var invitationExpiresAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds());
        var shortChallenge = service.CreateInvitation(workspaceId, tenantId, invitationId, invitationExpiresAt);
        shortChallenge.ExpiresAt.Should().Be(invitationExpiresAt);
    }

    [Fact]
    public void Legacy_challenges_remain_valid_but_wrong_invitation_binding_is_rejected()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var legacy = service.Create(workspaceId, tenantId);
        var invitation = service.CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));

        service.TryRead(legacy.Challenge, workspaceId, tenantId, out _).Should().BeTrue();
        service.TryReadInvitation(invitation.Challenge, workspaceId, tenantId, invitationId, out var payload).Should().BeTrue();
        payload.Purpose.Should().Be("invitation");
        payload.WorkspaceId.Should().Be(workspaceId);
        payload.TenantId.Should().Be(tenantId);
        payload.InvitationId.Should().Be(invitationId);
        payload.CorrelationId.Should().Be(invitation.CorrelationId);
        payload.ExpiresAt.Should().Be(invitation.ExpiresAt);

        service.TryReadInvitation(invitation.Challenge, workspaceId, tenantId, Guid.NewGuid(), out _).Should().BeFalse();
        service.TryReadInvitation(invitation.Challenge, Guid.NewGuid(), tenantId, invitationId, out _).Should().BeFalse();
        service.TryReadInvitation(invitation.Challenge, workspaceId, Guid.NewGuid(), invitationId, out _).Should().BeFalse();
    }

    [Fact]
    public void Invitation_challenge_rejects_tampering_unknown_version_purpose_malformed_and_expired_values()
    {
        var key = new byte[32];
        var service = new ConsentChallengeService(key);
        var workspaceId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var invitationId = Guid.NewGuid();
        var valid = service.CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1));

        service.TryReadInvitation($"{valid.Challenge}x", workspaceId, tenantId, invitationId, out _).Should().BeFalse();
        service.TryReadInvitation(Sign(key, InvitationPayload(workspaceId, tenantId, invitationId, "not-a-time")), workspaceId, tenantId, invitationId, out _).Should().BeFalse();
        service.TryReadInvitation(Sign(key, InvitationPayload(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(), version: "v9")), workspaceId, tenantId, invitationId, out _).Should().BeFalse();
        service.TryReadInvitation(Sign(key, InvitationPayload(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeSeconds().ToString(), purpose: "workspace")), workspaceId, tenantId, invitationId, out _).Should().BeFalse();
        service.TryReadInvitation("a" + new string('a', 4096), workspaceId, tenantId, invitationId, out _).Should().BeFalse();

        var expired = service.CreateInvitation(workspaceId, tenantId, invitationId, DateTimeOffset.UtcNow.AddMinutes(-1));
        service.TryReadInvitation(expired.Challenge, workspaceId, tenantId, invitationId, out _).Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_invitation_state_does_not_reach_legacy_repository_consume()
    {
        var service = new ConsentChallengeService(new byte[32]);
        var invitation = service.CreateInvitation(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTimeOffset.UtcNow.AddHours(1));
        var repository = new RecordingRepository();

        (await service.TryValidateAndConsumeAsync(invitation.Challenge, Guid.NewGuid(), Guid.NewGuid(), repository)).Should().BeFalse();
        repository.Consumed.Should().Be(0);
    }

    private static string InvitationPayload(
        Guid workspaceId,
        Guid tenantId,
        Guid invitationId,
        object expiresAt,
        string version = "v1",
        string purpose = "invitation") =>
        $"{version}|{purpose}|{Guid.NewGuid():N}|{workspaceId:N}|{tenantId:N}|{invitationId:N}|{expiresAt}|{Convert.ToBase64String(new byte[24]).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    private static string Sign(byte[] key, string payload)
    {
        var encodedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var signature = Convert.ToBase64String(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(encodedPayload)))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{encodedPayload}.{signature}";
    }

    private sealed class RecordingRepository : IConsentChallengeRepository
    {
        public int Consumed { get; private set; }

        public Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            if (Consumed > 0) return Task.FromResult(false);
            Consumed++;
            return Task.FromResult(true);
        }
    }
}
