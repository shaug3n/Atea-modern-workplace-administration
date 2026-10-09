using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class Device360Endpoints
{
    public static IEndpointRouteBuilder MapDevice360Endpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/devices/{managedDeviceId}/compliance-policies", GetCompliancePolicyStatesAsync)
            .RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{managedDeviceId}/configuration/reported", GetDeviceConfigurationStatesAsync)
            .RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{managedDeviceId}/configuration/assignments", GetConfigurationAssignmentsAsync)
            .RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{managedDeviceId}/apps", GetDetectedAppsAsync)
            .RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/devices/{managedDeviceId}/protection", GetWindowsProtectionStateAsync)
            .RequireAuthorization().RequireWorkspaceModule("devices");
        return endpoints;
    }

    private static Task<IResult> GetCompliancePolicyStatesAsync(
        string managedDeviceId,
        IWorkspaceContextAccessor accessor,
        IDevice360Service service,
        HttpContext http,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetCompliancePolicyStatesAsync(accessor.Current!, managedDeviceId, cancellationToken),
            http,
            cancellationToken);

    private static Task<IResult> GetDeviceConfigurationStatesAsync(
        string managedDeviceId,
        IWorkspaceContextAccessor accessor,
        IDevice360Service service,
        HttpContext http,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetDeviceConfigurationStatesAsync(accessor.Current!, managedDeviceId, cancellationToken),
            http,
            cancellationToken);

    private static Task<IResult> GetConfigurationAssignmentsAsync(
        string managedDeviceId,
        IWorkspaceContextAccessor accessor,
        IDevice360Service service,
        HttpContext http,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetConfigurationAssignmentsAsync(accessor.Current!, managedDeviceId, cancellationToken),
            http,
            cancellationToken);

    private static Task<IResult> GetDetectedAppsAsync(
        string managedDeviceId,
        IWorkspaceContextAccessor accessor,
        IDevice360Service service,
        HttpContext http,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetDetectedAppsAsync(accessor.Current!, managedDeviceId, cancellationToken),
            http,
            cancellationToken);

    private static Task<IResult> GetWindowsProtectionStateAsync(
        string managedDeviceId,
        IWorkspaceContextAccessor accessor,
        IDevice360Service service,
        HttpContext http,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            () => service.GetWindowsProtectionStateAsync(accessor.Current!, managedDeviceId, cancellationToken),
            http,
            cancellationToken);

    private static async Task<IResult> ExecuteAsync<T>(
        Func<Task<Device360SectionResponse<T>>> action,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        SetNoStore(http);
        try
        {
            var response = await action();
            return Results.Json(response, statusCode: StatusCodeFor(response.Status));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return Results.Json(
                new Device360SectionResponse<T>(Device360Status.Failed),
                statusCode: StatusCodes.Status502BadGateway);
        }
    }

    private static int StatusCodeFor(string status) => status switch
    {
        Device360Status.Succeeded or Device360Status.Partial
            or Device360Status.Unsupported or Device360Status.NoReportedPolicies => StatusCodes.Status200OK,
        Device360Status.InvalidTarget => StatusCodes.Status400BadRequest,
        Device360Status.DeviceNotFound => StatusCodes.Status404NotFound,
        Device360Status.CapabilityRequired or Device360Status.MissingScope
            or Device360Status.ConsentRequired or Device360Status.GraphForbidden => StatusCodes.Status403Forbidden,
        Device360Status.Throttled => StatusCodes.Status429TooManyRequests,
        Device360Status.TemporarilyUnavailable => StatusCodes.Status503ServiceUnavailable,
        _ => StatusCodes.Status502BadGateway
    };

    private static void SetNoStore(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers.Pragma = "no-cache";
        http.Response.Headers.Expires = "0";
    }
}
