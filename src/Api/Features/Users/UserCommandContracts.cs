using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public sealed record CreateUserCommand(
    string DisplayName,
    string GivenName,
    string Surname,
    string UserPrincipalName,
    string MailNickname,
    string? JobTitle,
    string? Department,
    string? OfficeLocation,
    string? MobilePhone,
    string UsageLocation,
    bool AccountEnabled,
    string? Reason = null);

public sealed record UpdateUserCommand(
    string? DisplayName,
    string? GivenName,
    string? Surname,
    string? JobTitle,
    string? Department,
    string? OfficeLocation,
    string? MobilePhone,
    string? UsageLocation,
    bool? AccountEnabled,
    string? Reason = null);

public sealed record SetAccountEnabledCommand(bool Enabled, string? Reason = null);

public sealed record GroupMembershipCommand(string GroupObjectId, string? Reason = null);

public sealed record LicenseAssignmentCommand(string SkuId, IReadOnlyList<string> DisabledPlans, string? Reason = null);

public sealed record UserWriteReasonCommand(string? Reason);

public sealed record ResetPasswordCommand(string? Reason);

public sealed record TemporaryCredentialNotice(
    string TemporaryPassword,
    bool ForceChangePasswordNextSignIn);

public sealed record UserCommandResult(
    string Status,
    string RequiredCapability,
    bool Replayed = false,
    string? Error = null,
    CapabilityDecision? Authorization = null,
    TemporaryCredentialNotice? TemporaryCredentialNotice = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null,
    string? AuditWarning = null);

public static class UserCommandStatus
{
    public const string Succeeded = "succeeded";
    public const string Denied = "denied";
    public const string SourceOfAuthorityReadOnly = "source_of_authority_read_only";
    public const string Conflict = "conflict";
    public const string IdempotencyKeyReused = "idempotency_key_reused";
    public const string NotFound = "not_found";
    public const string InvalidTarget = "invalid_target";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}
