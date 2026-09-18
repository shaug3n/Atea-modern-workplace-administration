namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class TenantConnection
{
    public Guid WorkspaceId { get; set; }
    public string Status { get; set; } = "awaiting_consent";
    public string ConsentScopesJson { get; set; } = "[]";
    public DateTimeOffset? LastVerifiedAt { get; set; }
    public string? LastFailureCategory { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
}
