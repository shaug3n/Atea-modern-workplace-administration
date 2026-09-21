using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Groups;

public sealed record GroupCatalogItem(string Id, string? DisplayName, string? MailNickname, bool? SecurityEnabled, IReadOnlyList<string> GroupTypes);
public sealed record GroupCatalogResponse(IReadOnlyList<GroupCatalogItem> Items, string? Search, int PageSize, DateTimeOffset FetchedAt, CapabilityDecision Access);
