using Microsoft.AspNetCore.Http;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

public static class HealthEndpoints
{
    private static readonly TimeSpan DatabaseProbeTimeout = TimeSpan.FromSeconds(3);

    public static async Task<IResult> CheckDatabaseReadinessAsync(Func<CancellationToken, Task<bool>> canConnect, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(DatabaseProbeTimeout);
        try
        {
            if (await canConnect(timeout.Token)) return Results.Json(new { status = "ready" });
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // An unhealthy dependency must fail readiness promptly instead of hanging the probe.
        }
        catch (Exception)
        {
            // Provider errors can include infrastructure details; expose only the dependency category.
        }
        return Results.Json(new { status = "unavailable", dependency = "database" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
}
