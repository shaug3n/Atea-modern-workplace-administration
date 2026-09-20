using System.Net;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Graph;

public sealed class FakeGraphTransport(IReadOnlyCollection<string>? scopes = null) : IGraphTransport
{
    private readonly Queue<GraphTransportResponse> responses = new();

    public IReadOnlyCollection<string> Scopes { get; } = scopes ?? [];
    public List<GraphRequest> Requests { get; } = [];

    public void EnqueueJson(HttpStatusCode statusCode, string content, string correlationId = "correlation-id", string requestId = "request-id")
    {
        var result = statusCode is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
            ? GraphOperationResult.Success(correlationId, requestId)
            : new GraphOperationResult(false, Category(statusCode), (int)statusCode, CorrelationId: correlationId, RequestId: requestId);

        responses.Enqueue(new GraphTransportResponse(
            result,
            content,
            1,
            new Dictionary<string, IReadOnlyCollection<string>>
            {
                ["client-request-id"] = [correlationId],
                ["request-id"] = [requestId]
            }));
    }

    public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        return Task.FromResult(responses.Count > 0
            ? responses.Dequeue()
            : new GraphTransportResponse(GraphOperationResult.Success(), "{}", 1, new Dictionary<string, IReadOnlyCollection<string>>()));
    }

    private static string Category(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Forbidden => "not_authorized",
        HttpStatusCode.NotFound => "not_found",
        HttpStatusCode.Conflict => "conflict",
        (HttpStatusCode)429 => "throttled",
        HttpStatusCode.Unauthorized => "unauthenticated",
        >= HttpStatusCode.InternalServerError => "temporarily_unavailable",
        _ => "temporarily_unavailable"
    };
}

public sealed class FakeDelegatedGraphClientFactory(FakeGraphTransport transport) : IDelegatedGraphClientFactory
{
    public List<IReadOnlyCollection<string>> RequestedScopes { get; } = [];

    public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        RequestedScopes.Add(scopes);
        return Task.FromResult(new GraphClientLease(transport, scopes));
    }
}
