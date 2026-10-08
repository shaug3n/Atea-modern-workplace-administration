using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Extensions.Logging;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public interface IOverviewService
{
    Task<OverviewResponse> GetAsync(
        WorkspaceContext context,
        IReadOnlyCollection<string> effectiveModules,
        CancellationToken cancellationToken);
}

public sealed class OverviewService(
    IOverviewGraphReader graphReader,
    IOverviewActivityReader activityReader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    Func<DateTimeOffset>? utcNow = null,
    TimeSpan? cacheLifetime = null,
    OverviewDataCache? cache = null,
    ILogger<OverviewService>? logger = null) : IOverviewService
{
    private static readonly TimeSpan DefaultCacheLifetime = TimeSpan.FromSeconds(30);
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);
    private readonly TimeSpan cacheLifetime = cacheLifetime ?? DefaultCacheLifetime;
    private readonly OverviewDataCache cache = cache ?? new();
    private readonly ILogger<OverviewService> logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<OverviewService>.Instance;

    public async Task<OverviewResponse> GetAsync(
        WorkspaceContext context,
        IReadOnlyCollection<string> effectiveModules,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(effectiveModules);
        if (cacheLifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(cacheLifetime), "Overview cache lifetime must be positive.");
        }

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var capabilitySnapshot = CapabilityEvaluator.Evaluate(snapshot, context.Membership);
        var usersDecision = capabilitySnapshot[Capability.UsersView];
        var licensesDecision = capabilitySnapshot[Capability.LicensesView];
        var devicesDecision = capabilitySnapshot[Capability.DevicesView];
        var auditDecision = CapabilityEvaluator.EvaluatePlatformCapability(Capability.AuditView, context.Membership);
        var effectiveCapabilities = new[] { usersDecision, licensesDecision, devicesDecision, auditDecision };
        var modules = effectiveModules.ToArray();
        var usersEnabled = IsEnabled(modules, "users");
        var licensesEnabled = IsEnabled(modules, "licenses");
        var users = usersEnabled && IsAllowed(usersDecision)
            ? await ReadUsersAsync(context, snapshot, usersDecision, cancellationToken)
            : Blocked<OverviewUserMetrics>(usersDecision, usersEnabled, "unknown");

        var licenses = licensesEnabled && IsAllowed(licensesDecision)
            ? await ReadLicensesAsync(context, snapshot, licensesDecision, cancellationToken)
            : Blocked<OverviewLicenseCoverage>(licensesDecision, licensesEnabled, "unknown");

        var activity = IsAllowed(auditDecision)
            ? await ReadActivityAsync(context, auditDecision, cancellationToken)
            : Restricted<OverviewActivityData>(auditDecision, "workspace");

        return new OverviewResponse(modules, effectiveCapabilities, users, licenses, activity);
    }

    private async Task<OverviewSource<OverviewUserMetrics>> ReadUsersAsync(
        WorkspaceContext context,
        GraphAuthorizationSnapshot snapshot,
        CapabilityDecision decision,
        CancellationToken cancellationToken)
    {
        var scope = SelectUserReadScope(snapshot);
        if (scope is null)
        {
            return Unavailable<OverviewUserMetrics>("tenant_wide", "required_scope_unavailable");
        }

        var fingerprint = Fingerprint(decision, snapshot.UserObjectId, scope, "module:users");
        var key = CacheKey(context, "users", fingerprint);
        var now = utcNow();
        if (cache.TryGet(key, now, cacheLifetime, out var cached))
        {
            return Source("fresh", cached.FetchedAt, false, (OverviewUserMetrics)cached.Data, "tenant_wide");
        }

        try
        {
            var result = await graphReader.ReadUserCountAsync(context, scope, cancellationToken);
            if (result.Error is null && result.Value >= 0)
            {
                var fetchedAt = utcNow();
                var data = new OverviewUserMetrics(result.Value);
                cache.Set(key, new OverviewCacheEntry(data, fetchedAt));
                return Source("fresh", fetchedAt, false, data, "tenant_wide");
            }

            var category = result.Error?.Category ?? "invalid_response";
            logger.LogWarning(
                "Overview users source returned failure category {Category} for workspace {WorkspaceId}",
                category,
                context.Membership.WorkspaceId);
            return UserFailure(key, category);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Overview users source failed for workspace {WorkspaceId}", context.Membership.WorkspaceId);
            return UserFailure(key, "source_unavailable");
        }
    }

    private async Task<OverviewSource<OverviewLicenseCoverage>> ReadLicensesAsync(
        WorkspaceContext context,
        GraphAuthorizationSnapshot snapshot,
        CapabilityDecision decision,
        CancellationToken cancellationToken)
    {
        if (!HasScope(snapshot, "Directory.Read.All"))
        {
            return Unavailable<OverviewLicenseCoverage>("tenant_wide", "required_scope_unavailable");
        }

        var fingerprint = Fingerprint(decision, snapshot.UserObjectId, "Directory.Read.All", "module:licenses");
        var key = CacheKey(context, "licenses", fingerprint);
        var now = utcNow();
        if (cache.TryGet(key, now, cacheLifetime, out var cached))
        {
            return Source("fresh", cached.FetchedAt, false, (OverviewLicenseCoverage)cached.Data, "tenant_wide");
        }

        try
        {
            var result = await graphReader.ReadLicenseCountsAsync(context, cancellationToken);
            if (result.Error is not null)
            {
                logger.LogWarning(
                    "Overview licenses source returned failure category {Category} for workspace {WorkspaceId}",
                    result.Error.Category,
                    context.Membership.WorkspaceId);
                return LicenseFailure(key, result.Error.Category);
            }

            var counts = result.Value;
            if (counts.TotalUsers < 0 || counts.AssignedUsers < 0 || counts.AssignedUsers > counts.TotalUsers)
            {
                logger.LogWarning(
                    "Overview licenses source returned invalid counts for workspace {WorkspaceId}",
                    context.Membership.WorkspaceId);
                return LicenseFailure(key, "invalid_response");
            }

            var percentage = counts.TotalUsers == 0
                ? 0
                : (int)Math.Round(counts.AssignedUsers * 100d / counts.TotalUsers, MidpointRounding.AwayFromZero);
            var data = new OverviewLicenseCoverage(counts.AssignedUsers, counts.TotalUsers, percentage);
            var fetchedAt = utcNow();
            cache.Set(key, new OverviewCacheEntry(data, fetchedAt));
            return Source("fresh", fetchedAt, false, data, "tenant_wide");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Overview licenses source failed for workspace {WorkspaceId}", context.Membership.WorkspaceId);
            return LicenseFailure(key, "source_unavailable");
        }
    }

    private async Task<OverviewSource<OverviewActivityData>> ReadActivityAsync(
        WorkspaceContext context,
        CapabilityDecision decision,
        CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(decision, null, "workspace", context.Membership.PlatformRole);
        var key = CacheKey(context, "activity", fingerprint);
        var now = utcNow();
        if (cache.TryGet(key, now, cacheLifetime, out var cached))
        {
            var cachedData = (OverviewActivityData)cached.Data;
            return Source(cachedData.Items.Count == 0 ? "empty" : "fresh", cached.FetchedAt, false, cachedData, "workspace");
        }

        try
        {
            var items = await activityReader.ReadRecentAsync(context, cancellationToken);
            var fetchedAt = utcNow();
            var data = new OverviewActivityData(items);
            cache.Set(key, new OverviewCacheEntry(data, fetchedAt));
            return Source(items.Count == 0 ? "empty" : "fresh", fetchedAt, false, data, "workspace");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Overview activity source failed for workspace {WorkspaceId}", context.Membership.WorkspaceId);
            if (cache.TryGetAny(key, out var stale))
            {
                return Source("stale", stale.FetchedAt, true, (OverviewActivityData)stale.Data, "workspace", "source_unavailable");
            }

            return Unavailable<OverviewActivityData>("workspace", "source_unavailable");
        }
    }

    private OverviewSource<OverviewUserMetrics> UserFailure(CacheKey key, string category)
    {
        if (cache.TryGetAny(key, out var stale))
        {
            return Source("stale", stale.FetchedAt, true, (OverviewUserMetrics)stale.Data, "tenant_wide", category);
        }

        return Unavailable<OverviewUserMetrics>("tenant_wide", category);
    }

    private OverviewSource<OverviewLicenseCoverage> LicenseFailure(CacheKey key, string category)
    {
        if (cache.TryGetAny(key, out var stale))
        {
            return Source("stale", stale.FetchedAt, true, (OverviewLicenseCoverage)stale.Data, "tenant_wide", category);
        }

        return Unavailable<OverviewLicenseCoverage>("tenant_wide", category);
    }

    private static OverviewSource<T> Restricted<T>(CapabilityDecision decision, string scope, string? reason = null)
        where T : class =>
        new("restricted", null, false, null, scope, RestrictedReason: reason ?? decision.ReasonCode);

    private static OverviewSource<T> Blocked<T>(CapabilityDecision decision, bool moduleEnabled, string scope)
        where T : class
    {
        if (!moduleEnabled)
        {
            return new OverviewSource<T>("restricted", null, false, null, scope, RestrictedReason: "workspace_module_disabled");
        }

        return decision.State is CapabilityState.TemporarilyUnavailable or CapabilityState.ConsentRequired
            ? Unavailable<T>(scope, decision.ReasonCode)
            : Restricted<T>(decision, scope);
    }

    private static OverviewSource<T> Unavailable<T>(string scope, string category)
        where T : class =>
        new("unavailable", null, false, null, scope, ErrorCategory: category);

    private static OverviewSource<T> Source<T>(
        string state,
        DateTimeOffset fetchedAt,
        bool partialData,
        T data,
        string scope,
        string? errorCategory = null)
        where T : class =>
        new(state, fetchedAt, partialData, data, scope, ErrorCategory: errorCategory);

    private static bool IsAllowed(CapabilityDecision decision) =>
        string.Equals(decision.State, CapabilityState.Allowed, StringComparison.Ordinal);

    private static bool IsEnabled(IEnumerable<string> modules, string module) =>
        modules.Contains(module, StringComparer.OrdinalIgnoreCase);

    private static string? SelectUserReadScope(GraphAuthorizationSnapshot snapshot)
    {
        if (HasScope(snapshot, "Directory.Read.All")) return "Directory.Read.All";
        if (HasScope(snapshot, "User.Read.All")) return "User.Read.All";
        return null;
    }

    private static bool HasScope(GraphAuthorizationSnapshot snapshot, string scope) =>
        snapshot.ScopeAvailability is not null
            ? snapshot.ScopeAvailability.TryGetValue(scope, out var available) && available
            : snapshot.GrantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase);

    private static string Fingerprint(CapabilityDecision decision, string? userObjectId, string queryScope, string context) =>
        string.Join('|',
            decision.Capability,
            decision.State,
            decision.ReasonCode,
            decision.RequiredRoleTemplateId ?? string.Empty,
            decision.MissingScopes is null
                ? string.Empty
                : string.Join(',', decision.MissingScopes.OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase)),
            userObjectId ?? string.Empty,
            queryScope,
            context);

    private static CacheKey CacheKey(WorkspaceContext context, string source, string fingerprint) =>
        new(context.Membership.WorkspaceId, context.User.TenantId, context.User.ObjectId, source, fingerprint);
}

public sealed class OverviewDataCache
{
    private readonly System.Collections.Concurrent.ConcurrentDictionary<CacheKey, OverviewCacheEntry> entries = new();

    public bool TryGet(CacheKey key, DateTimeOffset now, TimeSpan lifetime, out OverviewCacheEntry entry)
    {
        if (entries.TryGetValue(key, out entry!) && now - entry.FetchedAt <= lifetime)
        {
            return true;
        }

        entry = default!;
        return false;
    }

    public bool TryGetAny(CacheKey key, out OverviewCacheEntry entry) => entries.TryGetValue(key, out entry!);

    public void Set(CacheKey key, OverviewCacheEntry entry) => entries[key] = entry;
}

public sealed record OverviewCacheEntry(object Data, DateTimeOffset FetchedAt);

public sealed record OverviewData(int TotalUsers, int AssignedUsers, int AvailableLicenses);

public readonly record struct CacheKey(
    Guid WorkspaceId,
    Guid TenantId,
    Guid RequesterObjectId,
    string Source,
    string AuthorizationFingerprint);

public sealed record OverviewResponse(
    IReadOnlyCollection<string> EffectiveModules,
    IReadOnlyList<CapabilityDecision> EffectiveCapabilities,
    OverviewSource<OverviewUserMetrics> Users,
    OverviewSource<OverviewLicenseCoverage> LicenseCoverage,
    OverviewSource<OverviewActivityData> Activity);
