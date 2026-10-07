using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text.Json;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public interface ITenantResolver
{
    Task<TenantResolutionResult> ResolveAsync(string domain, CancellationToken cancellationToken = default);
}

public enum TenantResolutionStatus
{
    Resolved,
    InvalidInput,
    NotFound,
    Unavailable
}

public sealed record TenantResolutionResult(TenantResolutionStatus Status, Guid? TenantId);

public sealed class OidcTenantResolver(IHttpClientFactory httpClientFactory) : ITenantResolver
{
    private static readonly Uri DiscoveryBaseUri = new("https://login.microsoftonline.com/");
    private static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromSeconds(5);
    private const int MaxMetadataBytes = 64 * 1024;
    private static readonly HashSet<string> ReservedAuthorities = new(StringComparer.OrdinalIgnoreCase)
    {
        "common", "organizations", "consumers"
    };

    private readonly ConcurrentDictionary<string, (Guid TenantId, DateTimeOffset ExpiresAt)> _cache =
        new(StringComparer.Ordinal);

    public async Task<TenantResolutionResult> ResolveAsync(string domain, CancellationToken cancellationToken = default)
    {
        if (!TryNormalizeDomain(domain, out var normalizedDomain))
        {
            return new TenantResolutionResult(TenantResolutionStatus.InvalidInput, null);
        }

        if (_cache.TryGetValue(normalizedDomain, out var cached))
        {
            if (cached.ExpiresAt > DateTimeOffset.UtcNow)
            {
                return new TenantResolutionResult(TenantResolutionStatus.Resolved, cached.TenantId);
            }

            _cache.TryRemove(normalizedDomain, out _);
        }

        var requestUri = new Uri(DiscoveryBaseUri, $"{Uri.EscapeDataString(normalizedDomain)}/v2.0/.well-known/openid-configuration");
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(DiscoveryTimeout);
            var discoveryCancellationToken = timeout.Token;
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            using var response = await httpClientFactory.CreateClient("EntraTenantDiscovery")
                .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, discoveryCancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound ||
                (response.StatusCode == HttpStatusCode.BadRequest &&
                 await IsTenantNotFoundErrorAsync(response.Content, discoveryCancellationToken)))
            {
                return new TenantResolutionResult(TenantResolutionStatus.NotFound, null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new TenantResolutionResult(TenantResolutionStatus.Unavailable, null);
            }

            var metadataBytes = await ReadBoundedContentAsync(response.Content, discoveryCancellationToken);
            if (metadataBytes is null)
            {
                return new TenantResolutionResult(TenantResolutionStatus.Unavailable, null);
            }

            using var metadataContent = new MemoryStream(metadataBytes);
            var metadata = await JsonSerializer.DeserializeAsync<OpenIdConfiguration>(
                metadataContent,
                cancellationToken: discoveryCancellationToken);
            if (!TryReadTenantId(metadata, out var tenantId))
            {
                return new TenantResolutionResult(TenantResolutionStatus.Unavailable, null);
            }

            _cache[normalizedDomain] = (tenantId, DateTimeOffset.UtcNow.Add(CacheDuration));
            return new TenantResolutionResult(TenantResolutionStatus.Resolved, tenantId);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is OperationCanceledException or HttpRequestException or IOException or JsonException)
        {
            return new TenantResolutionResult(TenantResolutionStatus.Unavailable, null);
        }
    }

    private static bool TryNormalizeDomain(string? input, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        var value = input.Trim();
        if (value.Any(char.IsControl) || value.Contains('/') || value.Contains('\\') ||
            value.Contains('@') || value.Contains(':') || value.Contains('?') || value.Contains('#') ||
            value.Contains('*') || IPAddress.TryParse(value, out _))
        {
            return false;
        }

        string ascii;
        try
        {
            ascii = new IdnMapping().GetAscii(value).ToLowerInvariant();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (ascii.Length is 0 or > 253 || ReservedAuthorities.Contains(ascii))
        {
            return false;
        }

        var labels = ascii.Split('.');
        if (labels.Length < 2 || labels.Any(label =>
                label.Length is 0 or > 63 ||
                label[0] == '-' ||
                label[^1] == '-' ||
                label.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-')))
        {
            return false;
        }

        normalized = ascii;
        return true;
    }

    private static async Task<bool> IsTenantNotFoundErrorAsync(HttpContent content, CancellationToken cancellationToken)
    {
        var body = await ReadBoundedContentAsync(content, cancellationToken);
        if (body is null)
        {
            return false;
        }

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (root.TryGetProperty("error", out var error) &&
            error.ValueKind == JsonValueKind.String &&
            string.Equals(error.GetString(), "invalid_tenant", StringComparison.Ordinal))
        {
            return true;
        }

        return root.TryGetProperty("error_codes", out var errorCodes) &&
            errorCodes.ValueKind == JsonValueKind.Array &&
            errorCodes.EnumerateArray().Any(code => code.TryGetInt32(out var value) && value == 90002);
    }

    private static async Task<byte[]?> ReadBoundedContentAsync(HttpContent content, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is > MaxMetadataBytes)
        {
            return null;
        }

        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var boundedContent = new MemoryStream();
        var buffer = new byte[8192];
        while (true)
        {
            var remaining = MaxMetadataBytes + 1 - (int)boundedContent.Length;
            if (remaining <= 0)
            {
                return null;
            }

            var read = await stream.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, remaining)), cancellationToken);
            if (read == 0)
            {
                break;
            }

            await boundedContent.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return boundedContent.Length > MaxMetadataBytes ? null : boundedContent.ToArray();
    }

    private static bool TryReadTenantId(OpenIdConfiguration? metadata, out Guid tenantId)
    {
        tenantId = default;
        if (!TryReadTenantSegment(metadata?.Issuer, "/v2.0", out var issuerTenant) ||
            !Guid.TryParseExact(issuerTenant, "D", out tenantId) ||
            !IsTrustedEndpoint(metadata?.AuthorizationEndpoint, tenantId) ||
            !IsTrustedEndpoint(metadata?.TokenEndpoint, tenantId))
        {
            tenantId = default;
            return false;
        }

        return true;
    }

    private static bool IsTrustedEndpoint(string? endpoint, Guid tenantId)
    {
        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, DiscoveryBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !TryExtractTenantSegment(uri.AbsolutePath, out var endpointTenant) ||
            !string.Equals(endpointTenant, tenantId.ToString("D"), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static bool TryReadTenantSegment(string? value, string suffix, out string tenant)
    {
        tenant = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(uri.Host, DiscoveryBaseUri.Host, StringComparison.OrdinalIgnoreCase) ||
            uri.Port != 443 ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        var path = uri.AbsolutePath;
        if (!path.EndsWith(suffix, StringComparison.Ordinal) ||
            (suffix.Length > 0 && path.Length <= suffix.Length))
        {
            return false;
        }

        return TryExtractTenantSegment(path[..^suffix.Length], out tenant);
    }

    private static bool TryExtractTenantSegment(string path, out string tenant)
    {
        tenant = string.Empty;
        if (!path.StartsWith("/", StringComparison.Ordinal))
        {
            return false;
        }

        var end = path.IndexOf('/', 1);
        var tenantSegment = end < 0 ? path[1..] : path[1..end];
        if (tenantSegment.Length == 0 || tenantSegment.Contains('/'))
        {
            return false;
        }

        tenant = tenantSegment;
        return true;
    }

    private sealed record OpenIdConfiguration(
        [property: System.Text.Json.Serialization.JsonPropertyName("issuer")] string? Issuer,
        [property: System.Text.Json.Serialization.JsonPropertyName("authorization_endpoint")] string? AuthorizationEndpoint,
        [property: System.Text.Json.Serialization.JsonPropertyName("token_endpoint")] string? TokenEndpoint);
}
