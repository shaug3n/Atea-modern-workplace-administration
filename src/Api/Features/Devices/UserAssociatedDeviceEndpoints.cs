using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class UserAssociatedDeviceEndpoints
{
    public static IEndpointRouteBuilder MapUserAssociatedDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}/devices", GetAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        UserAssociatedDeviceService service,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            return Results.BadRequest(new { error = "invalid_target" });
        }

        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await service.GetAsync(context, userObjectId, cancellationToken));
    }
}
