using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.UserPreferences;

public static class UserPreferenceEndpoints
{
    private static readonly HashSet<string> AllowedThemes = new(StringComparer.Ordinal) { "light", "dark" };

    public static IEndpointRouteBuilder MapUserPreferenceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/user-preferences").RequireAuthorization();
        group.MapGet("/theme", GetThemeAsync);
        group.MapPut("/theme", SetThemeAsync);
        return endpoints;
    }

    private static async Task<IResult> GetThemeAsync(HttpContext httpContext, IWorkspaceContextAccessor workspaceAccessor, IThemePreferenceService preferences, IWorkspaceSettingsService workspaceSettings, CancellationToken cancellationToken)
    {
        if (!TryReadUserKey(httpContext.User, out var tenantId, out var userObjectId))
        {
            return Results.Unauthorized();
        }

        var theme = await preferences.GetThemeAsync(tenantId, userObjectId, cancellationToken);
        if (theme is null && workspaceAccessor.Current is { } context)
        {
            theme = (await workspaceSettings.GetConfigurationAsync(context, cancellationToken)).DefaultTheme;
        }
        return Results.Ok(new ThemePreferenceResponse(theme));
    }

    private static async Task<IResult> SetThemeAsync(ThemePreferenceRequest request, HttpContext httpContext, IThemePreferenceService preferences, CancellationToken cancellationToken)
    {
        if (!TryReadUserKey(httpContext.User, out var tenantId, out var userObjectId))
        {
            return Results.Unauthorized();
        }

        if (request.Theme is null || !AllowedThemes.Contains(request.Theme))
        {
            return Results.BadRequest(new { error = "invalid_theme_preference" });
        }

        await preferences.SetThemeAsync(tenantId, userObjectId, request.Theme, cancellationToken);
        return Results.Ok(new ThemePreferenceResponse(request.Theme));
    }

    private static bool TryReadUserKey(ClaimsPrincipal principal, out Guid tenantId, out Guid userObjectId)
    {
        var hasTenantId = Guid.TryParse(principal.FindFirstValue("tid"), out tenantId);
        var hasUserObjectId = Guid.TryParse(principal.FindFirstValue("oid"), out userObjectId);
        return hasTenantId && hasUserObjectId;
    }
}

public sealed record ThemePreferenceRequest(string? Theme);
public sealed record ThemePreferenceResponse(string? Theme);

public interface IThemePreferenceService
{
    Task<string?> GetThemeAsync(Guid tenantId, Guid userObjectId, CancellationToken cancellationToken = default);
    Task SetThemeAsync(Guid tenantId, Guid userObjectId, string theme, CancellationToken cancellationToken = default);
}

public sealed class ThemePreferenceService(IUserPreferenceRepository repository) : IThemePreferenceService
{
    public Task<string?> GetThemeAsync(Guid tenantId, Guid userObjectId, CancellationToken cancellationToken = default) =>
        repository.GetThemeAsync(tenantId, userObjectId, cancellationToken);

    public Task SetThemeAsync(Guid tenantId, Guid userObjectId, string theme, CancellationToken cancellationToken = default) =>
        repository.SetThemeAsync(tenantId, userObjectId, theme, cancellationToken);
}
