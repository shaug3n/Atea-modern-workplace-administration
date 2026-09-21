namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class WorkspaceSettings
{
    public Guid WorkspaceId { get; set; }
    public string DefaultTheme { get; set; } = "light";
    public string EnabledModulesJson { get; set; } = "[]";
    public string DefaultColumnsJson { get; set; } = "[]";
    public string DefaultFiltersJson { get; set; } = "{}";
    public string SupportInstructions { get; set; } = string.Empty;
    public Workspace Workspace { get; set; } = null!;
}
