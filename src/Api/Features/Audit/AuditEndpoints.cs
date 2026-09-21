using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Features.Audit;

public static class AuditEndpoints
{
    private const int DefaultPageSize = 25;
    private const int MaxPageSize = 100;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/audit/events", ReadAuditEventsAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> ReadAuditEventsAsync(
        IWorkspaceContextAccessor accessor,
        WorkplaceDbContext db,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var pageSize = PageSize(request);
        var query = db.AuditEvents.AsNoTracking()
            .Where(audit => audit.WorkspaceId == context.Membership.WorkspaceId);

        if (Query(request, "actorObjectId") is { } actor && Guid.TryParse(actor, out var actorObjectId))
        {
            query = query.Where(audit => audit.ActorObjectId == actorObjectId);
        }

        if (Query(request, "action") is { } action)
        {
            query = query.Where(audit => audit.Action == action);
        }

        if (Query(request, "outcome") is { } outcome)
        {
            query = query.Where(audit => audit.Outcome == outcome);
        }

        var items = await query
            .OrderByDescending(audit => audit.Timestamp)
            .ThenByDescending(audit => audit.Id)
            .Take(pageSize)
            .Select(audit => AuditEventDto.From(audit))
            .ToArrayAsync(cancellationToken);

        return Results.Ok(new AuditEventsResponse(
            items,
            DateTimeOffset.UtcNow,
            items.Length == 0 ? "stale" : "fresh",
            false,
            "Microsoft 365 audit logs remain authoritative for directory changes. This stream records platform actor intent, result and correlation references."));
    }

    private static string? Query(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value.ToString())
            ? value.ToString().Trim()
            : null;

    private static int PageSize(HttpRequest request)
    {
        if (!request.Query.TryGetValue("pageSize", out var value) || !int.TryParse(value.ToString(), out var pageSize))
        {
            return DefaultPageSize;
        }

        return Math.Clamp(pageSize, 1, MaxPageSize);
    }
}

public sealed record AuditEventsResponse(
    IReadOnlyList<AuditEventDto> Items,
    DateTimeOffset FetchedAt,
    string Freshness,
    bool PartialData,
    string AuthoritativeSourceNotice);

public sealed record AuditEventDto(
    Guid Id,
    Guid WorkspaceId,
    Guid TenantId,
    Guid ActorTenantId,
    Guid ActorObjectId,
    string Action,
    string TargetType,
    string TargetId,
    string Outcome,
    DateTimeOffset Timestamp,
    string? CorrelationId,
    string? GraphCorrelationId,
    string? GraphRequestId,
    string? PimRequestId,
    string? FailureCategory,
    string SafeMetadataJson)
{
    public static AuditEventDto From(AuditEvent audit) => new(
        audit.Id,
        audit.WorkspaceId,
        audit.TenantId,
        audit.ActorTenantId,
        audit.ActorObjectId,
        audit.Action,
        audit.TargetType,
        audit.TargetId,
        audit.Outcome,
        audit.Timestamp,
        audit.CorrelationId,
        audit.GraphCorrelationId,
        audit.GraphRequestId,
        audit.PimRequestId,
        audit.FailureCategory,
        RedactingLogEnricher.Redact(audit.SafeMetadataJson));
}
