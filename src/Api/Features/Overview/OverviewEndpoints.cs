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

        var response = await service.GetAsync(context, cancellationToken);
        var configuration = await settings.GetConfigurationAsync(context, cancellationToken);
        var modules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, configuration.EnabledModules, context.Membership.ModuleKeys);
        if (!modules.Contains("users", StringComparer.OrdinalIgnoreCase))
            response = response with { TotalUsers = 0, PermissionHealth = new PermissionHealthSummary("hidden", 0, 0), PimAttention = new PimAttentionSummary(false, 0) };
        if (!modules.Contains("licenses", StringComparer.OrdinalIgnoreCase))
            response = response with { LicenseCoverage = new LicenseCoverageSummary(0, 0, 0) };
        return Results.Ok(response);
    }
}
