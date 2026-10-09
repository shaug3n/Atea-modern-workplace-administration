using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public sealed class OverviewActivityReader(WorkplaceDbContext db) : IOverviewActivityReader
{
    public async Task<IReadOnlyList<OverviewActivityItem>> ReadRecentAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken) =>
        await BuildRecentQuery(db.AuditEvents.AsNoTracking(), context).ToArrayAsync(cancellationToken);

    public static IQueryable<OverviewActivityItem> BuildRecentQuery(
        IQueryable<AuditEvent> auditEvents,
        WorkspaceContext context) =>
        auditEvents
            .Where(audit => audit.WorkspaceId == context.Membership.WorkspaceId
                && audit.TenantId == context.User.TenantId
                && audit.ActorTenantId == context.User.TenantId)
            .OrderByDescending(audit => audit.Timestamp)
            .ThenByDescending(audit => audit.Id)
            .Take(5)
            .Select(audit => new OverviewActivityItem(audit.Action, audit.Outcome, audit.Timestamp));
}
