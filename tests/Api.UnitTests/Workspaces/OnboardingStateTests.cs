using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class OnboardingStateTests
{
    [Theory]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.ConsentRequired)]
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.Connected)]
    [InlineData(ConnectionState.Connected, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.Connected, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.Connected, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionState.Connected, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionState.PermissionIncomplete, ConnectionState.ConsentRequired)]
    [InlineData(ConnectionState.TemporarilyUnavailable, ConnectionState.ConsentRequired)]
    [InlineData(ConnectionState.ConsentRevoked, ConnectionState.ConsentRequired)]
    [InlineData(ConnectionState.ConnectionFailed, ConnectionState.ConsentRequired)]
    public void Allows_documented_transition(string from, string to)
    {
        OnboardingStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.Connected)]
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.Connected, ConnectionState.AwaitingInvitation)]
    [InlineData(ConnectionState.ConsentRevoked, ConnectionState.Connected)]
    public void Rejects_undocumented_transition(string from, string to)
    {
        OnboardingStateMachine.CanTransition(from, to).Should().BeFalse();
    }
}
