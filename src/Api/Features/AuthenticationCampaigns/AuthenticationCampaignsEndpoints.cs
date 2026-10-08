using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public static class AuthenticationCampaignsEndpoints
{
    public static IEndpointRouteBuilder MapAuthenticationCampaignsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/authentication-campaigns/registrations", GetRegistrationsAsync)
            .RequireAuthorization()
            .RequireWorkspaceModule("authentication-campaigns")
            .RequireCapability(Capability.AuthenticationCampaignsView);
        return endpoints;
    }

    private static async Task<IResult> GetRegistrationsAsync(
        IWorkspaceContextAccessor accessor,
        IAuthenticationCampaignsService service,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var result = await service.ReadAsync(context, cancellationToken);
        if (result.Error is null && result.Response is not null)
        {
            return Results.Ok(result.Response);
        }

        var category = SafeCategory(result.Error?.Category ?? "invalid_response");
        return Results.Json(new { error = new { category } }, statusCode: StatusCodeFor(result.Error?.Category ?? "invalid_response"));
    }

    private static int StatusCodeFor(string category) => category switch
    {
        "not_authorized" or "consent_required" => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string SafeCategory(string category) => category switch
    {
        "not_authorized" => "forbidden",
        "consent_required" => "consent_required",
        "invalid_response" => "invalid_response",
        _ => "unavailable"
    };
}
