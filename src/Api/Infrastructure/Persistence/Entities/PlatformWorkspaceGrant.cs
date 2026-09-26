namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class PlatformWorkspaceGrant
{
    public Guid OperatorTenantId { get; set; }
    public Guid OperatorObjectId { get; set; }
    public Guid WorkspaceId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
}
