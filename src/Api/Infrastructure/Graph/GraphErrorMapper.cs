using System.Net;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphErrorMapper
{
    public static GraphOperationResult FromResponse(HttpResponseMessage response)
    {
        var statusCode = (int)response.StatusCode;
        var category = ChallengeCategory(response) ?? response.StatusCode switch
        {
            HttpStatusCode.Unauthorized => "unauthenticated",
            HttpStatusCode.Forbidden => "not_authorized",
            HttpStatusCode.NotFound => "not_found",
            HttpStatusCode.Conflict => "conflict",
            (HttpStatusCode)429 => "throttled",
            >= HttpStatusCode.InternalServerError => "temporarily_unavailable",
            _ => "temporarily_unavailable"
        };

        return new GraphOperationResult(
            false,
            category,
            statusCode,
            RetryAfter(response),
            HeaderValue(response, "client-request-id"),
            HeaderValue(response, "request-id"));
    }

    public static GraphOperationResult FromException(HttpRequestException exception) =>
        new(false, "temporarily_unavailable", exception.StatusCode is null ? null : (int)exception.StatusCode);

    private static string? ChallengeCategory(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("WWW-Authenticate", out var values))
        {
            return null;
        }

        var challenge = string.Join(' ', values);
        if (challenge.Contains("consent_required", StringComparison.OrdinalIgnoreCase))
        {
            return "consent_required";
        }

        if (challenge.Contains("mfa_required", StringComparison.OrdinalIgnoreCase)
            || challenge.Contains("interaction_required", StringComparison.OrdinalIgnoreCase)
            || challenge.Contains("polids", StringComparison.OrdinalIgnoreCase))
        {
            return "mfa_required";
        }

        return null;
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is not null)
        {
            return retryAfter.Delta;
        }

        if (retryAfter?.Date is not null)
        {
            var delay = retryAfter.Date.Value - DateTimeOffset.UtcNow;
            return delay <= TimeSpan.Zero ? TimeSpan.Zero : delay;
        }

        return null;
    }

    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;
}
