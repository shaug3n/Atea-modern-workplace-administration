using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.Features.AdminAuth;

public static class AdminAuthEndpoints
{
    public static IEndpointRouteBuilder MapAdminAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/admin-auth").AllowAnonymous();
        group.MapPost("/login", (Delegate)LoginAsync);
        group.MapPost("/logout", (Delegate)LogoutAsync);
        group.MapGet("/session", (Delegate)Session);
        return endpoints;
    }

    private static async Task<IResult> LoginAsync(LoginRequest request, HttpContext context, IOptions<LocalAdminOptions> options, IHostEnvironment environment)
    {
        if (!LocalAdminAuthentication.IsConfigured(environment, options.Value)) return Results.Unauthorized();
        var principal = LocalAdminAuthentication.Authenticate(options.Value, request.Username, request.Password);
        if (principal is null) return Results.Unauthorized();
        await context.SignInAsync(LocalAdminAuthentication.Scheme, principal);
        return Results.Ok(new { authenticated = true, displayName = options.Value.DisplayName, objectId = options.Value.ObjectId });
    }

    private static async Task<IResult> LogoutAsync(HttpContext context)
    {
        await context.SignOutAsync(LocalAdminAuthentication.Scheme);
        return Results.NoContent();
    }

    private static async Task<IResult> Session(HttpContext context)
    {
        var result = await context.AuthenticateAsync(LocalAdminAuthentication.Scheme);
        var user = result.Principal;
        if (!result.Succeeded || user is null || !user.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true"))
            return Results.Unauthorized();
        return Results.Ok(new { authenticated = true, displayName = user.FindFirstValue("name"), objectId = user.FindFirstValue("oid") });
    }

    public sealed record LoginRequest(string Username, string Password);
}
