using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Devices;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IManagedDeviceReader
{
    Task<GraphReadResult<PagedResult<ManagedDeviceSummary>>> ReadAsync(DeviceSearchQuery query, CancellationToken cancellationToken);
    Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken);
}

public sealed class GraphManagedDeviceReader(IDelegatedGraphClientFactory clientFactory) : IManagedDeviceReader
{
    private const string Select = "id,deviceName,operatingSystem,osVersion,complianceState,managementState,managedDeviceOwnerType,lastSyncDateTime,userId,azureADDeviceId,serialNumber,manufacturer,model";

    public async Task<GraphReadResult<PagedResult<ManagedDeviceSummary>>> ReadAsync(DeviceSearchQuery query, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DeviceReadScopes, cancellationToken);
        var path = query.ContinuationPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            var parameters = new List<string> { $"$select={Select}", $"$top={query.PageSize}" };
            var filters = new List<string>();
            if (!string.IsNullOrWhiteSpace(query.UserObjectId))
            {
                filters.Add($"userId eq '{EscapeOData(query.UserObjectId)}'");
            }
            else if (!string.IsNullOrWhiteSpace(query.Search))
            {
                var search = EscapeOData(query.Search);
                filters.Add($"contains(deviceName,'{search}')");
            }

            if (string.IsNullOrWhiteSpace(query.UserObjectId))
            {
                if (!string.IsNullOrWhiteSpace(query.ComplianceState)) filters.Add($"complianceState eq '{EscapeOData(query.ComplianceState)}'");
                if (!string.IsNullOrWhiteSpace(query.OperatingSystem)) filters.Add($"operatingSystem eq '{EscapeOData(query.OperatingSystem)}'");
            }
            if (filters.Count > 0) parameters.Add($"%24filter={Uri.EscapeDataString(string.Join(" and ", filters))}");
            path = $"/v1.0/deviceManagement/managedDevices?{string.Join("&", parameters)}";
        }

        var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
        if (!response.Result.IsSuccess)
        {
            var result = IsTargetTenantNotApplicable(response.Content)
                ? response.Result with { Category = "not_provisioned" }
                : response.Result;
            return GraphReadResult<PagedResult<ManagedDeviceSummary>>.Failed(result);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var items = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(Map).ToArray()
                : [];
            var continuationLink = document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
                ? NormalizeGraphPath(nextLink.GetString())
                : null;
            return GraphReadResult<PagedResult<ManagedDeviceSummary>>.Succeeded(new PagedResult<ManagedDeviceSummary>(items, [], continuationLink));
        }
        catch (JsonException)
        {
            return GraphReadResult<PagedResult<ManagedDeviceSummary>>.Failed(new GraphOperationResult(false, "invalid_response"));
        }
    }

    public async Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken)
    {
        var page = await ReadAsync(new DeviceSearchQuery(PageSize: 100, UserObjectId: userObjectId), cancellationToken);
        return page.Error is null
            ? GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Succeeded(page.Value.Items)
            : GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Failed(MapAssociationError(page.Error));
    }

    private static ManagedDeviceSummary Map(JsonElement element) => new(
        Required(element, "id"),
        Optional(element, "deviceName"),
        Optional(element, "operatingSystem"),
        Optional(element, "osVersion"),
        Optional(element, "complianceState"),
        Optional(element, "managementState"),
        Optional(element, "managedDeviceOwnerType"),
        OptionalDate(element, "lastSyncDateTime"),
        Optional(element, "userId"),
        Optional(element, "azureADDeviceId"),
        Optional(element, "serialNumber"),
        Optional(element, "manufacturer"),
        Optional(element, "model"));

    private static string EscapeOData(string value) => value.Trim().Replace("'", "''", StringComparison.Ordinal);
    private static GraphOperationResult MapAssociationError(GraphOperationResult error) =>
        error.StatusCode == 400 ? error with { Category = "association_query_unsupported" } : error;
    private static bool IsTargetTenantNotApplicable(string content) =>
        content.Contains("Request not applicable to target tenant", StringComparison.OrdinalIgnoreCase);
    private static string? NormalizeGraphPath(string? nextLink) =>
        Uri.TryCreate(nextLink, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.PathAndQuery
            : nextLink;
    private static string Required(JsonElement element, string property) => Optional(element, property) ?? string.Empty;
    private static string? Optional(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static DateTimeOffset? OptionalDate(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var result) ? result : null;
}
