using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public interface IOverviewService
{
    Task<OverviewResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken);
}

public sealed class OverviewService(
    IOverviewDataReader dataReader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    Func<DateTimeOffset>? utcNow = null,
    TimeSpan? cacheLifetime = null,
    OverviewDataCache? cache = null) : IOverviewService
{
    private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromSeconds(30);
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan cacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
    private readonly OverviewDataCache cache = cache ?? new();

    public async Task<OverviewResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (this.cacheLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheLifetime), "Overview cache lifetime must be positive.");
        }

        var now = utcNow();
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var capabilities = CapabilityEvaluator.Evaluate(snapshot, context.Membership);
        var access = capabilities[Capability.UsersView];
        var permissionHealth = PermissionHealth(snapshot, capabilities);
        var pimAttention = PimAttention(snapshot);

        if (access.State != CapabilityState.Allowed)
        {
            return Unavailable(now, access, permissionHealth, pimAttention, null, "capability_required");
        }

        if (this.cache.TryGet(context.Membership.WorkspaceId, now, this.cacheLifetime, out var cached))
        {
            return Response(cached.Data, cached.FetchedAt, "cached", false, access, permissionHealth, pimAttention, null);
        }

        var result = await dataReader.ReadAsync(context, cancellationToken);
        if (result.Error is null)
        {
            this.cache.Set(context.Membership.WorkspaceId, new OverviewCacheEntry(result.Value, now));
            return Response(result.Value, now, "live", false, access, permissionHealth, pimAttention, null);
        }

        if (this.cache.TryGetAny(context.Membership.WorkspaceId, out cached))
        {
            return Response(cached.Data, cached.FetchedAt, "stale", true, access, permissionHealth, pimAttention, result.Error.Category);
        }

        return Unavailable(now, access, permissionHealth, pimAttention, result.Error.Category, result.Error.Category);
    }

    private static OverviewResponse Unavailable(
        DateTimeOffset fetchedAt,
        CapabilityDecision access,
        PermissionHealthSummary permissionHealth,
        PimAttentionSummary pimAttention,
        string? errorCategory,
        string? fallbackErrorCategory) =>
        new(
            "unavailable",
            fetchedAt,
            0,
            new LicenseCoverageSummary(0, 0, 0),
            permissionHealth,
            pimAttention,
            true,
            access,
            errorCategory ?? fallbackErrorCategory);

    private static OverviewResponse Response(
        OverviewData data,
        DateTimeOffset fetchedAt,
        string freshness,
        bool partialData,
        CapabilityDecision access,
        PermissionHealthSummary permissionHealth,
        PimAttentionSummary pimAttention,
        string? errorCategory)
    {
        var available = Math.Max(0, data.AvailableLicenses);
        var percentage = data.TotalUsers == 0
            ? 0
            : (int)Math.Round(data.AssignedUsers * 100d / data.TotalUsers, MidpointRounding.AwayFromZero);

        return new OverviewResponse(
            freshness,
            fetchedAt,
            data.TotalUsers,
            new LicenseCoverageSummary(data.AssignedUsers, available, percentage),
            permissionHealth,
            pimAttention,
            partialData,
            access,
            errorCategory);
    }

    private static PermissionHealthSummary PermissionHealth(
        GraphAuthorizationSnapshot snapshot,
        CapabilitySnapshot capabilities)
    {
        var decisions = capabilities.Capabilities.ToArray();
        var allowedCount = decisions.Count(decision => decision.State == CapabilityState.Allowed);
        var state = !snapshot.IsAvailable
            ? snapshot.ProblemCategory ?? CapabilityState.TemporarilyUnavailable
            : allowedCount == decisions.Length
                ? "healthy"
                : "incomplete";

        return new PermissionHealthSummary(state, allowedCount, decisions.Length);
    }

    private static PimAttentionSummary PimAttention(GraphAuthorizationSnapshot snapshot)
    {
        var attentionCount = snapshot.DirectoryRoles.Count(role => role.Pim is not null && !string.Equals(role.Pim.State, "active", StringComparison.OrdinalIgnoreCase));
        return new PimAttentionSummary(attentionCount > 0, attentionCount);
    }
}

public sealed class OverviewDataCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, OverviewCacheEntry> entries = new();

    public bool TryGet(Guid workspaceId, DateTimeOffset now, TimeSpan lifetime, out OverviewCacheEntry entry)
    {
        if (entries.TryGetValue(workspaceId, out entry!) && now - entry.FetchedAt <= lifetime)
        {
            return true;
        }

        entry = default!;
        return false;
    }

    public bool TryGetAny(Guid workspaceId, out OverviewCacheEntry entry) => entries.TryGetValue(workspaceId, out entry!);

    public void Set(Guid workspaceId, OverviewCacheEntry entry) => entries[workspaceId] = entry;
}

public sealed record OverviewCacheEntry(OverviewData Data, DateTimeOffset FetchedAt);

public sealed record OverviewData(int TotalUsers, int AssignedUsers, int AvailableLicenses);

public sealed record OverviewResponse(
    string Freshness,
    DateTimeOffset FetchedAt,
    int TotalUsers,
    LicenseCoverageSummary LicenseCoverage,
    PermissionHealthSummary PermissionHealth,
    PimAttentionSummary PimAttention,
    bool PartialData,
    CapabilityDecision Access,
    string? ErrorCategory = null);

public sealed record LicenseCoverageSummary(int Assigned, int Available, int Percentage);

public sealed record PermissionHealthSummary(string State, int AllowedCount, int TotalCount);

public sealed record PimAttentionSummary(bool RequiresAttention, int Count);
