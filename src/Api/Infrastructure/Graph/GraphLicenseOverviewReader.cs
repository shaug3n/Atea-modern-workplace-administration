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
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, "/v1.0/subscribedSkus?$select=skuId,skuPartNumber,consumedUnits,prepaidUnits"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(response.Result);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var items = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(MapLicense).ToArray()
                : [];
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
        var partNumber = OptionalString(element, "skuPartNumber") ?? string.Empty;
        var assigned = OptionalInt(element, "consumedUnits");
        var enabled = element.TryGetProperty("prepaidUnits", out var prepaid)
            ? OptionalInt(prepaid, "enabled")
            : 0;

        return new LicenseOverviewItem(
            RequiredString(element, "skuId"),
            partNumber,
            OptionalString(element, "displayName") ?? partNumber,
            assigned,
            Math.Max(enabled - assigned, 0));
    }

    private static string RequiredString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static int OptionalInt(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.TryGetInt32(out var result) ? result : 0;
}
