using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Features.Groups;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IGroupMembershipReader
{
    Task<GraphReadResult<IReadOnlyList<GroupMembership>>> ReadUserGroupsAsync(string userObjectId, CancellationToken cancellationToken);
}
public interface IGroupCatalogReader
{
    Task<GraphReadResult<IReadOnlyList<GroupCatalogItem>>> ReadGroupsAsync(string? search, int pageSize, CancellationToken cancellationToken);
}

public interface IGroupMembershipCommands
{
    Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class GraphGroupMembershipService(IDelegatedGraphClientFactory clientFactory) : IGroupMembershipReader, IGroupCatalogReader, IGroupMembershipCommands, IGraphMutationExecutor
{
    public async Task<GraphReadResult<IReadOnlyList<GroupCatalogItem>>> ReadGroupsAsync(string? search, int pageSize, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var filter = string.IsNullOrWhiteSpace(search) ? string.Empty : $"&$filter=startswith(displayName,'{search.Replace("'", "''")}')";
        var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, $"/v1.0/groups?$select=id,displayName,mailNickname,securityEnabled,groupTypes&$top={pageSize}{filter}"), cancellationToken);
        if (!response.Result.IsSuccess) return GraphReadResult<IReadOnlyList<GroupCatalogItem>>.Failed(response.Result);
        using var document = JsonDocument.Parse(response.Content);
        var items = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(element => new GroupCatalogItem(RequiredString(element, "id"), OptionalString(element, "displayName"), OptionalString(element, "mailNickname"), OptionalBool(element, "securityEnabled"), StringArray(element, "groupTypes"))).Where(item => item.Id.Length > 0).ToArray()
            : [];
        return GraphReadResult<IReadOnlyList<GroupCatalogItem>>.Succeeded(items);
    }
    async Task<GraphReadResult<IReadOnlyList<GroupMembership>>> IGroupMembershipReader.ReadUserGroupsAsync(string userObjectId, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/memberOf/microsoft.graph.group?$select=id,displayName,mailNickname,securityEnabled,groupTypes"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyList<GroupMembership>>.Failed(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var groups = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(MapGroup).ToArray()
            : [];
        return GraphReadResult<IReadOnlyList<GroupMembership>>.Succeeded(groups);
    }

    public Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new AddGroupMemberMutation(groupObjectId, memberObjectId), idempotencyKey, cancellationToken);

    public Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(new RemoveGroupMemberMutation(groupObjectId, memberObjectId), idempotencyKey, cancellationToken);

    Task<GraphOperationResult> IGraphMutationExecutor.ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(mutation, idempotencyKey, cancellationToken);

    private Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);

    private static GroupMembership MapGroup(JsonElement element) =>
        new(
            RequiredString(element, "id"),
            OptionalString(element, "displayName"),
            OptionalString(element, "mailNickname"),
            OptionalBool(element, "securityEnabled"),
            StringArray(element, "groupTypes"));

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static bool? OptionalBool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static IReadOnlyList<string> StringArray(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(item => item.GetString()).Where(item => !string.IsNullOrWhiteSpace(item)).Select(item => item!).ToArray()
            : [];
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
