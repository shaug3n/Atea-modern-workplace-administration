using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface ILicenseOverviewReader
{
    Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(
        WorkspaceContext context,
        LicenseOverviewQuery query,
        CancellationToken cancellationToken);
}

public sealed class GraphLicenseOverviewReader(IDelegatedGraphClientFactory clientFactory) : ILicenseOverviewReader
{
    public async Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(
        WorkspaceContext context,
        LicenseOverviewQuery query,
        CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(["Directory.Read.All"], cancellationToken);
        try
        {
            var items = new List<LicenseOverviewItem>();
            string? path = "/v1.0/subscribedSkus?$select=skuId,skuPartNumber,consumedUnits,prepaidUnits";
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (path is not null)
            {
                if (!seen.Add(path)) return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(new GraphOperationResult(false, "invalid_response"));
                var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
                if (!response.Result.IsSuccess) return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(response.Result);
                using var document = JsonDocument.Parse(response.Content);
                if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array)
                    return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(new GraphOperationResult(false, "invalid_response"));
                items.AddRange(value.EnumerateArray().Select(MapLicense));
                path = ContinuationPath(document.RootElement);
            }
            return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded(items);
        }
        catch (JsonException)
        {
            return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(new GraphOperationResult(false, "invalid_response"));
        }
        catch (InvalidOperationException)
        {
            return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(new GraphOperationResult(false, "invalid_response"));
        }
    }

    private static LicenseOverviewItem MapLicense(JsonElement element)
    {
        var skuId = RequiredString(element, "skuId");
        var partNumber = OptionalString(element, "skuPartNumber") ?? skuId;
        var assigned = RequiredInt(element, "consumedUnits");
        if (!element.TryGetProperty("prepaidUnits", out var prepaid) || prepaid.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("Subscribed SKU prepaid units are missing.");
        var enabled = RequiredInt(prepaid, "enabled");

        return new LicenseOverviewItem(
            skuId,
            partNumber,
            LicenseDisplayNameResolver.Resolve(partNumber) ?? partNumber,
            assigned,
            Math.Max(enabled - assigned, 0),
            enabled);
    }

    internal static string? ContinuationPath(JsonElement root)
    {
        if (!root.TryGetProperty("@odata.nextLink", out var next)) return null;
        var link = next.GetString();
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "graph.microsoft.com"
            || !uri.PathAndQuery.StartsWith("/v1.0/subscribedSkus?", StringComparison.Ordinal))
            throw new InvalidOperationException("Invalid subscribed SKU continuation.");
        return uri.PathAndQuery;
    }

    private static string RequiredString(JsonElement element, string property) =>
        OptionalString(element, property) is { Length: > 0 } value ? value : throw new InvalidOperationException($"Subscribed SKU {property} is missing.");

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static int RequiredInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var count) && count >= 0
            ? count : throw new InvalidOperationException($"Subscribed SKU {property} is invalid.");
}
