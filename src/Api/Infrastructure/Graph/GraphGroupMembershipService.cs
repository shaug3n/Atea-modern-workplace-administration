namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphGroupMembershipService(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new AddGroupMemberMutation(groupObjectId, memberObjectId), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new RemoveGroupMemberMutation(groupObjectId, memberObjectId), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}

internal sealed record AddGroupMemberMutation(string GroupObjectId, string MemberObjectId) : JsonGraphMutation(GraphScopeCatalog.GroupMembershipWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => $"/v1.0/groups/{Uri.EscapeDataString(GroupObjectId)}/members/$ref";
    internal override object Body => new Dictionary<string, string>
    {
        ["@odata.id"] = $"https://graph.microsoft.com/v1.0/directoryObjects/{MemberObjectId}"
    };
}

internal sealed record RemoveGroupMemberMutation(string GroupObjectId, string MemberObjectId) : JsonGraphMutation(GraphScopeCatalog.GroupMembershipWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Delete;
    internal override string PathAndQuery => $"/v1.0/groups/{Uri.EscapeDataString(GroupObjectId)}/members/{Uri.EscapeDataString(MemberObjectId)}/$ref";
    internal override object? Body => null;
}
