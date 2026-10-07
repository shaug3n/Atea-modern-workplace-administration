using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record PermissionCoverage(
    IReadOnlyCollection<string> AvailableScopes,
    IReadOnlyCollection<string> MissingScopes,
    IReadOnlyCollection<string> UnknownScopes);

public sealed record WorkspaceConnectionVerification(
    ConnectionHealthDto Health,
    PermissionCoverage? PermissionCoverage);

public interface IWorkspaceConnectionVerifier
{
    Task<WorkspaceConnectionVerification> VerifyAsync(
        WorkspaceContext context,
        bool includePermissionCoverage,
        CancellationToken cancellationToken = default);
}

public sealed class ConnectionVerificationService(
    IConnectionHealthReader connectionHealthReader,
    IDelegatedScopeAvailabilityReader scopeAvailabilityReader,
    IOnboardingService onboarding,
    TimeSpan? verificationTimeout = null) : IWorkspaceConnectionVerifier
{
    private static readonly TimeSpan DefaultVerificationTimeout = TimeSpan.FromSeconds(30);
    private readonly TimeSpan timeout = verificationTimeout ?? DefaultVerificationTimeout;

    public async Task<WorkspaceConnectionVerification> VerifyAsync(
        WorkspaceContext context,
        bool includePermissionCoverage,
        CancellationToken cancellationToken = default)
    {
        if (!includePermissionCoverage)
        {
            var baselineOnly = await connectionHealthReader.ReadAsync(context.User.TenantId, cancellationToken);
            return await RecordBaselineAsync(context, baselineOnly, permissionCoverage: null, cancellationToken);
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        ConnectionHealthReadResult baseline;
        try
        {
            baseline = await connectionHealthReader.ReadAsync(context.User.TenantId, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            baseline = new ConnectionHealthReadResult(
                ConnectionHealthStatus.TemporarilyUnavailable,
                [],
                "temporarily_unavailable");
        }

        if (baseline.Status != ConnectionHealthStatus.Connected)
        {
            return await RecordBaselineAsync(context, baseline, permissionCoverage: null, cancellationToken);
        }

        IReadOnlyCollection<DelegatedScopeResult> scopeResults;
        try
        {
            scopeResults = await scopeAvailabilityReader.ReadAsync(
                GraphScopeCatalog.CapabilityEvaluationScopes,
                timeoutSource.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            scopeResults = GraphScopeCatalog.CapabilityEvaluationScopes
                .Select(scope => new DelegatedScopeResult(scope, ScopeAvailability.Unknown, "temporarily_unavailable"))
                .ToArray();
        }
        catch (Exception)
        {
            scopeResults = GraphScopeCatalog.CapabilityEvaluationScopes
                .Select(scope => new DelegatedScopeResult(scope, ScopeAvailability.Unknown, "temporarily_unavailable"))
                .ToArray();
        }

        var byScope = scopeResults
            .GroupBy(result => result.Scope, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        var availableScopes = new List<string>();
        var missingScopes = new List<string>();
        var unknownScopes = new List<string>();
        foreach (var scope in GraphScopeCatalog.CapabilityEvaluationScopes)
        {
            var scopeStatus = byScope.TryGetValue(scope, out var result)
                ? result.Status
                : ScopeAvailability.Unknown;
            switch (scopeStatus)
            {
                case ScopeAvailability.Available:
                    availableScopes.Add(scope);
                    break;
                case ScopeAvailability.MissingConsent:
                    missingScopes.Add(scope);
                    break;
                default:
                    unknownScopes.Add(scope);
                    break;
            }
        }

        var coverage = new PermissionCoverage(availableScopes, missingScopes, unknownScopes);
        var status = missingScopes.Count > 0
            ? ConnectionState.PermissionIncomplete
            : unknownScopes.Count > 0
                ? ConnectionState.TemporarilyUnavailable
                : ConnectionState.Connected;
        var failureCategory = status == ConnectionState.PermissionIncomplete
            ? "permission_incomplete"
            : status == ConnectionState.TemporarilyUnavailable
                ? "temporarily_unavailable"
                : null;
        var recorded = await onboarding.RecordCheckAsync(
            context.Membership.WorkspaceId,
            status,
            coverage.AvailableScopes,
            failureCategory,
            cancellationToken);
        return new WorkspaceConnectionVerification(ToHealth(recorded, correlationId: string.Empty), coverage);
    }

    private async Task<WorkspaceConnectionVerification> RecordBaselineAsync(
        WorkspaceContext context,
        ConnectionHealthReadResult result,
        PermissionCoverage? permissionCoverage,
        CancellationToken cancellationToken)
    {
        var status = ToConnectionState(result.Status);
        var recorded = await onboarding.RecordCheckAsync(
            context.Membership.WorkspaceId,
            status,
            result.GrantedScopes,
            result.ProblemCategory,
            cancellationToken);
        return new WorkspaceConnectionVerification(ToHealth(recorded, correlationId: string.Empty), permissionCoverage);
    }

    private static string ToConnectionState(ConnectionHealthStatus status) =>
        status switch
        {
            ConnectionHealthStatus.Connected => ConnectionState.Connected,
            ConnectionHealthStatus.ConsentRequired => ConnectionState.ConsentRequired,
            ConnectionHealthStatus.PermissionIncomplete => ConnectionState.PermissionIncomplete,
            ConnectionHealthStatus.ConsentRevoked => ConnectionState.ConsentRevoked,
            ConnectionHealthStatus.ConnectionFailed => ConnectionState.ConnectionFailed,
            _ => ConnectionState.TemporarilyUnavailable
        };

    private static ConnectionHealthDto ToHealth(WorkspaceOnboardingState state, string correlationId) =>
        new(
            state.WorkspaceId,
            state.ConnectionStatus,
            state.LastVerifiedAt,
            state.ConsentScopes,
            state.FailureCategory,
            correlationId);
}
