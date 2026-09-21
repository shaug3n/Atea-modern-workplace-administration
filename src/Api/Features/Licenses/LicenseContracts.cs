using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses;

public sealed record LicenseOverviewRequest(
    string? Search = null,
    int PageSize = 25,
    int Page = 1,
    string? Filter = null);

public sealed record LicenseOverviewQuery(
    string? Search = null,
    int PageSize = 25,
    int Page = 1);

public sealed record LicenseOverviewItem(
    string SkuId,
    string PartNumber,
    string DisplayName,
    int Assigned,
    int Available);

public sealed record LicenseOverviewAccess(
    string State,
    string? ReasonCode = null,
    CapabilityDecision? Authorization = null);

public sealed record LicenseOverviewError(
    string Category,
    string Message,
    string? State = null,
    int? StatusCode = null,
    int? RetryAfterSeconds = null,
    string? Field = null);

public sealed record LicenseOverviewResponse(
    IReadOnlyList<LicenseOverviewItem> Items,
    int Total,
    int Page,
    int PageSize,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    LicenseOverviewAccess Access,
    LicenseOverviewError? Error = null);

public static class LicenseOverviewFreshness
{
    public const string Live = "live";
    public const string Stale = "stale";
    public const string Unavailable = "unavailable";
}

public sealed class LicenseOverviewValidationException : Exception
{
    public LicenseOverviewValidationException(string message, string category = "invalid_query", string? field = null) : base(message)
    {
        Category = category;
        Field = field;
    }

    public string Category { get; }
    public string? Field { get; }
}

public static class LicenseOverviewFilterContract
{
    public static LicenseOverviewQuery Normalize(LicenseOverviewRequest request)
    {
        if (request.PageSize is < 1 or > 100)
        {
            throw new LicenseOverviewValidationException("pageSize must be between 1 and 100.", field: "pageSize");
        }

        if (request.Page < 1)
        {
            throw new LicenseOverviewValidationException("page must be greater than or equal to 1.", field: "page");
        }

        if (!string.IsNullOrWhiteSpace(request.Filter))
        {
            throw new LicenseOverviewValidationException(
                "Raw license filters are not supported.",
                "unsupported_filter",
                "filter");
        }

        var search = string.IsNullOrWhiteSpace(request.Search) ? null : request.Search.Trim();
        if (search is not null && (search.Length > 200 || search.Any(char.IsControl)))
        {
            throw new LicenseOverviewValidationException("search filter is invalid.", field: "search");
        }

        return new LicenseOverviewQuery(search, request.PageSize, request.Page);
    }
}
