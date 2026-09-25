using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using System.Globalization;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses;

public static class LicenseEndpoints
{
    public static IEndpointRouteBuilder MapLicenseEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/licenses", GetOverviewAsync).RequireAuthorization().RequireWorkspaceModule("licenses");
        endpoints.MapGet("/api/licenses/{skuId}/assignees", GetAssigneesAsync).RequireAuthorization().RequireWorkspaceModule("licenses");
        endpoints.MapGet("/api/users/{userObjectId}/licenses", GetUserLicensesAsync).RequireAuthorization().RequireWorkspaceModule("licenses");
        return endpoints;
    }

    private static async Task<IResult> GetAssigneesAsync(string skuId, IWorkspaceContextAccessor accessor, ILicenseAssigneeService service, HttpRequest request, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.Forbid();
        try
        {
            return Results.Ok(await service.SearchAsync(context, skuId, PageSize(request), Query(request, "continuationToken"), cancellationToken));
        }
        catch (Atea.UnifiedWorkplace.Api.Features.Users.UserSearchValidationException exception)
        {
            return Results.BadRequest(new { error = "invalid_query", message = exception.Message });
        }
        catch (LicenseOverviewValidationException exception)
        {
            return Results.BadRequest(new { error = "invalid_query", message = exception.Message });
        }
    }

    private static async Task<IResult> GetOverviewAsync(
        IWorkspaceContextAccessor accessor,
        ILicenseOverviewService service,
        HttpRequest httpRequest,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        try
        {
            var request = new LicenseOverviewRequest(
                Query(httpRequest, "search"),
                PageSize(httpRequest),
                Page(httpRequest),
                Query(httpRequest, "filter"));
            return Results.Ok(await service.GetAsync(context, request, cancellationToken));
        }
        catch (LicenseOverviewValidationException exception) when (exception.Category == "unsupported_filter")
        {
            return Results.BadRequest(new
            {
                error = new LicenseOverviewError(exception.Category, exception.Message, Field: exception.Field)
            });
        }
        catch (LicenseOverviewValidationException exception)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.Field ?? "query"] = [exception.Message]
            });
        }
    }

    private static async Task<IResult> GetUserLicensesAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IUserDetailService service,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var section = await service.GetLicensesAsync(context, userObjectId, cancellationToken);
        return section is null ? Results.NotFound(new { error = "user_not_found" }) : Results.Ok(section);
    }

    private static string? Query(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    private static int PageSize(HttpRequest request)
    {
        if (!request.Query.TryGetValue("pageSize", out var values))
        {
            return 25;
        }

        return int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize)
            ? pageSize
            : throw new LicenseOverviewValidationException("pageSize must be an integer between 1 and 100.", field: "pageSize");
    }

    private static int Page(HttpRequest request)
    {
        if (!request.Query.TryGetValue("page", out var values))
        {
            return 1;
        }

        return int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out var page)
            ? page
            : throw new LicenseOverviewValidationException("page must be an integer greater than or equal to 1.", field: "page");
    }
}
