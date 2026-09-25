using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Identity;

public static class AuthenticationMethodEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationMethodEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}/authentication-methods", GetAsync).RequireAuthorization().RequireWorkspaceModule("users");
        endpoints.MapDelete("/api/users/{userObjectId}/authentication-methods/{methodObjectId}", RemoveAsync)
            .RequireAuthorization().RequireWorkspaceModule("users")
            .RequireCapability(Capability.AuthenticationMethodsManage);
        endpoints.MapPost("/api/users/{userObjectId}/authentication-methods/reset-mfa", ResetMfaAsync)
            .RequireAuthorization().RequireWorkspaceModule("users")
            .RequireCapability(Capability.AuthenticationMethodsManage);
        endpoints.MapPost("/api/users/{userObjectId}/authentication-methods/temporary-access-pass", CreateTemporaryAccessPassAsync)
            .RequireAuthorization().RequireWorkspaceModule("users")
            .RequireCapability(Capability.AuthenticationMethodsManage);
        return endpoints;
    }

    private static async Task<IResult> RemoveAsync(
        string userObjectId,
        string methodObjectId,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var methodType = request.Query["type"].ToString();
        if (string.IsNullOrWhiteSpace(userObjectId) || string.IsNullOrWhiteSpace(methodObjectId) || string.IsNullOrWhiteSpace(methodType) || userObjectId.Any(IsUnsafe) || methodObjectId.Any(IsUnsafe))
        {
            return Results.BadRequest(new { error = "invalid_target" });
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.RemoveAsync(context, userObjectId, methodObjectId, methodType, values.ToString().Trim(), cancellationToken);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result),
            "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "not_found" => Results.NotFound(result),
            "invalid_target" => Results.BadRequest(result),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<IResult> GetAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
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

    private static async Task<IResult> ResetMfaAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(IsUnsafe))
        {
            return Results.BadRequest(new { error = "invalid_target" });
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.ResetMfaAsync(context, userObjectId, values.ToString().Trim(), cancellationToken);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result),
            "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "invalid_target" => Results.BadRequest(result),
            "idempotency_key_reused" => Results.Conflict(result),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<IResult> CreateTemporaryAccessPassAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(IsUnsafe)) return Results.BadRequest(new { error = "invalid_target" });
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString())) return Results.BadRequest(new { error = "idempotency_key_required" });
        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.CreateTemporaryAccessPassAsync(context, userObjectId, values.ToString().Trim(), cancellationToken);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result),
            "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "invalid_target" => Results.BadRequest(result),
            "idempotency_key_reused" => Results.Conflict(result),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static bool IsUnsafe(char character) => char.IsControl(character) || character is '/' or '\\';
}
