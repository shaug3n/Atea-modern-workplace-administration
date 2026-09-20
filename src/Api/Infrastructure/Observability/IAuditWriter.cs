namespace Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

public interface IAuditWriter
{
    Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken);
}

public sealed record AuditEvent(
    Guid WorkspaceId,
    Guid ActorObjectId,
    string Action,
    string TargetResourceId,
    string Result,
    DateTimeOffset Timestamp,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null,
    string? FailureCategory = null);

public sealed class NoOpAuditWriter : IAuditWriter
{
    public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) => Task.CompletedTask;
}
