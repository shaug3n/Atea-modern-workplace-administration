using System.Text;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IGraphMutationExecutor
{
    Task<GraphOperationResult> ExecuteAsync(
        GraphMutation mutation,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

public sealed record GraphMutation(
    HttpMethod Method,
    string PathAndQuery,
    string? JsonBody,
    IReadOnlyCollection<string> Scopes);

public sealed class GraphUserLifecycle(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}

internal static class GraphMutationExecutor
{
    public static async Task<GraphOperationResult> ExecuteAsync(
        IDelegatedGraphClientFactory clientFactory,
        GraphMutation mutation,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (mutation.Scopes.Count == 0)
        {
            throw new ArgumentException("Mutating Graph operations require explicit delegated scopes.", nameof(mutation));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        await using var lease = await clientFactory.CreateForCurrentUserAsync(mutation.Scopes, cancellationToken);
        using var content = mutation.JsonBody is null ? null : new StringContent(mutation.JsonBody, Encoding.UTF8, "application/json");
        var response = await lease.Transport.SendAsync(new GraphRequest(
            mutation.Method,
            mutation.PathAndQuery,
            content,
            new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }),
            cancellationToken);

        return response.Result;
    }
}
