using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphDeviceRecoveryReader(IDelegatedGraphClientFactory clientFactory) : IGraphDeviceRecoveryReader
{
    public async Task<GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>> ListBitlockerAsync(string entraDeviceId, bool secretScope, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(entraDeviceId)) return Invalid<IReadOnlyList<BitlockerRecoveryMetadata>>();
        var filter = Uri.EscapeDataString($"deviceId eq '{entraDeviceId.Replace("'", "''", StringComparison.Ordinal)}'");
        var path = $"/v1.0/informationProtection/bitlocker/recoveryKeys?%24filter={filter}";
        var scopes = secretScope ? GraphScopeCatalog.BitlockerSecretScopes : GraphScopeCatalog.BitlockerMetadataScopes;
        var items = new List<BitlockerRecoveryMetadata>();
        var visited = new HashSet<string>(StringComparer.Ordinal);
        GraphOperationResult? lastResult = null;
        try
        {
            for (var page = 0; page < 100; page++)
            {
                if (!visited.Add(path)) return InvalidResponse<IReadOnlyList<BitlockerRecoveryMetadata>>(lastResult ?? new GraphOperationResult(false, "invalid_response"));
                var response = await SendAsync(path, scopes, cancellationToken);
                if (!response.Result.IsSuccess) return GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>.Failed(response.Result);
                lastResult = response.Result;
                using var document = JsonDocument.Parse(response.Content);
                if (!document.RootElement.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array) return InvalidResponse<IReadOnlyList<BitlockerRecoveryMetadata>>(response.Result);
                items.AddRange(value.EnumerateArray().Select(element => new BitlockerRecoveryMetadata(
                    Required(element, "id"), Optional(element, "deviceId"), Date(element, "createdDateTime"), Optional(element, "volumeType")))
                    .Where(item => string.Equals(item.DeviceId, entraDeviceId, StringComparison.OrdinalIgnoreCase)));
                if (!document.RootElement.TryGetProperty("@odata.nextLink", out var next) || next.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(next.GetString()))
                    return GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>.Succeeded(items, response.Result.CorrelationId, response.Result.RequestId);
                var link = next.GetString()!;
                if (!Uri.TryCreate(link, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || uri.Host != "graph.microsoft.com" || uri.AbsolutePath != "/v1.0/informationProtection/bitlocker/recoveryKeys")
                    return InvalidResponse<IReadOnlyList<BitlockerRecoveryMetadata>>(response.Result);
                path = uri.PathAndQuery;
            }
            return InvalidResponse<IReadOnlyList<BitlockerRecoveryMetadata>>(lastResult ?? new GraphOperationResult(false, "invalid_response"));
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return InvalidResponse<IReadOnlyList<BitlockerRecoveryMetadata>>(lastResult ?? new GraphOperationResult(false, "invalid_response")); }
    }

    public async Task<GraphReadResult<BitlockerSecret>> GetBitlockerAsync(string keyId, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(keyId)) return Invalid<BitlockerSecret>();
        var response = await SendAsync($"/v1.0/informationProtection/bitlocker/recoveryKeys/{Uri.EscapeDataString(keyId)}?$select=key", GraphScopeCatalog.BitlockerSecretScopes, cancellationToken);
        if (!response.Result.IsSuccess) return GraphReadResult<BitlockerSecret>.Failed(response.Result);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var root = Unwrap(document.RootElement);
            if (!string.Equals(Required(root, "id"), keyId, StringComparison.OrdinalIgnoreCase)) return InvalidResponse<BitlockerSecret>(response.Result);
            var key = Required(root, "key");
            return string.IsNullOrWhiteSpace(key) ? InvalidResponse<BitlockerSecret>(response.Result) : GraphReadResult<BitlockerSecret>.Succeeded(new BitlockerSecret(key), response.Result.CorrelationId, response.Result.RequestId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return InvalidResponse<BitlockerSecret>(response.Result); }
    }

    public async Task<GraphReadResult<LapsMetadata>> GetLapsMetadataAsync(string entraDeviceId, bool secretScope, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(entraDeviceId)) return Invalid<LapsMetadata>();
        var response = await SendAsync($"/v1.0/directory/deviceLocalCredentials/{Uri.EscapeDataString(entraDeviceId)}", secretScope ? GraphScopeCatalog.LapsSecretScopes : GraphScopeCatalog.LapsMetadataScopes, cancellationToken);
        if (!response.Result.IsSuccess) return GraphReadResult<LapsMetadata>.Failed(response.Result);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var root = Unwrap(document.RootElement);
            var id = Required(root, "id");
            if (!string.Equals(id, entraDeviceId, StringComparison.OrdinalIgnoreCase)) return InvalidResponse<LapsMetadata>(response.Result);
            return GraphReadResult<LapsMetadata>.Succeeded(new LapsMetadata(id, Optional(root, "deviceName"), Date(root, "lastBackupDateTime"), Date(root, "refreshDateTime")), response.Result.CorrelationId, response.Result.RequestId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException) { return InvalidResponse<LapsMetadata>(response.Result); }
    }

    public async Task<GraphReadResult<LapsSecret>> GetLapsSecretAsync(string entraDeviceId, CancellationToken cancellationToken)
    {
        if (!DeviceTarget.IsSafe(entraDeviceId)) return Invalid<LapsSecret>();
        var response = await SendAsync($"/v1.0/directory/deviceLocalCredentials/{Uri.EscapeDataString(entraDeviceId)}?$select=credentials", GraphScopeCatalog.LapsSecretScopes, cancellationToken);
        if (!response.Result.IsSuccess) return GraphReadResult<LapsSecret>.Failed(response.Result);
        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var root = Unwrap(document.RootElement);
            if (!string.Equals(Required(root, "id"), entraDeviceId, StringComparison.OrdinalIgnoreCase)) return InvalidResponse<LapsSecret>(response.Result);
            if (!root.TryGetProperty("credentials", out var credentials) || credentials.ValueKind != JsonValueKind.Array) return InvalidResponse<LapsSecret>(response.Result);
            if (credentials.GetArrayLength() == 0) return GraphReadResult<LapsSecret>.Failed(new GraphOperationResult(false, "recovery_not_found", 404, CorrelationId: response.Result.CorrelationId, RequestId: response.Result.RequestId));
            var item = credentials.EnumerateArray().OrderByDescending(candidate => Date(candidate, "backupDateTime")).First();
            var bytes = Convert.FromBase64String(Required(item, "passwordBase64"));
            if (bytes.Length == 0 || bytes.Length % 2 != 0) return InvalidResponse<LapsSecret>(response.Result);
            var password = new UnicodeEncoding(false, true, true).GetString(bytes);
            return string.IsNullOrWhiteSpace(password) ? InvalidResponse<LapsSecret>(response.Result) : GraphReadResult<LapsSecret>.Succeeded(new LapsSecret(Optional(item, "accountName"), password, Date(item, "backupDateTime")), response.Result.CorrelationId, response.Result.RequestId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException or DecoderFallbackException) { return InvalidResponse<LapsSecret>(response.Result); }
    }

    private async Task<GraphTransportResponse> SendAsync(string path, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(scopes, cancellationToken);
            return await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path, Headers: new Dictionary<string, string> { ["User-Agent"] = "AteaUnifiedWorkplace/1.0", ["ocp-client-name"] = "Atea Unified Workplace" }), cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception) { return Failure(GraphTokenAcquisitionErrorMapper.Map(exception)); }
        catch (MsalUiRequiredException exception) { return Failure(GraphTokenAcquisitionErrorMapper.Map(exception)); }
    }

    private static GraphTransportResponse Failure(GraphOperationResult result) => new(result, string.Empty, 0, new Dictionary<string, IReadOnlyCollection<string>>());
    private static GraphReadResult<T> Invalid<T>() => GraphReadResult<T>.Failed(new GraphOperationResult(false, "invalid_target"));
    private static GraphReadResult<T> InvalidResponse<T>(GraphOperationResult result) => GraphReadResult<T>.Failed(new GraphOperationResult(false, "invalid_response", CorrelationId: result.CorrelationId, RequestId: result.RequestId));
    private static JsonElement Unwrap(JsonElement root) => root.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object ? value : root;
    private static string Required(JsonElement root, string name) => Optional(root, name) ?? throw new InvalidOperationException("Missing Graph field.");
    private static string? Optional(JsonElement root, string name) => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement root, string name) => DateTimeOffset.TryParse(Optional(root, name), out var date) ? date : null;
}
