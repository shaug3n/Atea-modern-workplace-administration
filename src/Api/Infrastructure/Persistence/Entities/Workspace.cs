namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class Workspace
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string ConnectionStatus { get; set; } = "awaiting_invitation";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public ICollection<WorkspaceMembership> Memberships { get; set; } = new List<WorkspaceMembership>();
    public TenantConnection? TenantConnection { get; set; }
    public WorkspaceSettings? Settings { get; set; }
}
