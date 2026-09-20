namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphRoleAndPimService(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> AssignDirectoryRoleAsync(
        string roleDefinitionId,
        string principalId,
        string directoryScopeId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new AssignDirectoryRoleMutation(roleDefinitionId, principalId, directoryScopeId), idempotencyKey, cancellationToken);

    Task<GraphOperationResult> IGraphMutationExecutor.ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(mutation, idempotencyKey, cancellationToken);

    private Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}

internal sealed record AssignDirectoryRoleMutation(
    string RoleDefinitionId,
    string PrincipalId,
    string DirectoryScopeId) : JsonGraphMutation(GraphScopeCatalog.RoleAndPimScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => "/v1.0/roleManagement/directory/roleAssignments";
    internal override object Body => new
    {
        roleDefinitionId = RoleDefinitionId,
        principalId = PrincipalId,
        directoryScopeId = DirectoryScopeId
    };
}
