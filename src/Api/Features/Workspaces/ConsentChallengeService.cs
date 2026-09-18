using System.Security.Cryptography;
using System.Text;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record ConsentChallenge(string Challenge, string CorrelationId);

public sealed class ConsentChallengeService
{
    private readonly byte[] key;

    public ConsentChallengeService() : this(RandomNumberGenerator.GetBytes(32)) { }
    public ConsentChallengeService(byte[] key) => this.key = key.Length >= 32 ? key : throw new ArgumentException("Consent challenge key must be at least 32 bytes.", nameof(key));

    public ConsentChallenge Create(Guid workspaceId, Guid tenantId)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        var payload = $"{correlationId}|{workspaceId:N}|{tenantId:N}|{DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()}|{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}";
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(encodedPayload)));
        return new ConsentChallenge($"{encodedPayload}.{signature}", correlationId);
    }

    public bool Validate(string challenge, string correlationId)
    {
        var parts = challenge.Split('.', 2);
        if (parts.Length != 2) return false;
        var expected = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(parts[0]));
        var actual = FromBase64Url(parts[1]);
        if (actual is null || !CryptographicOperations.FixedTimeEquals(expected, actual)) return false;
        var payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]) ?? []);
        var fields = payload.Split('|');
        return fields.Length == 5 && fields[0] == correlationId && long.TryParse(fields[3], out var expiresAt) && expiresAt > DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[]? FromBase64Url(string value)
    {
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); } catch (FormatException) { return null; }
    }
}
