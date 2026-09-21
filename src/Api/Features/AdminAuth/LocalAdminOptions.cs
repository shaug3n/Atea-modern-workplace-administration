namespace Atea.UnifiedWorkplace.Api.Features.AdminAuth;

public sealed class LocalAdminOptions
{
    public bool Enabled { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string ObjectId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool AllowAllWorkspaces { get; set; }
}
