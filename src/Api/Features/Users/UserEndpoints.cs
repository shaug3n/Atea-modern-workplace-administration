using System.Globalization;
using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserEndpoints
{
    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users", SearchUsersAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> SearchUsersAsync(
        IWorkspaceContextAccessor accessor,
        IUserQueryService service,
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
            var request = new UserSearchRequest(
                Query(httpRequest, "search"),
                PageSize(httpRequest),
                Query(httpRequest, "continuationToken"),
                Query(httpRequest, "accountStatus"),
                Query(httpRequest, "tenantRole"),
                Query(httpRequest, "license"),
                Query(httpRequest, "userType"));

            return Results.Ok(await service.SearchAsync(context, request, cancellationToken));
        }
        catch (UserSearchValidationException exception)
        {
            if (exception.Category == "unsupported_filter")
            {
                return Results.BadRequest(new
                {
                    error = new UserDirectoryError(exception.Category, exception.Message, Field: exception.Field)
                });
            }

            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                [exception.Field ?? "query"] = [exception.Message]
            });
        }
    }

    private static string? Query(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    private static int PageSize(HttpRequest request)
    {
        if (!request.Query.TryGetValue("pageSize", out var values))
        {
            return 25;
        }

        var value = values.ToString();
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var pageSize))
        {
            throw new UserSearchValidationException("pageSize must be an integer between 1 and 100.", field: "pageSize");
        }

        return pageSize;
    }
}
