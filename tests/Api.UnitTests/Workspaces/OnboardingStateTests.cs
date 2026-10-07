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
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionState.ConsentRequired, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionState.PermissionIncomplete, ConnectionState.Connected)]
    [InlineData(ConnectionState.PermissionIncomplete, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.PermissionIncomplete, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionState.PermissionIncomplete, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionState.TemporarilyUnavailable, ConnectionState.Connected)]
    [InlineData(ConnectionState.TemporarilyUnavailable, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.TemporarilyUnavailable, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionState.TemporarilyUnavailable, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionState.ConsentRevoked, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.ConsentRevoked, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.ConsentRevoked, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionState.ConnectionFailed, ConnectionState.Connected)]
    [InlineData(ConnectionState.ConnectionFailed, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.ConnectionFailed, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.ConnectionFailed, ConnectionState.ConsentRevoked)]
    public void Allows_documented_transition(string from, string to)
    {
        OnboardingStateMachine.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.Connected)]
    [InlineData(ConnectionState.Connected, ConnectionState.AwaitingInvitation)]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.TemporarilyUnavailable)]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionState.AwaitingInvitation, ConnectionState.ConnectionFailed)]
    public void Rejects_undocumented_transition(string from, string to)
    {
        OnboardingStateMachine.CanTransition(from, to).Should().BeFalse();
    }
}
