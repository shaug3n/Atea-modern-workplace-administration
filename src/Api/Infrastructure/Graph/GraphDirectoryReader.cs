using System.Text.Json;
namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IUserDirectoryReader
{
    Task<PagedResult<UserSummary>> SearchAsync(
        UserSearchQuery query,
        CancellationToken cancellationToken);

    Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken);
}

public sealed record UserSearchQuery(string? Search, int Top = 25);

public sealed record PagedResult<T>(
    IReadOnlyList<T> Items,
    IReadOnlyList<string> CorrelationIds,
    GraphOperationResult? Error = null);

public sealed record UserSummary(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    string? Mail,
    bool? AccountEnabled = null,
    string? UserType = null);

public sealed record UserDetails(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    string? Mail,
    bool? AccountEnabled,
    string? UserType);

public sealed class GraphDirectoryReader(IDelegatedGraphClientFactory clientFactory) : IUserDirectoryReader
{
    private const string UserSelect = "id,displayName,userPrincipalName,mail,accountEnabled,userType";

    public async Task<PagedResult<UserSummary>> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var path = BuildSearchPath(query);
        var users = new List<UserSummary>();
        var correlations = new List<string>();

        while (!string.IsNullOrWhiteSpace(path))
        {
            var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
            AddCorrelation(response, correlations);
            if (!response.Result.IsSuccess)
            {
                return new PagedResult<UserSummary>(users, correlations, response.Result);
            }

            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array)
            {
                users.AddRange(value.EnumerateArray().Select(MapUserSummary));
            }

            path = document.RootElement.TryGetProperty("@odata.nextLink", out var next)
                ? NormalizeGraphPath(next.GetString())
                : null;
        }

        return new PagedResult<UserSummary>(users, correlations);
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
        var top = query.Top is <= 0 or > 999 ? 25 : query.Top;
        var path = $"/v1.0/users?$select={UserSelect}&$top={top}";
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            path += $"&$search={Uri.EscapeDataString($"\"displayName:{query.Search.Trim()}\"")}";
        }

        return path;
    }

    private static string? NormalizeGraphPath(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        return Uri.TryCreate(nextLink, UriKind.Absolute, out var uri)
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
