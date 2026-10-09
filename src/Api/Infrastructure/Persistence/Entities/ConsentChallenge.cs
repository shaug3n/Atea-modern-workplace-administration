namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class ConsentChallenge
{
    public string StateHash { get; set; } = string.Empty;
    public Guid WorkspaceId { get; set; }
    public Guid? InvitationId { get; set; }
    public Guid TenantId { get; set; }
    public string Purpose { get; set; } = "workspace";
    public string CorrelationId { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? ConsumedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
    public PlatformInvitation? Invitation { get; set; }
}
