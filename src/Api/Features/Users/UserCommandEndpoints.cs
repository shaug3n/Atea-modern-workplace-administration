using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserCommandEndpoints
{
    public static IEndpointRouteBuilder MapUserCommandEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/users", CreateUserAsync).RequireAuthorization().RequireCapability(Capability.UsersCreate);
        endpoints.MapPatch("/api/users/{userObjectId}", UpdateUserAsync).RequireAuthorization().RequireCapability(Capability.UsersUpdate);
        endpoints.MapPost("/api/users/{userObjectId}/disable", DisableUserAsync).RequireAuthorization().RequireCapability(Capability.UsersDisable);
        endpoints.MapPost("/api/users/{userObjectId}/reactivate", ReactivateUserAsync).RequireAuthorization().RequireCapability(Capability.UsersDisable);
        endpoints.MapPost("/api/users/{userObjectId}/groups/{groupObjectId}", AddGroupAsync).RequireAuthorization().RequireCapability(Capability.GroupsManageMembers);
        endpoints.MapDelete("/api/users/{userObjectId}/groups/{groupObjectId}", RemoveGroupAsync).RequireAuthorization().RequireCapability(Capability.GroupsManageMembers);
        endpoints.MapPost("/api/users/{userObjectId}/licenses/{skuId}", AssignLicenseAsync).RequireAuthorization().RequireCapability(Capability.LicensesAssign);
        endpoints.MapDelete("/api/users/{userObjectId}/licenses/{skuId}", RemoveLicenseAsync).RequireAuthorization().RequireCapability(Capability.LicensesAssign);
        return endpoints;
    }

    private static async Task<IResult> CreateUserAsync(
        CreateUserCommand command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.CreateAsync(context, command, idempotencyKey, cancellationToken), StatusCodes.Status201Created);
    }

    private static async Task<IResult> UpdateUserAsync(
        string userObjectId,
        UpdateUserCommand command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.UpdateAsync(context, userObjectId, command, idempotencyKey, cancellationToken));
    }

    private static Task<IResult> DisableUserAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken) =>
        SetAccountEnabledAsync(userObjectId, false, accessor, service, request, cancellationToken);

    private static Task<IResult> ReactivateUserAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken) =>
        SetAccountEnabledAsync(userObjectId, true, accessor, service, request, cancellationToken);

    private static async Task<IResult> SetAccountEnabledAsync(
        string userObjectId,
        bool enabled,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.SetAccountEnabledAsync(context, userObjectId, new SetAccountEnabledCommand(enabled), idempotencyKey, cancellationToken));
    }

    private static async Task<IResult> AddGroupAsync(
        string userObjectId,
        string groupObjectId,
        [FromBody] GroupMembershipCommand? command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidatedTarget(groupObjectId, "groupObjectId", out var routeGroupObjectId, out var invalidTarget))
        {
            return invalidTarget;
        }

        if (!TryMatchRouteTarget(command?.GroupObjectId, routeGroupObjectId, "groupObjectId", out var mismatch))
        {
            return mismatch;
        }

        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.AddGroupAsync(context, userObjectId, new GroupMembershipCommand(routeGroupObjectId), idempotencyKey, cancellationToken));
    }

    private static async Task<IResult> RemoveGroupAsync(
        string userObjectId,
        string groupObjectId,
        [FromBody] GroupMembershipCommand? command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidatedTarget(groupObjectId, "groupObjectId", out var routeGroupObjectId, out var invalidTarget))
        {
            return invalidTarget;
        }

        if (!TryMatchRouteTarget(command?.GroupObjectId, routeGroupObjectId, "groupObjectId", out var mismatch))
        {
            return mismatch;
        }

        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.RemoveGroupAsync(context, userObjectId, new GroupMembershipCommand(routeGroupObjectId), idempotencyKey, cancellationToken));
    }

    private static async Task<IResult> AssignLicenseAsync(
        string userObjectId,
        string skuId,
        LicenseAssignmentCommand? command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidatedTarget(skuId, "skuId", out var routeSkuId, out var invalidTarget))
        {
            return invalidTarget;
        }

        if (!TryMatchRouteTarget(command?.SkuId, routeSkuId, "skuId", out var mismatch))
        {
            return mismatch;
        }

        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.AssignLicenseAsync(
            context,
            userObjectId,
            new LicenseAssignmentCommand(routeSkuId, command?.DisabledPlans ?? []),
            idempotencyKey,
            cancellationToken));
    }

    private static async Task<IResult> RemoveLicenseAsync(
        string userObjectId,
        string skuId,
        [FromBody] LicenseAssignmentCommand? command,
        IWorkspaceContextAccessor accessor,
        IUserCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryValidatedTarget(skuId, "skuId", out var routeSkuId, out var invalidTarget))
        {
            return invalidTarget;
        }

        if (!TryMatchRouteTarget(command?.SkuId, routeSkuId, "skuId", out var mismatch))
        {
            return mismatch;
        }

        if (!TryContext(accessor, out var context, out var missingContext))
        {
            return missingContext;
        }

        if (!TryIdempotencyKey(request, out var idempotencyKey, out var missingKey))
        {
            return missingKey;
        }

        return ToResult(await service.RemoveLicenseAsync(context, userObjectId, new LicenseAssignmentCommand(routeSkuId, []), idempotencyKey, cancellationToken));
    }

    private static bool TryContext(IWorkspaceContextAccessor accessor, out WorkspaceContext context, out IResult result)
    {
        if (accessor.Current is { } current)
        {
            context = current;
            result = Results.Empty;
            return true;
        }

        context = null!;
        result = Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        return false;
    }

    private static bool TryIdempotencyKey(HttpRequest request, out string idempotencyKey, out IResult result)
    {
        idempotencyKey = request.Headers.TryGetValue("Idempotency-Key", out var values) ? values.ToString().Trim() : string.Empty;
        if (!string.IsNullOrWhiteSpace(idempotencyKey))
        {
            result = Results.Empty;
            return true;
        }

        result = Results.BadRequest(new { error = "idempotency_key_required" });
        return false;
    }

    private static IResult ToResult(UserCommandResult result, int successStatus = StatusCodes.Status200OK) => result.Status switch
    {
        UserCommandStatus.Succeeded => Results.Json(result, statusCode: successStatus),
        UserCommandStatus.Denied => Results.Json(new { error = result.Error, capability = result.RequiredCapability, state = result.Authorization?.State }, statusCode: StatusCodes.Status403Forbidden),
        UserCommandStatus.SourceOfAuthorityReadOnly => Results.Json(result, statusCode: StatusCodes.Status409Conflict),
        UserCommandStatus.Conflict => Results.Json(result, statusCode: StatusCodes.Status409Conflict),
        UserCommandStatus.IdempotencyKeyReused => Results.Json(result, statusCode: StatusCodes.Status409Conflict),
        UserCommandStatus.NotFound => Results.Json(result, statusCode: StatusCodes.Status404NotFound),
        UserCommandStatus.InvalidTarget => Results.Json(result, statusCode: StatusCodes.Status400BadRequest),
        _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
    };

    private static bool TryValidatedTarget(string targetId, string fieldName, out string validTargetId, out IResult result)
    {
        validTargetId = targetId;
        if (!IsSafeTargetId(targetId))
        {
            result = Results.BadRequest(new { error = "invalid_target", target = fieldName });
            return false;
        }

        result = Results.Empty;
        return true;
    }

    private static bool TryMatchRouteTarget(string? bodyTargetId, string routeTargetId, string fieldName, out IResult result)
    {
        if (bodyTargetId is null || string.Equals(bodyTargetId, routeTargetId, StringComparison.Ordinal))
        {
            result = Results.Empty;
            return true;
        }

        result = Results.BadRequest(new { error = "target_mismatch", target = fieldName });
        return false;
    }

    private static bool IsSafeTargetId(string targetId) =>
        !string.IsNullOrWhiteSpace(targetId)
        && targetId.Length <= 256
        && !targetId.Any(character => char.IsControl(character) || character is '/' or '\\');
}
