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

public sealed record GraphReadResult<T>(T Value, GraphOperationResult? Error, string? CorrelationId = null, string? RequestId = null)
{
    public static GraphReadResult<T> Succeeded(T value, string? correlationId = null, string? requestId = null) => new(value, null, correlationId, requestId);
    public static GraphReadResult<T> Failed(GraphOperationResult error) => new(default!, error);
}
