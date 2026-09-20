using Atea.UnifiedWorkplace.Api.Features.Pim;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Pim;

public sealed class PimStateMapperTests
{
    [Theory]
    [InlineData("Eligible", "pim_activation_required")]
    [InlineData("PendingApproval", "pim_approval_required")]
    [InlineData("ApprovalRequired", "pim_approval_required")]
    [InlineData("MfaOnActivationRequired", "pim_mfa_required")]
    [InlineData("MfaRequired", "pim_mfa_required")]
    [InlineData("EligibilityExpired", "pim_eligibility_expired")]
    [InlineData("RoleEligibilityScheduleExpired", "pim_eligibility_expired")]
    public void Maps_graph_pim_status_to_exact_capability_state(string graphStatus, string expected)
    {
        PimStateMapper.ToCapabilityState(graphStatus).Should().Be(expected);
    }

    [Fact]
    public void Unknown_pim_status_fails_closed()
    {
        PimStateMapper.ToCapabilityState("UnexpectedNewStatus").Should().Be("temporarily_unavailable");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_or_blank_pim_status_fails_closed(string? graphStatus)
    {
        PimStateMapper.ToPimRequirement(graphStatus).Should().Be("temporarily_unavailable");
        PimStateMapper.ToCapabilityState(graphStatus).Should().Be("temporarily_unavailable");
    }
}
