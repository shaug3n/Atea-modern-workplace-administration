using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

public sealed class AuditWriter(
    WorkplaceDbContext db,
    ILogger<AuditWriter> logger,
    ICorrelationContextAccessor? correlationContext = null) : IAuditWriter
{
    public async Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
    {
        try
        {
            auditEvent.Id = auditEvent.Id == Guid.Empty ? Guid.NewGuid() : auditEvent.Id;
            auditEvent.CorrelationId = string.IsNullOrWhiteSpace(auditEvent.CorrelationId)
                ? correlationContext?.Current?.CorrelationId
                : CorrelationMiddleware.SafeCorrelationId(auditEvent.CorrelationId);
            auditEvent.SafeMetadataJson = RedactingLogEnricher.Redact(auditEvent.SafeMetadataJson);
            db.AuditEvents.Add(auditEvent);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            db.Entry(auditEvent).State = EntityState.Detached;
            throw;
        }
        catch (Exception exception)
        {
            db.Entry(auditEvent).State = EntityState.Detached;
            logger.LogError(
                exception,
                "Audit write failed for action {Action}, outcome {Outcome}, workspace {WorkspaceHash}, correlation {CorrelationId}",
                auditEvent.Action,
                auditEvent.Outcome,
                StableHash(auditEvent.WorkspaceId),
                auditEvent.CorrelationId ?? correlationContext?.Current?.CorrelationId);
            throw;
        }
    }

    private static string StableHash(Guid value) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(value.ToByteArray()))[..16].ToLowerInvariant();
}
