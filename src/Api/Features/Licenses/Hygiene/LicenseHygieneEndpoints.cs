using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;

public static class LicenseHygieneEndpoints
{
    public static IEndpointRouteBuilder MapLicenseHygieneEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/licenses/hygiene", RefreshAsync)
            .RequireAuthorization()
            .RequireWorkspaceModule("license-hygiene");
        return endpoints;
    }

    private static async Task<IResult> RefreshAsync(
        IWorkspaceContextAccessor accessor,
        ILicenseHygieneService service,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(
                new { error = "workspace_membership_required" },
                statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await service.RefreshAsync(context, cancellationToken));
    }
}
