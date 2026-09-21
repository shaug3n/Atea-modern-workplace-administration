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
        endpoints.MapGet("/api/audit/events", ReadAuditEventsAsync).RequireAuthorization().RequireCapability(Capability.AuditView);
        return endpoints;
    }

    private static async Task<IResult> ReadAuditEventsAsync(
        IWorkspaceContextAccessor accessor,
        WorkplaceDbContext db,
        AuditContinuationTokenProtector continuationTokens,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var pageSize = PageSize(request);
        var actorFilter = Query(request, "actorObjectId");
        Guid? actorObjectId = null;
        if (actorFilter is not null)
        {
            if (!Guid.TryParse(actorFilter, out var parsedActorObjectId))
            {
                return Results.BadRequest(new { error = "invalid_actor_filter" });
            }

            actorObjectId = parsedActorObjectId;
        }

        var actionFilter = Query(request, "action");
        var outcomeFilter = Query(request, "outcome");
        AuditContinuationCursor? cursor = null;
        if (Query(request, "continuationToken") is { } token)
        {
            if (!continuationTokens.TryUnprotect(token, out cursor)
                || cursor is null
                || cursor.WorkspaceId != context.Membership.WorkspaceId
                || cursor.RequesterObjectId != context.User.ObjectId
                || cursor.ActorObjectId != actorObjectId
                || !string.Equals(cursor.Action, actionFilter, StringComparison.Ordinal)
                || !string.Equals(cursor.Outcome, outcomeFilter, StringComparison.Ordinal)
                || cursor.PageSize != pageSize)
            {
                return Results.BadRequest(new { error = "invalid_continuation_token" });
            }
        }

        var query = db.AuditEvents.AsNoTracking()
            .Where(audit => audit.WorkspaceId == context.Membership.WorkspaceId
                && audit.TenantId == context.User.TenantId
                && audit.ActorTenantId == context.User.TenantId);

        if (actorObjectId is { } actor)
        {
            query = query.Where(audit => audit.ActorObjectId == actor);
        }

        if (actionFilter is { } action)
        {
            query = query.Where(audit => audit.Action == action);
        }

        if (outcomeFilter is { } outcome)
        {
            query = query.Where(audit => audit.Outcome == outcome);
        }

        if (cursor is not null)
        {
            query = query.Where(audit => audit.Timestamp < cursor.Timestamp
                || audit.Timestamp == cursor.Timestamp && audit.Id.CompareTo(cursor.Id) < 0);
        }

        var page = await query
            .OrderByDescending(audit => audit.Timestamp)
            .ThenByDescending(audit => audit.Id)
            .Take(pageSize + 1)
            .Select(audit => AuditEventDto.From(audit))
            .ToArrayAsync(cancellationToken);

        var items = page.Take(pageSize).ToArray();
        var nextContinuationToken = page.Length > pageSize
            ? continuationTokens.Protect(new AuditContinuationCursor(
                context.Membership.WorkspaceId,
                context.User.ObjectId,
                actorObjectId,
                actionFilter,
                outcomeFilter,
                pageSize,
                items[^1].Timestamp,
                items[^1].Id))
            : null;

        return Results.Ok(new AuditEventsResponse(
            items,
            DateTimeOffset.UtcNow,
            items.Length == 0 ? "stale" : "fresh",
            false,
            "Microsoft 365 audit logs remain authoritative for directory changes. This stream records platform actor intent, result and correlation references.",
            nextContinuationToken));
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
    string AuthoritativeSourceNotice,
    string? NextContinuationToken = null);

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
