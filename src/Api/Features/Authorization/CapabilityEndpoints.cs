using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;

namespace Atea.UnifiedWorkplace.Api.Features.Authorization;

public static class CapabilityEndpoints
{
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/capabilities", GetCapabilitiesAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetCapabilitiesAsync(
        IWorkspaceContextAccessor accessor,
        IGraphAuthorizationSnapshotReader reader,
        IWorkspaceSettingsService settings,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var snapshot = await reader.ReadAsync(context, cancellationToken);
        var configuration = await settings.GetConfigurationAsync(context, cancellationToken);
        return Results.Ok(CapabilityEvaluator.Evaluate(snapshot, context.Membership, configuration.EnabledModules));
    }
}
