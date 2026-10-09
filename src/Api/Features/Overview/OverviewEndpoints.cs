using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public static class OverviewEndpoints
{
    public static IEndpointRouteBuilder MapOverviewEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/overview", GetOverviewAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetOverviewAsync(
        IWorkspaceContextAccessor accessor,
        IWorkspaceSettingsService settings,
        IOverviewService service,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var configuration = await settings.GetConfigurationAsync(context, cancellationToken);
        var modules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, configuration.EnabledModules, context.Membership.ModuleKeys);
        var response = await service.GetAsync(context, modules, cancellationToken);
        return Results.Ok(response);
    }
}
