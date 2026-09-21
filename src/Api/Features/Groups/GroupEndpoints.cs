using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Groups;

public static class GroupEndpoints
{
    public static IEndpointRouteBuilder MapGroupEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}/groups", GetUserGroupsAsync).RequireAuthorization();
        endpoints.MapGet("/api/groups", GetGroupsAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetUserGroupsAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IUserDetailService service,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var section = await service.GetGroupsAsync(context, userObjectId, cancellationToken);
        return section is null ? Results.NotFound(new { error = "user_not_found" }) : Results.Ok(section);
    }

    private static async Task<IResult> GetGroupsAsync(IWorkspaceContextAccessor accessor, IGroupCatalogReader reader, HttpRequest request, IGraphAuthorizationSnapshotReader snapshotReader, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: 403);
        var snapshot = await snapshotReader.ReadAsync(context, cancellationToken);
        var access = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.GroupsManageMembers];
        if (access.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly)) return Results.Ok(new { items = Array.Empty<object>(), search = request.Query["search"].ToString(), pageSize = 25, fetchedAt = DateTimeOffset.UtcNow, access });
        var search = request.Query["search"].ToString();
        var pageSize = int.TryParse(request.Query["pageSize"], out var parsed) ? Math.Clamp(parsed, 1, 100) : 25;
        var result = await reader.ReadGroupsAsync(search, pageSize, cancellationToken);
        return result.Error is null ? Results.Ok(new GroupCatalogResponse(result.Value, string.IsNullOrWhiteSpace(search) ? null : search, pageSize, DateTimeOffset.UtcNow, access)) : Results.Json(new { error = result.Error.Category, access }, statusCode: result.Error.StatusCode ?? 503);
    }
}
