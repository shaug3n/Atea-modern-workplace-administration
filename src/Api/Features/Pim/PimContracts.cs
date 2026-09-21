using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Features.Pim;

public static class PimStatus
{
    public const string Active = "active";
    public const string EligibleInactive = "eligible_inactive";
    public const string NotEligible = "not_eligible";
    public const string ActivationPending = "activation_pending";
    public const string ApprovalRequired = "approval_required";
    public const string MfaRequired = "mfa_required";
    public const string NotAuthorized = "not_authorized";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
    public const string PolicyBlocked = "policy_blocked";
}

public static class PimRoleType
{
    public const string DirectoryRole = "directoryRole";
}

public sealed record PimRoleStatus(
    string RoleTemplateId,
    string? RoleDefinitionId,
    string? DisplayName,
    string Status,
    DateTimeOffset? ExpiresAt = null,
    string? RequestId = null,
    string RequiredCapability = Capability.PimActivate,
    bool ActivationAvailable = false,
    PimGuidedHandoff? Handoff = null);

public sealed record PimGuidedHandoff(
    string NextStep,
    string RoleTemplateId,
    string? RoleDisplayName,
    string? PortalUrl,
    string RefreshAction = "/api/capabilities",
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public sealed record PimUserResponse(
    string UserObjectId,
    string Status,
    IReadOnlyList<PimRoleStatus> Roles,
    PimGuidedHandoff? Handoff = null,
    string? Error = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null)
{
    public SectionAccessState? Access { get; init; }
    public IReadOnlyList<PimEligibility> Items { get; init; } = [];
}

public sealed record PimUserResult(
    string Outcome,
    PimUserResponse? Response = null,
    UserDirectoryError? Error = null);

public static class PimUserOutcome
{
    public const string Found = "found";
    public const string NotFound = "not_found";
}

public sealed record PimActivationRequest(
    string RoleTemplateId,
    int DurationMinutes,
    string? Justification,
    bool Confirmed,
    string RoleType = PimRoleType.DirectoryRole);

public sealed record PimActivationResult(
    string Status,
    string RequiredCapability,
    bool Replayed = false,
    string? RoleTemplateId = null,
    string? RoleDefinitionId = null,
    string? DisplayName = null,
    string? RequestId = null,
    string? Error = null,
    PimGuidedHandoff? Handoff = null,
    CapabilityDecision? Authorization = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);
