using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Pim;

public static class PimEndpoints
{
    public static IEndpointRouteBuilder MapPimEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}/pim", GetUserPimAsync).RequireAuthorization();
        endpoints.MapPost("/api/pim/activations", ActivateAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetUserPimAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IPimService service,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        return Results.Ok(await service.GetUserPimAsync(context, userObjectId, cancellationToken));
    }

    private static async Task<IResult> ActivateAsync(
        PimActivationRequest command,
        IWorkspaceContextAccessor accessor,
        IPimService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var idempotencyKey = request.Headers.TryGetValue("Idempotency-Key", out var values) ? values.ToString().Trim() : string.Empty;
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        var result = await service.ActivateAsync(context, command, idempotencyKey, cancellationToken);
        return Results.Json(result, statusCode: StatusCodeFor(result));
    }

    private static int StatusCodeFor(PimActivationResult result) => result.Status switch
    {
        PimStatus.Active or PimStatus.ActivationPending => StatusCodes.Status200OK,
        PimStatus.NotAuthorized => StatusCodes.Status403Forbidden,
        PimStatus.NotEligible or PimStatus.PolicyBlocked => StatusCodes.Status409Conflict,
        _ => StatusCodes.Status503ServiceUnavailable
    };
}
