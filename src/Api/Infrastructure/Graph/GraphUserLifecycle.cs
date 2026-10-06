using System.Text;
using System.Text.Json;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

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

public interface IUserLifecycleCommands
{
    Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphOperationResult> ResetPasswordAsync(string userObjectId, TemporaryPasswordProfile passwordProfile, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class GraphUserLifecycle(IDelegatedGraphClientFactory clientFactory) : IUserLifecycleCommands, IGraphMutationExecutor
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
    string GivenName,
    string Surname,
    string UserPrincipalName,
    string MailNickname,
    string? JobTitle,
    string? Department,
    string? OfficeLocation,
    string? MobilePhone,
    string UsageLocation,
    TemporaryPasswordProfile PasswordProfile,
    bool AccountEnabled = true);

public sealed record GraphUserProfileUpdate(
    string? DisplayName = null,
    string? GivenName = null,
    string? Surname = null,
    string? JobTitle = null,
    string? Department = null,
    string? OfficeLocation = null,
    string? MobilePhone = null,
    string? UsageLocation = null,
    bool? AccountEnabled = null);

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

        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(mutation.Scopes, cancellationToken);
            var response = await lease.Transport.SendAsync(mutation.CreateRequest(idempotencyKey), cancellationToken);
            return response.Result;
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return GraphTokenAcquisitionErrorMapper.Map(exception);
        }
        catch (MsalUiRequiredException exception)
        {
            return GraphTokenAcquisitionErrorMapper.Map(exception);
        }
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
        givenName = Request.GivenName,
        surname = Request.Surname,
        mailNickname = Request.MailNickname,
        userPrincipalName = Request.UserPrincipalName,
        jobTitle = Request.JobTitle,
        department = Request.Department,
        officeLocation = Request.OfficeLocation,
        mobilePhone = Request.MobilePhone,
        usageLocation = Request.UsageLocation,
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
        givenName = Update.GivenName,
        surname = Update.Surname,
        department = Update.Department,
        jobTitle = Update.JobTitle,
        officeLocation = Update.OfficeLocation,
        mobilePhone = Update.MobilePhone,
        usageLocation = Update.UsageLocation,
        accountEnabled = Update.AccountEnabled
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
