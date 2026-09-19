using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;

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
}
