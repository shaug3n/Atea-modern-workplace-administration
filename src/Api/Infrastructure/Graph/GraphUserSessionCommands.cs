using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IUserSessionCommands
{
    Task<GraphOperationResult> RevokeAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class GraphUserSessionCommands(IDelegatedGraphClientFactory clientFactory) : IUserSessionCommands
{
    public Task<GraphOperationResult> RevokeAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!UserSessionCommandValidation.IsValidTarget(userObjectId) || string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Task.FromResult(new GraphOperationResult(false, "invalid_request"));
        }

        return GraphMutationExecutor.ExecuteAsync(clientFactory, new RevokeUserSessionsMutation(userObjectId), idempotencyKey, cancellationToken);
    }
}

internal sealed record RevokeUserSessionsMutation(string UserObjectId) : JsonGraphMutation(GraphScopeCatalog.UserSessionWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}/revokeSignInSessions";
    internal override object? Body => null;
}
