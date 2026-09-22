using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
using Atea.UnifiedWorkplace.Api.Features.Devices;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public sealed class WorkspaceContextMiddleware(RequestDelegate next)
{
    public const string ItemKey = "Atea.UnifiedWorkplace.WorkspaceContext";

    public async Task InvokeAsync(HttpContext httpContext, WorkspaceContextResolver resolver)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api") ||
            httpContext.Request.Path.StartsWithSegments("/api/invitations") ||
            httpContext.Request.Path.StartsWithSegments("/api/user-preferences") ||
            httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        var resolution = await resolver.ResolveAsync(httpContext.User, httpContext.RequestAborted);
        if (!resolution.Succeeded)
        {
            if (resolution.FailureReason == WorkspaceContextFailureReason.WorkspaceMembershipRequired
                && httpContext.Request.Path.StartsWithSegments("/api/devices")
                && httpContext.Request.Path.Value?.Contains("/actions/", StringComparison.OrdinalIgnoreCase) == true)
            {
                httpContext.Response.StatusCode = StatusCodes.Status403Forbidden;
                await httpContext.Response.WriteAsJsonAsync(
                    new DeviceCommandResult(DeviceCommandStatus.Denied, Capability.DevicesPrivilegedManage, "workspace_membership_required"),
                    httpContext.RequestAborted);
                return;
            }

            var code = resolution.IsAuthenticationFailure
                ? ApiProblemCode.AuthenticationRequired
                : ApiProblemCode.AuthorizationDenied;
            var detail = resolution.IsAuthenticationFailure
                ? null
                : "The signed-in user is not assigned to this workspace.";
            await ApiProblemDetails.WriteAsync(httpContext, code, detail);
            return;
        }

        httpContext.Items[ItemKey] = resolution.Context;
        await next(httpContext);
    }
}
