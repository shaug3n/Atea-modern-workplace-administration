using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Groups;

public sealed record GroupCatalogItem(string Id, string? DisplayName, string? MailNickname, bool? SecurityEnabled, IReadOnlyList<string> GroupTypes);
public sealed record GroupCatalogResponse(IReadOnlyList<GroupCatalogItem> Items, string? Search, int PageSize, DateTimeOffset FetchedAt, CapabilityDecision Access);

public sealed class GroupCatalogValidationException(string message, string? field = null) : Exception(message)
{
    public string? Field { get; } = field;
}

public static class GroupCatalogQueryContract
{
    public static (string? Search, int PageSize) Normalize(string? search, string? pageSizeValue)
    {
        var pageSize = string.IsNullOrWhiteSpace(pageSizeValue) ? 25 : int.TryParse(pageSizeValue, out var parsed) ? parsed : throw new GroupCatalogValidationException("pageSize must be an integer between 1 and 100.", "pageSize");
        if (pageSize is < 1 or > 100) throw new GroupCatalogValidationException("pageSize must be between 1 and 100.", "pageSize");
        var normalizedSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        if (normalizedSearch is not null && (normalizedSearch.Length > 200 || normalizedSearch.Any(char.IsControl))) throw new GroupCatalogValidationException("search filter is invalid.", "search");
        return (normalizedSearch, pageSize);
    }
}
