using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class ConsentChallengeTests
{
    [Fact]
    public void Creates_unique_verifiable_challenges_without_tokens()
    {
        var service = new ConsentChallengeService();
        var first = service.Create(Guid.NewGuid(), Guid.NewGuid());
        var second = service.Create(Guid.NewGuid(), Guid.NewGuid());

        first.Challenge.Should().NotBe(second.Challenge);
        first.Challenge.Should().NotBe("delegated_consent_required");
        first.Challenge.Should().NotContain("token");
        service.Validate(first.Challenge, first.CorrelationId).Should().BeTrue();
    }
}
