using System.Net;
using System.Net.Http.Headers;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IGraphTransport
{
    IReadOnlyCollection<string> Scopes { get; }

    Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken);
}

public sealed record GraphRequest(
    HttpMethod Method,
    string PathAndQuery,
    HttpContent? Content = null,
    IReadOnlyDictionary<string, string>? Headers = null);

public sealed record GraphTransportResponse(
    GraphOperationResult Result,
    string Content,
    int Attempts,
    IReadOnlyDictionary<string, IReadOnlyCollection<string>> Headers);

public sealed record GraphTransportOptions(int MaxRetries = 2, Func<TimeSpan, CancellationToken, Task>? DelayAsync = null);

public sealed class GraphHttpTransport(
    HttpClient httpClient,
    string accessToken,
    IReadOnlyCollection<string> scopes,
    GraphTransportOptions? options = null,
    CorrelationContext? correlationContext = null) : IGraphTransport
{
    private readonly GraphTransportOptions options = options ?? new GraphTransportOptions();

    public IReadOnlyCollection<string> Scopes { get; } = scopes.ToArray();

    public async Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PathAndQuery);
        var contentSnapshot = request.Content is null
            ? null
            : new ContentSnapshot(
                await request.Content.ReadAsByteArrayAsync(cancellationToken),
                request.Content.Headers.ContentType,
                request.Content.Headers.ToDictionary(header => header.Key, header => header.Value.ToArray()));

        for (var attempt = 1; ; attempt++)
        {
            using var message = new HttpRequestMessage(request.Method, request.PathAndQuery);
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            message.Headers.TryAddWithoutValidation("client-request-id", correlationContext?.CorrelationId ?? Guid.NewGuid().ToString("D"));
            foreach (var header in request.Headers ?? new Dictionary<string, string>())
            {
                message.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            if (contentSnapshot is not null)
            {
                message.Content = contentSnapshot.CreateContent();
            }

            try
            {
                using var response = await httpClient.SendAsync(message, cancellationToken);
                var content = response.Content is null ? string.Empty : await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    return new GraphTransportResponse(
                        GraphOperationResult.Success(HeaderValue(response, "client-request-id"), HeaderValue(response, "request-id")),
                        content,
                        attempt,
                        HeaderDictionary(response));
                }

                var result = GraphErrorMapper.FromResponse(response);
                if (!ShouldRetry(response.StatusCode, attempt))
                {
                    return new GraphTransportResponse(result, content, attempt, HeaderDictionary(response));
                }

                await DelayAsync(result.RetryAfter ?? TimeSpan.FromMilliseconds(200), cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt <= options.MaxRetries + 1)
            {
                if (attempt > options.MaxRetries)
                {
                    return new GraphTransportResponse(GraphErrorMapper.FromException(exception), string.Empty, attempt, new Dictionary<string, IReadOnlyCollection<string>>());
                }

                await DelayAsync(TimeSpan.FromMilliseconds(200), cancellationToken);
            }
        }
    }

    private bool ShouldRetry(HttpStatusCode statusCode, int attempt) =>
        attempt <= options.MaxRetries && (statusCode == (HttpStatusCode)429 || (int)statusCode >= 500);

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (options.DelayAsync is not null)
        {
            return options.DelayAsync(delay, cancellationToken);
        }

        return Task.Delay(delay, cancellationToken);
    }

    private static string? HeaderValue(HttpResponseMessage response, string name) =>
        response.Headers.TryGetValues(name, out var values) ? values.FirstOrDefault() : null;

    private static IReadOnlyDictionary<string, IReadOnlyCollection<string>> HeaderDictionary(HttpResponseMessage response) =>
        response.Headers.ToDictionary(header => header.Key, header => (IReadOnlyCollection<string>)header.Value.ToArray());

    private sealed record ContentSnapshot(
        byte[] Body,
        MediaTypeHeaderValue? ContentType,
        IReadOnlyDictionary<string, string[]> Headers)
    {
        public HttpContent CreateContent()
        {
            var content = new ByteArrayContent(Body);
            content.Headers.ContentType = ContentType;
            foreach (var header in Headers.Where(header => !header.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
            {
                content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }

            return content;
        }
    }
}
