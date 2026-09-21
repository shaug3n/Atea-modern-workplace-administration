using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

public sealed record CorrelationContext(string CorrelationId);

public interface ICorrelationContextAccessor
{
    CorrelationContext? Current { get; set; }
}

public sealed class CorrelationContextAccessor : ICorrelationContextAccessor
{
    private static readonly AsyncLocal<CorrelationContext?> CurrentContext = new();
    public CorrelationContext? Current
    {
        get => CurrentContext.Value;
        set => CurrentContext.Value = value;
    }
}

public sealed partial class CorrelationMiddleware(RequestDelegate next, ICorrelationContextAccessor accessor, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext httpContext)
    {
        var correlationId = SafeCorrelationId(httpContext.Request.Headers[HeaderName].FirstOrDefault());
        var context = new CorrelationContext(correlationId);
        accessor.Current = context;
        httpContext.Items[nameof(CorrelationContext)] = context;
        httpContext.Response.OnStarting(() =>
        {
            httpContext.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using var activity = new Activity("http.request");
        activity.SetTag("correlation.id", correlationId);
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            try
            {
                await next(httpContext);
            }
            finally
            {
                accessor.Current = null;
            }
        }
    }

    public static string SafeCorrelationId(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate)
            && candidate.Length <= 100
            && SafeCorrelationIdRegex().IsMatch(candidate))
        {
            return candidate;
        }

        return Guid.NewGuid().ToString("N");
    }

    [GeneratedRegex("^[A-Za-z0-9_.-]{16,100}$")]
    private static partial Regex SafeCorrelationIdRegex();
}
