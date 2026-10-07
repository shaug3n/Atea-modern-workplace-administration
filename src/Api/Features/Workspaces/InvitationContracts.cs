namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record InvitationRedemptionResponse(string Status, Guid WorkspaceId, string WorkspaceName, string NextStep);

public sealed record InvitationConsentStartRequest;
public sealed record InvitationConsentResumeRequest(string State, Guid? Tenant = null, string? ErrorCode = null);
public sealed record InvitationConsentStartResponse(
    string AuthorizationUrl,
    IReadOnlyCollection<string> Scopes,
    string Challenge,
    string CorrelationId,
    DateTimeOffset ExpiresAt);
public sealed record InvitationConsentResumeResponse(
    bool Valid,
    string Status,
    Guid? TenantId,
    string CorrelationId);
