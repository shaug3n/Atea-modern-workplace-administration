namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphLicenseService(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> AssignUserLicensesAsync(
        string userObjectId,
        IReadOnlyCollection<string> addSkuIds,
        IReadOnlyCollection<string> removeSkuIds,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new AssignUserLicensesMutation(userObjectId, addSkuIds, removeSkuIds), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}

internal sealed record AssignUserLicensesMutation(
    string UserObjectId,
    IReadOnlyCollection<string> AddSkuIds,
    IReadOnlyCollection<string> RemoveSkuIds) : JsonGraphMutation(GraphScopeCatalog.LicenseWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}/assignLicense";
    internal override object Body => new
    {
        addLicenses = AddSkuIds.Select(skuId => new { skuId }).ToArray(),
        removeLicenses = RemoveSkuIds.ToArray()
    };
}
