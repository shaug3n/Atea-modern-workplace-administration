using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Microsoft.AspNetCore.Mvc;

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
        [FromBody] UserWriteReasonCommand? command,
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

        if (!UserWriteReasonValidation.TryNormalize(command?.Reason, out var reason, out var reasonError))
        {
            return Results.BadRequest(new { error = reasonError });
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.RemoveAsync(context, userObjectId, methodObjectId, methodType, values.ToString().Trim(), cancellationToken, reason);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result),
            "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "not_found" => Results.NotFound(result),
            "invalid_target" => Results.BadRequest(result),
            "idempotency_key_reused" => Results.Conflict(result),
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
        [FromBody] UserWriteReasonCommand? command,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(IsUnsafe))
        {
            return Results.BadRequest(new { error = "invalid_target" });
        }

        if (!UserWriteReasonValidation.TryNormalize(command?.Reason, out var reason, out var reasonError))
        {
            return Results.BadRequest(new { error = reasonError });
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Results.BadRequest(new { error = "idempotency_key_required" });
        }

        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.ResetMfaAsync(context, userObjectId, values.ToString().Trim(), cancellationToken, reason);
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
        [FromBody] TemporaryAccessPassRequest? command,
        IWorkspaceContextAccessor accessor,
        IAuthenticationMethodService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(userObjectId) || userObjectId.Any(IsUnsafe)) return Results.BadRequest(new { error = "invalid_target" });
        if (!UserWriteReasonValidation.TryNormalize(command?.Reason, out var reason, out var reasonError)) return Results.BadRequest(new { error = reasonError });
        var tapRequest = command ?? new TemporaryAccessPassRequest();
        if (tapRequest.LifetimeInMinutes is < 10 or > 1440) return Results.BadRequest(new { error = "invalid_request" });
        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString())) return Results.BadRequest(new { error = "idempotency_key_required" });
        var context = accessor.Current;
        if (context is null) return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        var result = await service.CreateTemporaryAccessPassAsync(context, userObjectId, values.ToString().Trim(), tapRequest with { Reason = reason }, cancellationToken);
        return result.Status switch
        {
            "succeeded" => Results.Ok(result),
            "denied" => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            "invalid_target" => Results.BadRequest(result),
            "idempotency_key_reused" => Results.Conflict(result),
            "unsupported_options" => Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity),
            "policy_rejected" => Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static bool IsUnsafe(char character) => char.IsControl(character) || character is '/' or '\\';
}
