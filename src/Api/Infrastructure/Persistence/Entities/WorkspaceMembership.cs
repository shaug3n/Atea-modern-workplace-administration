namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class WorkspaceMembership
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public Guid TenantObjectId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string PlatformRole { get; set; } = string.Empty;
    public bool IsAteaOperator { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
}
