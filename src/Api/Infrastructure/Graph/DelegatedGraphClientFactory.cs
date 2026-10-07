using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IDelegatedGraphClientFactory
{
    Task<GraphClientLease> CreateForCurrentUserAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken);
}

public sealed record GraphClientLease(
    IGraphTransport Transport,
    IReadOnlyCollection<string> Scopes,
    IAsyncDisposable? OwnedResource = null) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => OwnedResource?.DisposeAsync() ?? ValueTask.CompletedTask;
}

public sealed class DelegatedGraphClientFactory(
    IGraphTokenProvider tokenProvider,
    IHttpClientFactory httpClientFactory,
    ICorrelationContextAccessor correlationContext) : IDelegatedGraphClientFactory
{
    public async Task<GraphClientLease> CreateForCurrentUserAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken)
    {
        if (scopes.Count == 0)
        {
            throw new ArgumentException("At least one Graph delegated scope is required.", nameof(scopes));
        }

        var normalizedScopes = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedScopes.Length == 0)
        {
            throw new ArgumentException("At least one Graph delegated scope is required.", nameof(scopes));
        }

        var accessToken = await tokenProvider.GetAccessTokenForCurrentUserAsync(normalizedScopes, cancellationToken);
        var client = httpClientFactory.CreateClient("MicrosoftGraph");
        if (client.BaseAddress is null)
        {
            client.BaseAddress = new Uri("https://graph.microsoft.com");
        }

        return new GraphClientLease(new GraphHttpTransport(client, accessToken, normalizedScopes, correlationContext: correlationContext.Current), normalizedScopes);
    }
}
