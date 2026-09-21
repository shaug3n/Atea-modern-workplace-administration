namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record CreateWorkspaceRequest(Guid TenantId, string DisplayName);
public sealed record AddWorkspaceMembershipRequest(Guid TenantObjectId, string Email, string PlatformRole, bool IsAteaOperator);
public sealed record WorkspaceDto(Guid Id, Guid TenantId, string DisplayName, string ConnectionStatus);
public sealed record InvitationRequest(string Email, string DisplayName, DateTimeOffset ExpiresAt, Guid? ApprovedTenantObjectId = null);
public sealed record ConsentStartResponse(string AuthorizationUrl, IReadOnlyCollection<string> Scopes, string Challenge, string CorrelationId);
public sealed record ConnectionHealthDto(Guid WorkspaceId, string Status, DateTimeOffset? LastVerifiedAt, IReadOnlyCollection<string> Scopes, string? Problem, string CorrelationId);
public sealed record WorkspaceMembershipDto(Guid Id, Guid TenantObjectId, string Email, string PlatformRole, bool IsAteaOperator);
public sealed record InvitationSummaryDto(Guid Id, string Email, string DisplayName, DateTimeOffset ExpiresAt, DateTimeOffset? RedeemedAt);
public sealed record WorkspaceAdminDetailDto(Guid Id, Guid TenantId, string DisplayName, string ConnectionStatus, DateTimeOffset? LastVerifiedAt, string? ConnectionFailureCategory, IReadOnlyCollection<WorkspaceMembershipDto> Memberships, IReadOnlyCollection<InvitationSummaryDto> Invitations);
