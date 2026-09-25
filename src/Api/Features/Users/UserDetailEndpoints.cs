using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserDetailEndpoints
{
    public static IEndpointRouteBuilder MapUserDetailEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/{userObjectId}", GetUserDetailAsync).RequireAuthorization().RequireWorkspaceModule("users");
        return endpoints;
    }

    private static async Task<IResult> GetUserDetailAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IUserDetailService service,
        IWorkspaceSettingsService settings,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.GetDetailAsync(context, userObjectId, cancellationToken);
        if (result.Detail is { } detail)
        {
            var configuration = await settings.GetConfigurationAsync(context, cancellationToken);
            var modules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, configuration.EnabledModules, context.Membership.ModuleKeys);
            if (!modules.Contains("licenses", StringComparer.OrdinalIgnoreCase))
            {
                var hidden = new CapabilityDecision(Capability.LicensesView, CapabilityState.Hidden, "workspace_module_unavailable");
                var access = detail.Licenses.Access with { Authorization = hidden, Freshness = UserDirectoryFreshness.Unavailable, PartialData = false, Error = null };
                result = result with { Detail = detail with { Licenses = detail.Licenses with { Access = access, Items = [] } } };
            }
        }
        return result.Status switch
        {
            UserDetailStatus.NotFound => Results.NotFound(new { error = result.Error?.Category ?? "user_not_found", message = result.Error?.Message }),
            _ => Results.Ok(result.Detail)
        };
    }
}
