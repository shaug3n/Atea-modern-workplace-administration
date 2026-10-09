using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Identity;

public sealed record AuthenticationMethodItem(
    string Id,
    string Type,
    string DisplayName,
    DateTimeOffset? CreatedDateTime,
    string? Model = null,
    string? AttestationLevel = null);

public static class AuthenticationMethodActions
{
    public const string Remove = "remove";
    public const string ResetMfa = "reset_mfa";

    public static bool CanRemove(string type) => !string.Equals(type, "passwordAuthenticationMethod", StringComparison.OrdinalIgnoreCase);
}

public sealed record AuthenticationMethodCommandResult(
    string Status,
    string RequiredCapability,
    string? Error = null,
    CapabilityDecision? Authorization = null,
    bool Replayed = false,
    string? AuditWarning = null,
    int RemovedCount = 0,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public sealed record TemporaryAccessPassCommandResult(
    string Status,
    string RequiredCapability,
    string? TemporaryAccessPass = null,
    string? Id = null,
    DateTimeOffset? StartDateTime = null,
    int? LifetimeInMinutes = null,
    bool? IsUsableOnce = null,
    string? Error = null,
    CapabilityDecision? Authorization = null,
    bool Replayed = false,
    string? AuditWarning = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public sealed record AuthenticationMethodsAccess(
    string State,
    string? ReasonCode = null,
    CapabilityDecision? Authorization = null);

public sealed record AuthenticationMethodsResponse(
    string UserObjectId,
    IReadOnlyList<AuthenticationMethodItem> Items,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    AuthenticationMethodsAccess Access,
    AuthenticationMethodsError? Error = null);

public sealed record AuthenticationMethodsError(
    string Category,
    string Message,
    string? State = null,
    int? StatusCode = null,
    int? RetryAfterSeconds = null);
