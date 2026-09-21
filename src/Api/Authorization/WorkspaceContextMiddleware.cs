using Atea.UnifiedWorkplace.Api.Infrastructure.Http;

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
