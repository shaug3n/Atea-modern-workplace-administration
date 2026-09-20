using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IUserLicenseReader
{
    Task<GraphReadResult<IReadOnlyList<AssignedLicense>>> ReadUserLicensesAsync(string userObjectId, CancellationToken cancellationToken);
}

public sealed class GraphLicenseService(IDelegatedGraphClientFactory clientFactory) : IUserLicenseReader, IGraphMutationExecutor
{
    async Task<GraphReadResult<IReadOnlyList<AssignedLicense>>> IUserLicenseReader.ReadUserLicensesAsync(string userObjectId, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/licenseDetails?$select=skuId,skuPartNumber"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyList<AssignedLicense>>.Failed(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var licenses = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(MapLicense).ToArray()
            : [];
        return GraphReadResult<IReadOnlyList<AssignedLicense>>.Succeeded(licenses);
    }

    public Task<GraphOperationResult> AssignUserLicensesAsync(
        string userObjectId,
        IReadOnlyCollection<string> addSkuIds,
        IReadOnlyCollection<string> removeSkuIds,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new AssignUserLicensesMutation(userObjectId, addSkuIds, removeSkuIds), idempotencyKey, cancellationToken);

    Task<GraphOperationResult> IGraphMutationExecutor.ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(mutation, idempotencyKey, cancellationToken);

    private Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);

    private static AssignedLicense MapLicense(JsonElement element) =>
        new(
            RequiredString(element, "skuId"),
            OptionalString(element, "skuPartNumber"),
            OptionalString(element, "skuPartNumber"));

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
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
