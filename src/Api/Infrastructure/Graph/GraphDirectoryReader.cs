using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IUserDirectoryReader
{
    Task<PagedResult<UserSummary>> SearchAsync(
        WorkspaceContext context,
        UserSearchQuery query,
        CancellationToken cancellationToken);

    Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken);
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string> CorrelationIds,
    string? ContinuationLink = null,
    GraphOperationResult? Error = null);

public sealed class GraphDirectoryReader(IDelegatedGraphClientFactory clientFactory) : IUserDirectoryReader
{
    private const string UserSelect = "id,displayName,userPrincipalName,mail,accountEnabled,userType";

    public async Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var path = BuildSearchPath(query);
        var users = new List<UserSummary>();
        var correlations = new List<string>();

        var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path, Headers: HeadersFor(query)), cancellationToken);
        AddCorrelation(response, correlations);
        if (!response.Result.IsSuccess)
        {
            return new PagedResult<UserSummary>(users, correlations, null, response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        if (document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
        {
            users.AddRange(value.EnumerateArray().Select(MapUserSummary));
        }

        var continuationLink = document.RootElement.TryGetProperty("@odata.nextLink", out var next)
            ? NormalizeGraphPath(next.GetString())
            : null;

        return new PagedResult<UserSummary>(users, correlations, continuationLink);
    }

    public async Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userObjectId);

        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}?$select={UserSelect}"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return response.Result.Category == "not_found" ? null : throw new GraphAdapterException(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var user = MapUserSummary(document.RootElement);
        return new UserDetails(user.Id, user.DisplayName, user.UserPrincipalName, user.Mail, user.AccountEnabled, user.UserType);
    }

    private static string BuildSearchPath(UserSearchQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.ContinuationPath))
        {
            return query.ContinuationPath;
        }

        var top = query.PageSize is <= 0 or > 100 ? 25 : query.PageSize;
        var parameters = new List<string>
        {
            $"$select={UserSelect}",
            $"$top={top}"
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            parameters.Add($"$search={Uri.EscapeDataString($"\"displayName:{query.Search.Trim()}\"")}");
        }

        var filters = BuildFilters(query);
        if (filters.Count > 0)
        {
            parameters.Add($"$filter={Uri.EscapeDataString(string.Join(" and ", filters))}");
        }

        return $"/v1.0/users?{string.Join("&", parameters)}";
    }

    private static List<string> BuildFilters(UserSearchQuery query)
    {
        var filters = new List<string>();
        if (string.Equals(query.AccountStatus, "enabled", StringComparison.OrdinalIgnoreCase))
        {
            filters.Add("accountEnabled eq true");
        }
        else if (string.Equals(query.AccountStatus, "disabled", StringComparison.OrdinalIgnoreCase))
        {
            filters.Add("accountEnabled eq false");
        }

        if (!string.IsNullOrWhiteSpace(query.UserType))
        {
            filters.Add($"userType eq '{EscapeODataString(query.UserType)}'");
        }

        if (!string.IsNullOrWhiteSpace(query.License))
        {
            filters.Add($"assignedLicenses/any(l:l/skuId eq '{EscapeODataString(query.License)}')");
        }

        if (!string.IsNullOrWhiteSpace(query.TenantRole))
        {
            filters.Add($"appRoleAssignments/any(r:r/displayName eq '{EscapeODataString(query.TenantRole)}')");
        }

        return filters;
    }

    private static IReadOnlyDictionary<string, string>? HeadersFor(UserSearchQuery query) =>
        string.IsNullOrWhiteSpace(query.Search) && string.IsNullOrWhiteSpace(query.TenantRole) && string.IsNullOrWhiteSpace(query.License)
            ? null
            : new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

    private static string EscapeODataString(string value) => value.Trim().Replace("'", "''", StringComparison.Ordinal);

    private static string? NormalizeGraphPath(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        return Uri.TryCreate(nextLink, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.PathAndQuery
            : nextLink;
    }

    private static UserSummary MapUserSummary(JsonElement element) => new(
        RequiredString(element, "id"),
        OptionalString(element, "displayName"),
        OptionalString(element, "userPrincipalName"),
        OptionalString(element, "mail"),
        OptionalBool(element, "accountEnabled"),
        OptionalString(element, "userType"));

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static bool? OptionalBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static void AddCorrelation(GraphTransportResponse response, List<string> correlations)
    {
        if (!string.IsNullOrWhiteSpace(response.Result.CorrelationId))
        {
            correlations.Add(response.Result.CorrelationId);
        }
    }
}

public sealed class GraphAdapterException(GraphOperationResult result) : Exception($"Graph request failed with category '{result.Category}'.")
{
    public GraphOperationResult Result { get; } = result;
}
