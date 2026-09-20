namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed record GraphOperationResult(
    bool IsSuccess,
    string Category,
    int? StatusCode = null,
    TimeSpan? RetryAfter = null,
    string? CorrelationId = null,
    string? RequestId = null)
{
    public static GraphOperationResult Success(string? correlationId = null, string? requestId = null) =>
        new(true, "success", CorrelationId: correlationId, RequestId: requestId);
}
