using System.Security.Cryptography;
using System.Text;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record ConsentChallenge(string Challenge, string CorrelationId);

public sealed class ConsentChallengeService
{
    private readonly byte[]? key;

    public ConsentChallengeService(string? base64Key)
    {
        if (!string.IsNullOrWhiteSpace(base64Key))
        {
            try
            {
                var decoded = Convert.FromBase64String(base64Key);
                if (decoded.Length >= 32) key = decoded;
            }
            catch (FormatException) { }
        }
    }

    public ConsentChallengeService(byte[] key) => this.key = key.Length >= 32 ? key : throw new ArgumentException("Consent challenge key must be at least 32 bytes.", nameof(key));
    public bool IsConfigured => key is not null;

    public ConsentChallenge Create(Guid workspaceId, Guid tenantId)
    {
        EnsureConfigured();
        var correlationId = Guid.NewGuid().ToString("N");
        var payload = $"{correlationId}|{workspaceId:N}|{tenantId:N}|{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}|{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}";
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(encodedPayload)));
        return new ConsentChallenge($"{encodedPayload}.{signature}", correlationId);
    }

    public bool Validate(string challenge, string correlationId, Guid expectedWorkspaceId, Guid expectedTenantId)
    {
        if (!IsConfigured) return false;
        var parts = challenge.Split('.', 2);
        if (parts.Length != 2) return false;
        var expected = HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(parts[0]));
        var actual = FromBase64Url(parts[1]);
        var payloadBytes = FromBase64Url(parts[0]);
        if (actual is null || payloadBytes is null || !CryptographicOperations.FixedTimeEquals(expected, actual)) return false;
        var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
        return fields.Length == 5 && fields[0] == correlationId && Guid.TryParse(fields[1], out var workspaceId) && workspaceId == expectedWorkspaceId && Guid.TryParse(fields[2], out var tenantId) && tenantId == expectedTenantId && long.TryParse(fields[3], out var expiresAt) && expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private void EnsureConfigured() => _ = key ?? throw new InvalidOperationException("Consent challenge signing is not configured.");
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[]? FromBase64Url(string value)
    {
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); } catch (FormatException) { return null; }
    }
}
