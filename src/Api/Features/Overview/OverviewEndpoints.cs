using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public static class OverviewEndpoints
{
    public static IEndpointRouteBuilder MapOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/overview", GetOverviewAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        IWorkspaceContextAccessor accessor,
        IOverviewService service,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await service.GetAsync(context, cancellationToken));
    }
}
