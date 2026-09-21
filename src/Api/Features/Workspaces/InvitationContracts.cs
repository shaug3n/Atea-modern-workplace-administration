namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record InvitationRedemptionResponse(string Status, Guid WorkspaceId, string WorkspaceName, string NextStep);
