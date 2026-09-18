namespace Atea.UnifiedWorkplace.Api.Authorization;

public sealed class WorkspaceContextMiddleware(RequestDelegate next)
{
    public const string ItemKey = "Atea.UnifiedWorkplace.WorkspaceContext";

    public async Task InvokeAsync(HttpContext httpContext, WorkspaceContextResolver resolver)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api") || httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        var resolution = await resolver.ResolveAsync(httpContext.User, httpContext.RequestAborted);
        if (!resolution.Succeeded)
        {
            httpContext.Response.StatusCode = resolution.IsAuthenticationFailure
                ? StatusCodes.Status401Unauthorized
                : StatusCodes.Status403Forbidden;
            httpContext.Response.ContentType = "application/json";
            await httpContext.Response.WriteAsJsonAsync(new
            {
                error = resolution.IsAuthenticationFailure ? "authentication_required" : "workspace_membership_required"
            }, httpContext.RequestAborted);
            return;
        }

        httpContext.Items[ItemKey] = resolution.Context;
        await next(httpContext);
    }
}
