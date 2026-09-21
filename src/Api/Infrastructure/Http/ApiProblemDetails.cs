using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Microsoft.AspNetCore.Mvc;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Http;

public static class ApiProblemDetails
{
    public static ProblemDetails Describe(
        string code,
        string? correlationId,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var template = Template(code);
        var problem = new ProblemDetails
        {
            Status = template.Status,
            Title = template.Title,
            Detail = SafeDetail(code, detail) ?? template.Detail,
            Type = $"https://httpstatuses.com/{template.Status}"
        };
        problem.Extensions["code"] = template.Code;
        problem.Extensions["correlationId"] = string.IsNullOrWhiteSpace(correlationId) ? "unavailable" : correlationId;

        if (extensions is not null)
        {
            foreach (var extension in extensions)
            {
                if (extension.Value is not null)
                {
                    problem.Extensions[extension.Key] = extension.Value;
                }
            }
        }

        return problem;
    }

    public static IResult Result(
        string code,
        HttpContext httpContext,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null) =>
        Results.Problem(Describe(code, CorrelationId(httpContext), detail, extensions));

    public static async Task WriteAsync(
        HttpContext httpContext,
        string code,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
    {
        var problem = Describe(code, CorrelationId(httpContext), detail, extensions);
        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        httpContext.Response.ContentType = "application/problem+json";
        if (problem.Extensions.TryGetValue("correlationId", out var correlationId)
            && correlationId is string safeCorrelationId
            && safeCorrelationId != "unavailable")
        {
            httpContext.Response.Headers[CorrelationMiddleware.HeaderName] = safeCorrelationId;
        }
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problem, cancellationToken: httpContext.RequestAborted);
    }

    public static string FromGraphCategory(string? category) => category switch
    {
        "unauthenticated" => ApiProblemCode.AuthenticationRequired,
        "not_authorized" or "mfa_required" => ApiProblemCode.AuthorizationDenied,
        "consent_required" => ApiProblemCode.ConsentRequired,
        "throttled" => ApiProblemCode.Throttled,
        "conflict" => ApiProblemCode.Conflict,
        "not_found" => ApiProblemCode.NotFound,
        _ => ApiProblemCode.TransientGraphFailure
    };

    private static string? CorrelationId(HttpContext httpContext)
    {
        if (httpContext.Items[nameof(CorrelationContext)] is CorrelationContext context)
        {
            return context.CorrelationId;
        }

        return CorrelationMiddleware.SafeCorrelationId(
            httpContext.Request.Headers[CorrelationMiddleware.HeaderName].FirstOrDefault());
    }

    private static string? SafeDetail(string code, string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail) || IsGraphBoundary(code))
        {
            return null;
        }

        return detail;
    }

    private static bool IsGraphBoundary(string code) =>
        code is ApiProblemCode.ConsentRequired
            or ApiProblemCode.Throttled
            or ApiProblemCode.TransientGraphFailure;

    private static ApiProblemTemplate Template(string code) => code switch
    {
        ApiProblemCode.AuthenticationRequired => new(code, StatusCodes.Status401Unauthorized, "Authentication required", "Sign in with a valid access token before calling this API."),
        ApiProblemCode.AuthorizationDenied => new(code, StatusCodes.Status403Forbidden, "Authorization denied", "The signed-in user is not authorized to perform this action."),
        ApiProblemCode.CapabilityRequired => new(code, StatusCodes.Status403Forbidden, "Capability required", "The signed-in user does not have the required workspace capability."),
        ApiProblemCode.ConsentRequired => new(code, StatusCodes.Status403Forbidden, "Microsoft 365 consent required", "Microsoft 365 delegated consent is required before this workspace operation can continue."),
        ApiProblemCode.Throttled => new(code, StatusCodes.Status429TooManyRequests, "Request throttled", "The request was throttled. Retry after the suggested interval or wait before trying again."),
        ApiProblemCode.ValidationFailed => new(code, StatusCodes.Status400BadRequest, "Validation failed", "The request did not pass validation. Correct the input and retry."),
        ApiProblemCode.Conflict => new(code, StatusCodes.Status409Conflict, "Conflict", "The request conflicts with current workspace state. Refresh and retry."),
        ApiProblemCode.NotFound => new(code, StatusCodes.Status404NotFound, "Resource not found", "The requested resource was not found in this workspace."),
        ApiProblemCode.TransientGraphFailure => new(code, StatusCodes.Status503ServiceUnavailable, "Microsoft Graph temporarily unavailable", "The upstream Microsoft Graph request could not be completed. Retry later using the correlation ID."),
        _ => new(ApiProblemCode.TransientGraphFailure, StatusCodes.Status503ServiceUnavailable, "Request failed", "The request could not be completed. Retry later using the correlation ID.")
    };

    private sealed record ApiProblemTemplate(string Code, int Status, string Title, string Detail);
}
