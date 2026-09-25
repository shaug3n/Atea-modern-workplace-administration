using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserSessionCommandEndpoints
{
    public static IEndpointRouteBuilder MapUserSessionCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/users/{userObjectId}/revoke-sessions", RevokeAsync)
            .RequireAuthorization().RequireWorkspaceModule("users").RequireCapability(Capability.UsersRevokeSessions);
        return endpoints;
    }

    private static async Task<IResult> RevokeAsync(string userObjectId, IWorkspaceContextAccessor accessor, IUserSessionCommandService service, HttpRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(character => char.IsControl(character) || character is '/' or '\\')) return Results.BadRequest(new { error = "invalid_target" });
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString())) return Results.BadRequest(new { error = "idempotency_key_required" });
        if (accessor.Current is not { } context) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.RevokeAsync(context, userObjectId, values.ToString().Trim(), cancellationToken);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result), "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "invalid_target" => Results.BadRequest(result), "idempotency_key_reused" => Results.Conflict(result),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }
}
