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
    private static readonly IReadOnlySet<(string From, string To)> AllowedTransitions = new HashSet<(string, string)>
    {
        (ConnectionState.AwaitingInvitation, ConnectionState.ConsentRequired),
        (ConnectionState.ConsentRequired, ConnectionState.Connected),
        (ConnectionState.Connected, ConnectionState.PermissionIncomplete),
        (ConnectionState.Connected, ConnectionState.TemporarilyUnavailable),
        (ConnectionState.Connected, ConnectionState.ConsentRevoked),
        (ConnectionState.Connected, ConnectionState.ConnectionFailed),
        (ConnectionState.PermissionIncomplete, ConnectionState.ConsentRequired),
        (ConnectionState.TemporarilyUnavailable, ConnectionState.ConsentRequired),
        (ConnectionState.ConsentRevoked, ConnectionState.ConsentRequired),
        (ConnectionState.ConnectionFailed, ConnectionState.ConsentRequired)
    };

    public static bool CanTransition(string from, string to) => AllowedTransitions.Contains((from, to));
    public static void EnsureTransition(string from, string to)
    {
        if (!CanTransition(from, to)) throw new InvalidOperationException($"Invalid connection state transition from '{from}' to '{to}'.");
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
