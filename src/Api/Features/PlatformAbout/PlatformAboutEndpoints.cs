using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Atea.UnifiedWorkplace.Api.Features.PlatformAbout;

public static class PlatformAboutEndpoints
{
    public static IEndpointRouteBuilder MapPlatformAboutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/about/system-versions", (PlatformBuildMetadata metadata) => Results.Ok(metadata))
            .RequireAuthorization()
            .RequireWorkspaceModule("about")
            .RequireCapability(Capability.PlatformAboutView);
        return endpoints;
    }
}
