namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class AuditEvent
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid TenantId { get; set; }
    public Guid ActorTenantId { get; set; }
    public Guid ActorObjectId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string TargetType { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public string? CorrelationId { get; set; }
    public string? GraphCorrelationId { get; set; }
    public string? GraphRequestId { get; set; }
    public string? PimRequestId { get; set; }
    public string? FailureCategory { get; set; }
    public string SafeMetadataJson { get; set; } = "{}";
}
