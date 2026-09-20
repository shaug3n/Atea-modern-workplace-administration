namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class UserPreference
{
    public Guid TenantId { get; set; }
    public Guid UserObjectId { get; set; }
    public string Theme { get; set; } = "light";
    public DateTimeOffset UpdatedAt { get; set; }
}
