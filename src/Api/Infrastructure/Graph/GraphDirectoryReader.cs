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

    Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
}

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string> CorrelationIds,
    string? ContinuationLink = null,
    GraphOperationResult? Error = null);

public sealed class GraphDirectoryReader(IDelegatedGraphClientFactory clientFactory) : IUserDirectoryReader
{
    private const string UserSelect = "id,displayName,userPrincipalName,mail,accountEnabled,userType,givenName,surname,jobTitle,department,officeLocation,mobilePhone,usageLocation,onPremisesSyncEnabled,creationType";

    public async Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(
            string.IsNullOrWhiteSpace(query.License) ? GraphScopeCatalog.DirectoryReadScopes : ["Directory.Read.All"], cancellationToken);
        var path = BuildSearchPath(query);
        var users = new List<UserSummary>();
        var correlations = new List<string>();

        if (string.IsNullOrWhiteSpace(query.ContinuationPath) && !string.IsNullOrWhiteSpace(query.License))
        {
            var license = await ResolveLicenseSkuAsync(lease.Transport, query.License, cancellationToken);
            if (license.Error is not null)
            {
                return new PagedResult<UserSummary>(users, correlations, null, license.Error);
            }

            path = BuildSearchPath(query, license.SkuId);
        }

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

    public async Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
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
        return MapUserDetails(document.RootElement) with { DirectoryTenantId = context.User.TenantId };
    }

    private static string BuildSearchPath(UserSearchQuery query, string? resolvedLicenseSkuId = null)
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

        var filters = BuildFilters(query, resolvedLicenseSkuId);
        if (filters.Count > 0)
        {
            parameters.Add($"$filter={Uri.EscapeDataString(string.Join(" and ", filters))}");
        }

        return $"/v1.0/users?{string.Join("&", parameters)}";
    }

    private static List<string> BuildFilters(UserSearchQuery query, string? resolvedLicenseSkuId)
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

        if (!string.IsNullOrWhiteSpace(resolvedLicenseSkuId))
        {
            filters.Add($"assignedLicenses/any(a:a/skuId eq {resolvedLicenseSkuId})");
        }

        return filters;
    }

    private static IReadOnlyDictionary<string, string>? HeadersFor(UserSearchQuery query) =>
        string.IsNullOrWhiteSpace(query.Search)
            ? null
            : new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

    private static string EscapeODataString(string value) => value.Trim().Replace("'", "''", StringComparison.Ordinal);

    private static async Task<(string? SkuId, GraphOperationResult? Error)> ResolveLicenseSkuAsync(
        IGraphTransport transport,
        string licenseInput,
        CancellationToken cancellationToken)
    {
        try
        {
            var normalized = licenseInput.Trim();
            string? path = "/v1.0/subscribedSkus?$select=skuId,skuPartNumber";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (path is not null)
            {
                if (!seen.Add(path)) return (null, new GraphOperationResult(false, "invalid_response"));
                var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
                if (!response.Result.IsSuccess) return (null, response.Result);
                using var document = JsonDocument.Parse(response.Content);
                if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                    return (null, new GraphOperationResult(false, "invalid_response"));
                var match = value.EnumerateArray().FirstOrDefault(item =>
                    string.Equals(OptionalString(item, "skuId"), normalized, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(OptionalString(item, "skuPartNumber"), normalized, StringComparison.OrdinalIgnoreCase));
                if (match.ValueKind != JsonValueKind.Undefined && OptionalString(match, "skuId") is { } skuId)
                    return (skuId, null);
                path = GraphLicenseOverviewReader.ContinuationPath(document.RootElement);
            }
            return (null, new GraphOperationResult(false, "invalid_license_filter", 400));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return (null, new GraphOperationResult(false, "invalid_response"));
        }
    }

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

    private static UserDetails MapUserDetails(JsonElement element)
    {
        var syncEnabled = OptionalBool(element, "onPremisesSyncEnabled") == true;
        var creationType = OptionalString(element, "creationType");
        var source = syncEnabled ? "on_premises_sync" : string.Equals(creationType, "Invitation", StringComparison.OrdinalIgnoreCase) ? "external" : "cloud";
        var reason = source switch
        {
            "on_premises_sync" => "This user is synchronized from an on-premises directory and must be edited at the source.",
            "external" => "This user is externally managed and cannot be edited here.",
            _ => null
        };

        return new UserDetails(
            RequiredString(element, "id"),
            OptionalString(element, "displayName"),
            OptionalString(element, "userPrincipalName"),
            OptionalString(element, "mail"),
            OptionalBool(element, "accountEnabled"),
            OptionalString(element, "userType"),
            OptionalString(element, "givenName"),
            OptionalString(element, "surname"),
            OptionalString(element, "jobTitle"),
            OptionalString(element, "department"),
            OptionalString(element, "officeLocation"),
            OptionalString(element, "mobilePhone"),
            OptionalString(element, "usageLocation"),
            source != "cloud",
            source,
            reason);
    }

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
