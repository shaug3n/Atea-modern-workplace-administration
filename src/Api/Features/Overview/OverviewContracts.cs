using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public interface IOverviewGraphReader
{
    Task<GraphReadResult<int>> ReadUserCountAsync(
        WorkspaceContext context,
        string delegatedScope,
        CancellationToken cancellationToken);

    Task<GraphReadResult<OverviewLicenseCounts>> ReadLicenseCountsAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}

public interface IOverviewActivityReader
{
    Task<IReadOnlyList<OverviewActivityItem>> ReadRecentAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}

public sealed record OverviewLicenseCounts(int TotalUsers, int AssignedUsers);

public sealed record OverviewActivityItem(string Action, string Outcome, DateTimeOffset Timestamp);

public sealed record OverviewSource<T>(
    string State,
    DateTimeOffset? FetchedAt,
    bool PartialData,
    T? Data,
    string Scope,
    string? ErrorCategory = null,
    string? RestrictedReason = null)
    where T : class;

public sealed record OverviewUserMetrics(int TotalUsers);

public sealed record OverviewLicenseCoverage(int AssignedUsers, int TotalUsers, int Percentage);

public sealed record OverviewActivityData(IReadOnlyList<OverviewActivityItem> Items);
