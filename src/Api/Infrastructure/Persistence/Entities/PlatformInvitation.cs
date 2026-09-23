namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class PlatformInvitation
{
    public Guid Id { get; set; }
    public Guid WorkspaceId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public Guid? ApprovedTenantObjectId { get; set; }
    public string Role { get; set; } = "customer_admin";
    public string NonceHash { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public Workspace Workspace { get; set; } = null!;
}
