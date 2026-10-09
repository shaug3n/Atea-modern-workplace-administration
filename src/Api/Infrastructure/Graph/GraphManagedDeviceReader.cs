using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IManagedDeviceReader
{
    Task<GraphReadResult<PagedResult<ManagedDeviceSummary>>> ReadAsync(DeviceSearchQuery query, CancellationToken cancellationToken);
    Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken);
}

public interface IManagedDeviceDetailReader
{
    Task<GraphReadResult<ManagedDeviceSummary?>> GetAsync(string deviceObjectId, CancellationToken cancellationToken);
}

public sealed class GraphManagedDeviceReader(IDelegatedGraphClientFactory clientFactory) : IManagedDeviceReader, IManagedDeviceDetailReader
{
    private const string Select = "id,deviceName,operatingSystem,osVersion,complianceState,managementState,managedDeviceOwnerType,lastSyncDateTime,userId,azureADDeviceId,serialNumber,manufacturer,model";
    private const string DetailSelect = Select + ",userDisplayName,userPrincipalName,isEncrypted";

    public async Task<GraphReadResult<PagedResult<ManagedDeviceSummary>>> ReadAsync(DeviceSearchQuery query, CancellationToken cancellationToken) =>
        (await ReadPageAsync(query, cancellationToken)).Result;

    public async Task<GraphReadResult<ManagedDeviceSummary?>> GetAsync(string deviceObjectId, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(deviceObjectId))
            return GraphReadResult<ManagedDeviceSummary?>.Failed(new GraphOperationResult(false, "invalid_target"));
        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DeviceReadScopes, cancellationToken);
            var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get,
                $"/v1.0/deviceManagement/managedDevices/{Uri.EscapeDataString(deviceObjectId)}?$select={DetailSelect}"), cancellationToken);
            if (response.Result.Category == "not_found") return GraphReadResult<ManagedDeviceSummary?>.Succeeded(null);
            if (!response.Result.IsSuccess) return GraphReadResult<ManagedDeviceSummary?>.Failed(response.Result);
            try
            {
                using var document = JsonDocument.Parse(response.Content);
                var device = Map(document.RootElement);
                return string.Equals(device.Id, deviceObjectId, StringComparison.OrdinalIgnoreCase)
                    ? GraphReadResult<ManagedDeviceSummary?>.Succeeded(device)
                    : GraphReadResult<ManagedDeviceSummary?>.Failed(new GraphOperationResult(false, "invalid_response"));
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return GraphReadResult<ManagedDeviceSummary?>.Failed(new GraphOperationResult(false, "invalid_response"));
            }
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return GraphReadResult<ManagedDeviceSummary?>.Failed(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return GraphReadResult<ManagedDeviceSummary?>.Failed(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
    }

    public async Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken)
    {
        var page = await ReadPageAsync(new DeviceSearchQuery(PageSize: 100, UserObjectId: userObjectId), cancellationToken);
        return page.Result.Error is null
            ? GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Succeeded(page.Result.Value.Items)
            : GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Failed(MapAssociationError(page.Result.Error, page.ResponseContent));
    }

    private async Task<ReadPageResult> ReadPageAsync(DeviceSearchQuery query, CancellationToken cancellationToken)
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
            return new(GraphReadResult<PagedResult<ManagedDeviceSummary>>.Failed(result), response.Content);
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
            return new(GraphReadResult<PagedResult<ManagedDeviceSummary>>.Succeeded(new PagedResult<ManagedDeviceSummary>(items, [], continuationLink)), null);
        }
        catch (JsonException)
        {
            return new(GraphReadResult<PagedResult<ManagedDeviceSummary>>.Failed(new GraphOperationResult(false, "invalid_response")), response.Content);
        }
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
        Optional(element, "model"),
        Optional(element, "userDisplayName"),
        Optional(element, "userPrincipalName"),
        OptionalBoolean(element, "isEncrypted"));

    private static string EscapeOData(string value) => value.Trim().Replace("'", "''", StringComparison.Ordinal);
    private static GraphOperationResult MapAssociationError(GraphOperationResult error, string? responseContent) =>
        IsUnsupportedAssociationFilter(responseContent)
            ? error with { Category = "association_query_unsupported" }
            : error;
    private static bool IsUnsupportedAssociationFilter(string? content) =>
        !string.IsNullOrWhiteSpace(content)
        && content.Contains("filter", StringComparison.OrdinalIgnoreCase)
        && (content.Contains("not supported", StringComparison.OrdinalIgnoreCase)
            || content.Contains("unsupported", StringComparison.OrdinalIgnoreCase));
    private static bool IsTargetTenantNotApplicable(string content) =>
        content.Contains("Request not applicable to target tenant", StringComparison.OrdinalIgnoreCase);
    private static string? NormalizeGraphPath(string? nextLink) =>
        Uri.TryCreate(nextLink, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp)
            ? uri.PathAndQuery
            : nextLink;
    private static string Required(JsonElement element, string property) => Optional(element, property) ?? string.Empty;
    private static string? Optional(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static bool? OptionalBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;
    private static DateTimeOffset? OptionalDate(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var result) ? result : null;

    private sealed record ReadPageResult(
        GraphReadResult<PagedResult<ManagedDeviceSummary>> Result,
        string? ResponseContent);
}
