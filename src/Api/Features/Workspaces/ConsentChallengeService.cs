using System.Security.Cryptography;
using System.Text;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record ConsentChallenge(string Challenge, string CorrelationId, DateTimeOffset ExpiresAt);

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
        => Create(workspaceId, tenantId, DateTimeOffset.UtcNow.AddMinutes(10));

    public ConsentChallenge Create(Guid workspaceId, Guid tenantId, DateTimeOffset expiresAt)
    {
        EnsureConfigured();
        var correlationId = Guid.NewGuid().ToString("N");
        var payload = $"{correlationId}|{workspaceId:N}|{tenantId:N}|{expiresAt.ToUnixTimeSeconds()}|{Convert.ToBase64String(RandomNumberGenerator.GetBytes(24))}";
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(encodedPayload)));
        return new ConsentChallenge($"{encodedPayload}.{signature}", correlationId, expiresAt);
    }

    public bool Validate(string challenge, string correlationId, Guid expectedWorkspaceId, Guid expectedTenantId)
    {
        return TryRead(challenge, expectedWorkspaceId, expectedTenantId, out var parsed) && parsed.CorrelationId == correlationId;
    }

    public bool TryRead(string challenge, Guid expectedWorkspaceId, Guid expectedTenantId, out ConsentChallenge parsed)
    {
        parsed = default!;
        if (!IsConfigured) return false;
        var parts = challenge.Split('.', 2);
        if (parts.Length != 2) return false;
        var payloadBytes = FromBase64Url(parts[0]);
        var signature = FromBase64Url(parts[1]);
        if (payloadBytes is null || signature is null) return false;
        var expectedSignature = HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(parts[0]));
        if (!CryptographicOperations.FixedTimeEquals(expectedSignature, signature)) return false;
        var fields = Encoding.UTF8.GetString(payloadBytes).Split('|');
        if (fields.Length != 5 || !Guid.TryParse(fields[1], out var workspaceId) || workspaceId != expectedWorkspaceId ||
            !Guid.TryParse(fields[2], out var tenantId) || tenantId != expectedTenantId ||
            !long.TryParse(fields[3], out var expiresAt) || expiresAt <= DateTimeOffset.UtcNow.ToUnixTimeSeconds()) return false;
        parsed = new ConsentChallenge(challenge, fields[0], DateTimeOffset.FromUnixTimeSeconds(expiresAt));
        return true;
    }

    public async Task<bool> TryValidateAndConsumeAsync(string challenge, Guid expectedWorkspaceId, Guid expectedTenantId, IConsentChallengeRepository repository, CancellationToken cancellationToken = default)
    {
        if (!TryRead(challenge, expectedWorkspaceId, expectedTenantId, out _)) return false;
        return await repository.TryConsumeAsync(expectedWorkspaceId, expectedTenantId, HashState(challenge), DateTimeOffset.UtcNow, cancellationToken);
    }

    public static string HashState(string challenge) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(challenge)));

    private void EnsureConfigured() => _ = key ?? throw new InvalidOperationException("Consent challenge signing is not configured.");
    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    private static byte[]? FromBase64Url(string value)
    {
        try { return Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') + new string('=', (4 - value.Length % 4) % 4)); } catch (FormatException) { return null; }
    }
}
