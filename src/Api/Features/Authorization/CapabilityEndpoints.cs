using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Authorization;

public static class CapabilityEndpoints
{
    public static IEndpointRouteBuilder MapCapabilityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/capabilities", GetCapabilitiesAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetCapabilitiesAsync(IWorkspaceContextAccessor accessor, IGraphAuthorizationSnapshotReader reader, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        }

        var snapshot = await reader.ReadAsync(context, cancellationToken);
        return Results.Ok(CapabilityEvaluator.Evaluate(snapshot, context.Membership));
    }
}
