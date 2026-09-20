namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphGroupMembershipService(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}
