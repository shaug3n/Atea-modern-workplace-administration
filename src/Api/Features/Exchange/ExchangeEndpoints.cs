using System.Globalization;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Exchange;

public static class ExchangeEndpoints
{
    public static IEndpointRouteBuilder MapExchangeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/exchange/mailboxes", ListMailboxesAsync).RequireAuthorization().RequireWorkspaceModule("exchange");
        endpoints.MapGet("/api/exchange/mailboxes/{userObjectId}/overview", GetMailboxOverviewAsync).RequireAuthorization().RequireWorkspaceModule("exchange");
        return endpoints;
    }

    private static async Task<IResult> ListMailboxesAsync(
        IWorkspaceContextAccessor accessor,
        IExchangeService service,
        HttpRequest request,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (!TryReadPageSize(request, out var pageSize))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["pageSize"] = ["pageSize must be an integer between 1 and 100."]
            });
        }

        var query = new ExchangeMailboxListQuery(
            Query(request, "search"),
            pageSize,
            Query(request, "continuationToken"));
        var result = await service.ListAsync(context, query, cancellationToken);
        return result.Error is null
            ? Results.Ok(result.Response)
            : Results.Json(new { error = new { category = SafeCategory(result.Error.Category) } }, statusCode: StatusCodeFor(result.Error));
    }

    private static async Task<IResult> GetMailboxOverviewAsync(
        string userObjectId,
        IWorkspaceContextAccessor accessor,
        IExchangeService service,
        CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        if (string.IsNullOrWhiteSpace(userObjectId))
        {
            return Results.BadRequest(new { error = "user_object_id_required" });
        }

        var result = await service.VerifyAsync(context, userObjectId, cancellationToken);
        return Results.Json(result, statusCode: result.HttpStatusCode);
    }

    private static string? Query(HttpRequest request, string name) =>
        request.Query.TryGetValue(name, out var value) ? value.ToString() : null;

    private static bool TryReadPageSize(HttpRequest request, out int pageSize)
    {
        pageSize = 25;
        if (!request.Query.TryGetValue("pageSize", out var values))
        {
            return true;
        }

        return int.TryParse(values.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out pageSize)
            && pageSize is >= 1 and <= 100;
    }

    private static int StatusCodeFor(GraphOperationResult error) => error.Category switch
    {
        "invalid_continuation" => StatusCodes.Status400BadRequest,
        "not_authorized" or "consent_required" => StatusCodes.Status403Forbidden,
        "not_found" => StatusCodes.Status404NotFound,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static string SafeCategory(string category) => category switch
    {
        "invalid_continuation" => "invalid_continuation",
        "not_authorized" => "forbidden",
        "consent_required" => "consent_required",
        "not_found" => "not_found",
        _ => "unavailable"
    };
}
