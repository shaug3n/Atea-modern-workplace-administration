using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/devices", SearchAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{deviceObjectId}", GetDetailAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{deviceObjectId}/recovery/bitlocker", GetBitlockerMetadataAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{deviceObjectId}/recovery/laps", GetLapsMetadataAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapPost("/api/devices/{deviceObjectId}/recovery/bitlocker/{keyId}/reveal", RevealBitlockerAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapPost("/api/devices/{deviceObjectId}/recovery/laps/reveal", RevealLapsAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapPost("/api/devices/{deviceObjectId}/actions/{action}", ExecuteActionAsync)
            .RequireAuthorization()
            .RequireWorkspaceModule("devices")
            .RequireCapability(Capability.DevicesPrivilegedManage);
        return endpoints;
    }

    private static async Task<IResult> GetDetailAsync(string deviceObjectId, IManagedDeviceDetailReader reader, HttpContext http, CancellationToken cancellationToken)
    {
        NoStore(http);
        if (!DeviceTarget.IsSafe(deviceObjectId)) return Results.BadRequest(new { status = "invalid_target" });
        try
        {
            var result = await reader.GetAsync(deviceObjectId, cancellationToken);
            if (result.Error is not null) return RecoveryResponse(new RecoveryResult<object>(result.Error.Category == "not_authorized" ? "graph_forbidden" : result.Error.Category,
                GraphCorrelationId: result.Error.CorrelationId, GraphRequestId: result.Error.RequestId));
            return result.Value is null ? Results.NotFound(new { status = "device_not_found" }) : Results.Ok(result.Value);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Results.Json(new { status = "temporarily_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> GetBitlockerMetadataAsync(string deviceObjectId, IWorkspaceContextAccessor accessor, IDeviceRecoveryService service, HttpContext http, CancellationToken cancellationToken)
    {
        NoStore(http);
        return RecoveryResponse(await service.GetBitlockerMetadataAsync(accessor.Current!, deviceObjectId, cancellationToken));
    }

    private static async Task<IResult> GetLapsMetadataAsync(string deviceObjectId, IWorkspaceContextAccessor accessor, IDeviceRecoveryService service, HttpContext http, CancellationToken cancellationToken)
    {
        NoStore(http);
        return RecoveryResponse(await service.GetLapsMetadataAsync(accessor.Current!, deviceObjectId, cancellationToken));
    }

    private static async Task<IResult> RevealBitlockerAsync(string deviceObjectId, string keyId, RecoveryRevealRequest? input, IWorkspaceContextAccessor accessor, IDeviceRecoveryService service, HttpContext http, CancellationToken cancellationToken)
    {
        NoStore(http);
        if (string.IsNullOrWhiteSpace(input?.Reason)) return RecoveryResponse(new RecoveryResult<BitlockerSecret>("reason_required"));
        return RecoveryResponse(await service.RevealBitlockerAsync(accessor.Current!, deviceObjectId, keyId, input.Reason, cancellationToken));
    }

    private static async Task<IResult> RevealLapsAsync(string deviceObjectId, RecoveryRevealRequest? input, IWorkspaceContextAccessor accessor, IDeviceRecoveryService service, HttpContext http, CancellationToken cancellationToken)
    {
        NoStore(http);
        if (string.IsNullOrWhiteSpace(input?.Reason)) return RecoveryResponse(new RecoveryResult<LapsSecret>("reason_required"));
        return RecoveryResponse(await service.RevealLapsAsync(accessor.Current!, deviceObjectId, input.Reason, cancellationToken));
    }

    private sealed record RecoveryRevealRequest(string? Reason);

    private static void NoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";
    }

    private static IResult RecoveryResponse<T>(RecoveryResult<T> result) => Results.Json(result, statusCode: result.Status switch
    {
        "succeeded" => StatusCodes.Status200OK,
        "reason_required" or "invalid_target" => StatusCodes.Status400BadRequest,
        "device_not_found" or "entra_device_missing" or "recovery_not_found" => StatusCodes.Status404NotFound,
        "missing_scope" or "consent_required" or "graph_forbidden" => StatusCodes.Status403Forbidden,
        "throttled" => StatusCodes.Status429TooManyRequests,
        _ => StatusCodes.Status503ServiceUnavailable
    });

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
