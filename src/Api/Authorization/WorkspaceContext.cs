using System.Security.Claims;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public enum WorkspaceContextFailureReason
{
    MissingTenantId,
    MissingObjectId,
    WrongAudience,
    WorkspaceMembershipRequired
}

public sealed record WorkspaceContext(AuthenticatedUser User, WorkspaceMembership Membership);

public sealed record WorkspaceContextResolution(WorkspaceContext? Context, WorkspaceContextFailureReason? FailureReason)
{
    public bool Succeeded => Context is not null;
    public static WorkspaceContextResolution Failed(WorkspaceContextFailureReason reason) => new(null, reason);
    public static WorkspaceContextResolution Success(WorkspaceContext context) => new(context, null);
}

public sealed class WorkspaceContextResolver(string apiAudience, IWorkspaceMembershipReader membershipReader)
{
    public async Task<WorkspaceContextResolution> ResolveAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(principal.FindFirstValue("tid"), out var tenantId))
            return WorkspaceContextResolution.Failed(WorkspaceContextFailureReason.MissingTenantId);
        if (!Guid.TryParse(principal.FindFirstValue("oid"), out var objectId))
            return WorkspaceContextResolution.Failed(WorkspaceContextFailureReason.MissingObjectId);
        if (!principal.FindAll("aud").Any(claim => string.Equals(claim.Value, apiAudience, StringComparison.Ordinal)))
            return WorkspaceContextResolution.Failed(WorkspaceContextFailureReason.WrongAudience);

        var user = new AuthenticatedUser(
            tenantId,
            objectId,
            principal.FindFirstValue("preferred_username") ?? principal.FindFirstValue("upn") ?? string.Empty,
            principal.FindFirstValue("name") ?? string.Empty,
            principal.FindFirstValue("userType") ?? "Member",
            Guid.TryParse(principal.FindFirstValue("home_tid"), out var homeTenantId) ? homeTenantId : null);
        var membership = await membershipReader.FindMembershipAsync(tenantId, objectId, cancellationToken);
        return membership is null
            ? WorkspaceContextResolution.Failed(WorkspaceContextFailureReason.WorkspaceMembershipRequired)
            : WorkspaceContextResolution.Success(new WorkspaceContext(user, membership));
    }
}

public interface IWorkspaceContextAccessor
{
    WorkspaceContext? Current { get; }
}

internal sealed class WorkspaceContextAccessor(IHttpContextAccessor httpContextAccessor) : IWorkspaceContextAccessor
{
    public WorkspaceContext? Current => httpContextAccessor.HttpContext?.Items[WorkspaceContextMiddleware.ItemKey] as WorkspaceContext;
}
