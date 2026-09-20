namespace Atea.UnifiedWorkplace.Api.Features.Users;

public sealed record UserSearchRequest(
    string? Search = null,
    int PageSize = 25,
    string? ContinuationToken = null,
    string? AccountStatus = null,
    string? TenantRole = null,
    string? License = null,
    string? UserType = null);

public sealed record UserSearchQuery(
    string? Search = null,
    int PageSize = 25,
    string? ContinuationPath = null,
    string? AccountStatus = null,
    string? TenantRole = null,
    string? License = null,
    string? UserType = null);

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

public sealed record UserDirectoryResponse(
    IReadOnlyList<UserSummary> Items,
    string? ContinuationToken,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    UserDirectoryError? Error = null);

public sealed record UserDirectoryError(
    string Category,
    string Message,
    string? State = null,
    int? StatusCode = null,
    int? RetryAfterSeconds = null);

public static class UserDirectoryFreshness
{
    public const string Fresh = "fresh";
    public const string Stale = "stale";
    public const string Unavailable = "unavailable";
}

public sealed class UserSearchValidationException : Exception
{
    public UserSearchValidationException(string message) : base(message)
    {
    }

    public UserSearchValidationException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
