using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public static class ConnectionState
{
    public const string AwaitingInvitation = "awaiting_invitation";
    public const string ConsentRequired = "consent_required";
    public const string Connected = "connected";
    public const string PermissionIncomplete = "permission_incomplete";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
    public const string ConsentRevoked = "consent_revoked";
    public const string ConnectionFailed = "connection_failed";
}

public static class OnboardingStateMachine
{
    private static readonly string[] PostRedemptionStates =
    [
        ConnectionState.ConsentRequired,
        ConnectionState.Connected,
        ConnectionState.PermissionIncomplete,
        ConnectionState.TemporarilyUnavailable,
        ConnectionState.ConsentRevoked,
        ConnectionState.ConnectionFailed
    ];
    private static readonly IReadOnlySet<(string From, string To)> AllowedTransitions = BuildAllowedTransitions();

    public static bool CanTransition(string from, string to) => AllowedTransitions.Contains((from, to));
    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to)) throw new InvalidOperationException($"Invalid connection state transition from '{from}' to '{to}'.");
    }

    private static IReadOnlySet<(string From, string To)> BuildAllowedTransitions()
    {
        var transitions = new HashSet<(string From, string To)>
        {
            (ConnectionState.AwaitingInvitation, ConnectionState.ConsentRequired)
        };
        foreach (var from in PostRedemptionStates)
        foreach (var to in PostRedemptionStates)
            transitions.Add((from, to));
        return transitions;
    }
}

public interface IOnboardingService
{
    Task<WorkspaceOnboardingState> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
    Task<WorkspaceOnboardingState> TransitionAsync(Guid workspaceId, string targetState, string? failureCategory = null, CancellationToken cancellationToken = default);
    Task<WorkspaceOnboardingState> RecordCheckAsync(Guid workspaceId, string status, IReadOnlyCollection<string> scopes, string? failureCategory = null, CancellationToken cancellationToken = default);
}

public sealed record WorkspaceOnboardingState(Guid WorkspaceId, string ConnectionStatus, DateTimeOffset? LastVerifiedAt, IReadOnlyCollection<string> ConsentScopes, string? FailureCategory);

public sealed class OnboardingService(IOnboardingRepository repository) : IOnboardingService
{
    public async Task<WorkspaceOnboardingState> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => ToState(await repository.GetConnectionAsync(workspaceId, cancellationToken) ?? throw new KeyNotFoundException("Workspace not found."));

    public async Task<WorkspaceOnboardingState> TransitionAsync(Guid workspaceId, string targetState, string? failureCategory = null, CancellationToken cancellationToken = default)
    {
        var current = await repository.GetConnectionAsync(workspaceId, cancellationToken) ?? throw new KeyNotFoundException("Workspace not found.");
        OnboardingStateMachine.EnsureTransition(current.ConnectionStatus, targetState);
        return ToState(await repository.UpdateConnectionAsync(workspaceId, targetState, current.ConsentScopes, DateTimeOffset.UtcNow, failureCategory, cancellationToken));
    }

    public async Task<WorkspaceOnboardingState> RecordCheckAsync(Guid workspaceId, string status, IReadOnlyCollection<string> scopes, string? failureCategory = null, CancellationToken cancellationToken = default)
    {
        var current = await repository.GetConnectionAsync(workspaceId, cancellationToken) ?? throw new KeyNotFoundException("Workspace not found.");
        if (!string.Equals(current.ConnectionStatus, status, StringComparison.Ordinal)) OnboardingStateMachine.EnsureTransition(current.ConnectionStatus, status);
        return ToState(await repository.UpdateConnectionAsync(workspaceId, status, scopes, DateTimeOffset.UtcNow, failureCategory, cancellationToken));
    }

    private static WorkspaceOnboardingState ToState(ConnectionSnapshot snapshot) => new(snapshot.WorkspaceId, snapshot.ConnectionStatus, snapshot.LastVerifiedAt, snapshot.ConsentScopes, snapshot.FailureCategory);
}
