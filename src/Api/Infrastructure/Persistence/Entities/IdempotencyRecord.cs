namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid ActorObjectId { get; set; }
    public string Operation { get; set; } = string.Empty;
    public string TargetId { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResultCategory { get; set; } = string.Empty;
    public string SafeResultJson { get; set; } = "{}";
    public string? GraphCorrelationId { get; set; }
    public string? GraphRequestId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
