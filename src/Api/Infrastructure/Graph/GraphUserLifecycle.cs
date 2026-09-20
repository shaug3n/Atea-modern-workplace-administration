using System.Text;
using System.Text.Json;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

internal interface IGraphMutationExecutor
{
    Task<GraphOperationResult> ExecuteAsync(
        GraphMutation mutation,
        string idempotencyKey,
        CancellationToken cancellationToken);
}

internal abstract record GraphMutation(IReadOnlyCollection<string> Scopes)
{
    internal abstract GraphRequest CreateRequest(string idempotencyKey);
}

public sealed class GraphUserLifecycle(IDelegatedGraphClientFactory clientFactory) : IGraphMutationExecutor
{
    public Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new CreateUserMutation(request), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new UpdateUserProfileMutation(userObjectId, update), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new SetAccountEnabledMutation(userObjectId, accountEnabled), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> ResetPasswordAsync(string userObjectId, TemporaryPasswordProfile passwordProfile, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new ResetPasswordMutation(userObjectId, passwordProfile), idempotencyKey, cancellationToken);

    Task<GraphOperationResult> IGraphMutationExecutor.ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(mutation, idempotencyKey, cancellationToken);

    private Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);
}

public sealed record GraphUserCreateRequest(
    string DisplayName,
    string UserPrincipalName,
    string MailNickname,
    TemporaryPasswordProfile PasswordProfile,
    bool AccountEnabled = true);

public sealed record GraphUserProfileUpdate(string? DisplayName = null, string? Mail = null, string? Department = null, string? JobTitle = null);

public sealed record TemporaryPasswordProfile(string TemporaryPassword, bool ForceChangePasswordNextSignIn = true);

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
        var response = await lease.Transport.SendAsync(mutation.CreateRequest(idempotencyKey), cancellationToken);

        return response.Result;
    }
}

internal abstract record JsonGraphMutation(IReadOnlyCollection<string> Scopes) : GraphMutation(Scopes)
{
    internal abstract HttpMethod Method { get; }
    internal abstract string PathAndQuery { get; }
    internal abstract object? Body { get; }

    internal override GraphRequest CreateRequest(string idempotencyKey) => new(
        Method,
        PathAndQuery,
        Body is null ? null : new StringContent(JsonSerializer.Serialize(Body), Encoding.UTF8, "application/json"),
        new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey });
}

internal sealed record CreateUserMutation(GraphUserCreateRequest Request) : JsonGraphMutation(GraphScopeCatalog.UserCreateScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => "/v1.0/users";
    internal override object Body => new
    {
        accountEnabled = Request.AccountEnabled,
        displayName = Request.DisplayName,
        mailNickname = Request.MailNickname,
        userPrincipalName = Request.UserPrincipalName,
        passwordProfile = new
        {
            password = Request.PasswordProfile.TemporaryPassword,
            forceChangePasswordNextSignIn = Request.PasswordProfile.ForceChangePasswordNextSignIn
        }
    };
}

internal sealed record UpdateUserProfileMutation(string UserObjectId, GraphUserProfileUpdate Update) : JsonGraphMutation(GraphScopeCatalog.UserProfileWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Patch;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}";
    internal override object Body => new
    {
        displayName = Update.DisplayName,
        mail = Update.Mail,
        department = Update.Department,
        jobTitle = Update.JobTitle
    };
}

internal sealed record SetAccountEnabledMutation(string UserObjectId, bool AccountEnabled) : JsonGraphMutation(GraphScopeCatalog.UserAccountWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Patch;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}";
    internal override object Body => new { accountEnabled = AccountEnabled };
}

internal sealed record ResetPasswordMutation(string UserObjectId, TemporaryPasswordProfile PasswordProfile) : JsonGraphMutation(GraphScopeCatalog.UserPasswordWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Patch;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}";
    internal override object Body => new
    {
        passwordProfile = new
        {
            password = PasswordProfile.TemporaryPassword,
            forceChangePasswordNextSignIn = PasswordProfile.ForceChangePasswordNextSignIn
        }
    };
}
