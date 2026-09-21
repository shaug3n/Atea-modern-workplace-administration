using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses;

public interface ILicenseOverviewService
{
    Task<LicenseOverviewResponse> GetAsync(
        WorkspaceContext context,
        LicenseOverviewRequest request,
        CancellationToken cancellationToken);
}

public sealed class LicenseOverviewService(
    ILicenseOverviewReader reader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    Func<DateTimeOffset>? utcNow = null) : ILicenseOverviewService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<LicenseOverviewResponse> GetAsync(
        WorkspaceContext context,
        LicenseOverviewRequest request,
        CancellationToken cancellationToken)
    {
        var query = LicenseOverviewFilterContract.Normalize(request);
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.LicensesAssign];

        if (authorization.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
        {
            return Response(
                query,
                authorization,
                [],
                0,
                LicenseOverviewFreshness.Unavailable,
                true,
                new LicenseOverviewError(
                    "capability_required",
                    "License data cannot be read for the current capability state.",
                    authorization.State));
        }

        var result = await reader.ReadAsync(context, query, cancellationToken);
        if (result.Error is not null)
        {
            return Response(
                query,
                authorization,
                [],
                0,
                FreshnessFor(result.Error.Category),
                true,
                new LicenseOverviewError(
                    result.Error.Category,
                    MessageFor(result.Error.Category),
                    authorization.State,
                    result.Error.StatusCode,
                    result.Error.RetryAfter is null ? null : (int)Math.Ceiling(result.Error.RetryAfter.Value.TotalSeconds)));
        }

        var filtered = result.Value
            .Where(item => Matches(item, query.Search))
            .ToArray();
        var page = filtered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToArray();

        return Response(query, authorization, page, filtered.Length, LicenseOverviewFreshness.Live, false);
    }

    private LicenseOverviewResponse Response(
        LicenseOverviewQuery query,
        CapabilityDecision authorization,
        IReadOnlyList<LicenseOverviewItem> items,
        int total,
        string freshness,
        bool partialData,
        LicenseOverviewError? error = null) =>
        new(
            items,
            total,
            query.Page,
            query.PageSize,
            utcNow(),
            freshness,
            partialData,
            new LicenseOverviewAccess(authorization.State, authorization.ReasonCode, authorization),
            error);

    private static bool Matches(LicenseOverviewItem item, string? search) =>
        string.IsNullOrWhiteSpace(search)
        || item.SkuId.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.PartNumber.Contains(search, StringComparison.OrdinalIgnoreCase)
        || item.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase);

    private static string FreshnessFor(string category) => category switch
    {
        "throttled" => LicenseOverviewFreshness.Stale,
        _ => LicenseOverviewFreshness.Unavailable
    };

    private static string MessageFor(string category) => category switch
    {
        "throttled" => "Microsoft Graph throttled the license overview request.",
        "not_authorized" => "The signed-in user is not authorized to read license data.",
        "consent_required" => "Delegated Microsoft Graph consent is required.",
        _ => "License data is temporarily unavailable."
    };
}
