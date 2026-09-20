using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserDetailEndpoints
{
    public static IEndpointRouteBuilder MapUserDetailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}", GetUserDetailAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetUserDetailAsync(
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

        var result = await service.GetDetailAsync(context, userObjectId, cancellationToken);
        return result.Status switch
        {
            UserDetailStatus.NotFound => Results.NotFound(new { error = result.Error?.Category ?? "user_not_found", message = result.Error?.Message }),
            _ => Results.Ok(result.Detail)
        };
    }
}
