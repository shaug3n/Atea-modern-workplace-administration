using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.Extensions.Configuration;
using System.Text.Json.Serialization;

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
    string? UserType,
    string? GivenName = null,
    string? Surname = null,
    string? JobTitle = null,
    string? Department = null,
    string? OfficeLocation = null,
    string? MobilePhone = null,
    string? UsageLocation = null,
    bool IsReadOnly = false,
    string SourceOfAuthority = "cloud",
    string? SourceOfAuthorityReason = null,
    [property: JsonIgnore] Guid? DirectoryTenantId = null);

public sealed record AssignedLicense(
    string SkuId,
    string? SkuPartNumber,
    string? DisplayName = null);

public sealed record GroupMembership(
    string Id,
    string? DisplayName,
    string? MailNickname,
    bool? SecurityEnabled,
    IReadOnlyList<string> GroupTypes);

public sealed record DirectoryRoleAssignment(
    string Id,
    string RoleTemplateId,
    string? DisplayName,
    string AssignmentState,
    string? DirectoryScopeId);

public sealed record PimEligibility(
    string Id,
    string RoleTemplateId,
    string? DisplayName,
    string Status,
    string RequiredCapability,
    bool ActivationAvailable,
    bool RequiresApproval,
    bool RequiresMfa,
    bool RequiresJustification,
    int? MaximumDurationMinutes,
    PimActivationAction? ActivationAction)
{
    public string? RoleDefinitionId { get; init; }
    public string? DirectoryScopeId { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record PimActivationAction(
    string Action,
    string Href,
    string Method,
    string RequiredCapability,
    bool RequiresConfirmation);

public sealed record SectionAccessState(
    CapabilityDecision Authorization,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    UserDirectoryError? Error = null);

public sealed record UserDetailSection<T>(
    SectionAccessState Access,
    IReadOnlyList<T> Items);

public sealed record UserDetailResponse(
    SectionAccessState Access,
    UserDetails? User,
    UserDetailSection<AssignedLicense> Licenses,
    UserDetailSection<GroupMembership> Groups,
    UserDetailSection<DirectoryRoleAssignment> Roles,
    UserDetailSection<PimEligibility> Pim);

public sealed record UserDetailResult(
    string Status,
    UserDetailResponse? Detail = null,
    UserDirectoryError? Error = null);

public static class UserDetailStatus
{
    public const string Found = "found";
    public const string NotFound = "not_found";
    public const string NotAccessible = "not_accessible";
}

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
    int? RetryAfterSeconds = null,
    string? Field = null);

public static class UserDirectoryFreshness
{
    public const string Fresh = "fresh";
    public const string Stale = "stale";
    public const string Unavailable = "unavailable";
}

public sealed class UserSearchValidationException : Exception
{
    public UserSearchValidationException(string message, string category = "invalid_query", string? field = null) : base(message)
    {
        Category = category;
        Field = field;
    }

    public UserSearchValidationException(string message, Exception innerException) : base(message, innerException)
    {
        Category = "invalid_query";
    }

    public string Category { get; }
    public string? Field { get; }
}

public static class UserSearchFilterContract
{
    private static readonly IReadOnlySet<string> SupportedUserTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Member",
        "Guest"
    };

    public static UserSearchQuery Normalize(UserSearchRequest request)
    {
        if (request.PageSize is < 1 or > 100)
        {
            throw new UserSearchValidationException("pageSize must be between 1 and 100.", field: "pageSize");
        }

        var search = NormalizeSearch(request.Search);
        var accountStatus = NormalizeEnum(request.AccountStatus, "accountStatus", ["enabled", "disabled"]);
        var userType = NormalizeEnum(request.UserType, "userType", SupportedUserTypes);

        var tenantRole = NormalizeText(request.TenantRole, "tenantRole");
        var license = NormalizeText(request.License, "license");

        return new UserSearchQuery(
            search,
            request.PageSize,
            null,
            accountStatus,
            tenantRole,
            license,
            userType);
    }

    private static string? NormalizeSearch(string? value)
    {
        var normalized = Clean(value);
        if (normalized is not null && (normalized.Length > 200 || normalized.Any(char.IsControl)))
        {
            throw new UserSearchValidationException("search filter is invalid.", field: "search");
        }

        return normalized;
    }

    private static string? NormalizeText(string? value, string field)
    {
        var normalized = Clean(value);
        if (normalized is not null && (normalized.Length > 200 || normalized.Any(char.IsControl) || normalized.Contains('\'')))
        {
            throw new UserSearchValidationException($"{field} filter is invalid.", field: field);
        }

        return normalized;
    }

    private static string? NormalizeEnum(string? value, string field, IEnumerable<string> supportedValues)
    {
        var normalized = Clean(value);
        if (normalized is null)
        {
            return null;
        }

        var supported = supportedValues.FirstOrDefault(candidate => string.Equals(candidate, normalized, StringComparison.OrdinalIgnoreCase));
        if (supported is null)
        {
            throw new UserSearchValidationException($"{field} filter is not supported.", field: field);
        }

        return supported;
    }

    private static void RejectUnsupported(string? value, string field, string message)
    {
        if (Clean(value) is not null)
        {
            throw new UserSearchValidationException(message, "unsupported_filter", field);
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public static class UserContinuationConfiguration
{
    public static string? ResolveSigningKey(IConfiguration configuration, bool isDevelopment)
    {
        var signingKey = configuration["Users:ContinuationSigningKey"];
        if (!isDevelopment && string.IsNullOrWhiteSpace(signingKey))
        {
            throw new InvalidOperationException("Users:ContinuationSigningKey must be configured outside Development.");
        }

        return signingKey;
    }
}
