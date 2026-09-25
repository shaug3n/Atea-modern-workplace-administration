using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/devices", SearchAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapPost("/api/devices/{deviceObjectId}/actions/{action}", ExecuteActionAsync)
            .RequireAuthorization()
            .RequireWorkspaceModule("devices")
            .RequireCapability(Capability.DevicesPrivilegedManage);
        return endpoints;
    }

    private static async Task<IResult> ExecuteActionAsync(
        string deviceObjectId,
        string action,
        IWorkspaceContextAccessor accessor,
        IDeviceCommandService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(deviceObjectId) || deviceObjectId.Any(character => char.IsControl(character) || character is '/' or '\\'))
        {
            return Results.BadRequest(new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "invalid_target"));
        }

        if (!request.Headers.TryGetValue("Idempotency-Key", out var values) || string.IsNullOrWhiteSpace(values.ToString()))
        {
            return Results.BadRequest(new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "idempotency_key_required"));
        }

        var idempotencyKey = values.ToString().Trim();
        if (!DeviceCommandInputValidation.IsValidIdempotencyKey(idempotencyKey))
        {
            return Results.BadRequest(new DeviceCommandResult(DeviceCommandStatus.InvalidTarget, Capability.DevicesPrivilegedManage, "invalid_idempotency_key"));
        }

        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new DeviceCommandResult(DeviceCommandStatus.Denied, Capability.DevicesPrivilegedManage, "workspace_membership_required"), statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.ExecuteAsync(context, deviceObjectId, action, idempotencyKey, cancellationToken);
        return result.Status switch
        {
            DeviceCommandStatus.Succeeded => Results.Ok(result),
            DeviceCommandStatus.Denied => Results.Json(result, statusCode: StatusCodes.Status403Forbidden),
            DeviceCommandStatus.InvalidTarget => Results.BadRequest(result),
            DeviceCommandStatus.NotFound => Results.NotFound(result),
            _ => Results.Json(result, statusCode: StatusCodes.Status503ServiceUnavailable)
        };
    }

    private static async Task<IResult> SearchAsync(
        IWorkspaceContextAccessor accessor,
        IDeviceService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var query = new DeviceSearchRequest(
                Query(request, "search"),
                IntegerQuery(request, "pageSize", 50),
                Query(request, "complianceState"),
                Query(request, "operatingSystem"),
                Query(request, "continuationToken"));
            return Results.Ok(await service.SearchAsync(context, query, cancellationToken));
        }
        catch (DeviceSearchValidationException exception)
        {
            return Results.BadRequest(new { error = "invalid_query", field = exception.Field, message = exception.Message });
        }
    }

    private static string? Query(HttpRequest request, string name) => request.Query.TryGetValue(name, out var values) ? values.ToString() : null;

    private static int IntegerQuery(HttpRequest request, string name, int fallback)
    {
        if (!request.Query.TryGetValue(name, out var values) || string.IsNullOrWhiteSpace(values)) return fallback;
        return int.TryParse(values, out var parsed) ? parsed : throw new DeviceSearchValidationException($"{name} must be an integer.", name);
    }
}
