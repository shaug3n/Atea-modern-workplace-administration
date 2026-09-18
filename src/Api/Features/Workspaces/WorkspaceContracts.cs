namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record CreateWorkspaceRequest(Guid TenantId, string DisplayName);
public sealed record AddWorkspaceMembershipRequest(Guid TenantObjectId, string Email, string PlatformRole, bool IsAteaOperator);
public sealed record WorkspaceDto(Guid Id, Guid TenantId, string DisplayName, string ConnectionStatus);
