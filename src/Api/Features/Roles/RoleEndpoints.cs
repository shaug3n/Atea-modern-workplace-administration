using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Features.Roles;

public static class RoleEndpoints
{
    public static IEndpointRouteBuilder MapRoleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}/roles", GetUserRolesAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetUserRolesAsync(
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

        var section = await service.GetRolesAsync(context, userObjectId, cancellationToken);
        return section is null ? Results.NotFound(new { error = "user_not_found" }) : Results.Ok(section);
    }
}
