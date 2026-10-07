using System.Security.Cryptography;
using System.Text;
using System.Globalization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record ConsentChallenge(string Challenge, string CorrelationId, DateTimeOffset ExpiresAt);
public sealed record ConsentChallengePayload(
    string Purpose,
    string CorrelationId,
    Guid WorkspaceId,
    Guid TenantId,
    Guid InvitationId,
    DateTimeOffset ExpiresAt);

public sealed class ConsentChallengeService
{
    private const int MaximumChallengeLength = 4096;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
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
        return Sign(payload, correlationId, expiresAt);
    }

    public ConsentChallenge CreateInvitation(Guid workspaceId, Guid tenantId, Guid invitationId, DateTimeOffset invitationExpiresAt)
    {
        EnsureConfigured();
        var now = DateTimeOffset.UtcNow;
        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(
            Math.Min(now.AddMinutes(20).ToUnixTimeSeconds(), invitationExpiresAt.ToUnixTimeSeconds()));
        var correlationId = Guid.NewGuid().ToString("N");
        var random = Base64Url(RandomNumberGenerator.GetBytes(24));
        var payload = $"v1|invitation|{correlationId}|{workspaceId:N}|{tenantId:N}|{invitationId:N}|{expiresAt.ToUnixTimeSeconds()}|{random}";
        return Sign(payload, correlationId, expiresAt);
    }

    public bool Validate(string challenge, string correlationId, Guid expectedWorkspaceId, Guid expectedTenantId)
    {
        return TryRead(challenge, expectedWorkspaceId, expectedTenantId, out var parsed) && parsed.CorrelationId == correlationId;
    }

    public bool TryRead(string challenge, Guid expectedWorkspaceId, Guid expectedTenantId, out ConsentChallenge parsed)
    {
        parsed = default!;
        if (!TryVerifySignature(challenge, out var payloadBytes) ||
            !TryDecodePayload(payloadBytes, out var payload))
        {
            return false;
        }

        var fields = payload.Split('|');
        if (fields.Length != 5 ||
            !Guid.TryParseExact(fields[0], "N", out _) ||
            !Guid.TryParseExact(fields[1], "N", out var workspaceId) ||
            workspaceId != expectedWorkspaceId ||
            !Guid.TryParseExact(fields[2], "N", out var tenantId) ||
            tenantId != expectedTenantId ||
            !TryParseExpiration(fields[3], out var expiration) ||
            expiration <= DateTimeOffset.UtcNow ||
            !TryFromBase64(fields[4], out var randomBytes) ||
            randomBytes.Length != 24)
        {
            return false;
        }

        parsed = new ConsentChallenge(challenge, fields[0], expiration);
        return true;
    }

    public bool TryReadInvitation(
        string challenge,
        Guid expectedWorkspaceId,
        Guid expectedTenantId,
        Guid expectedInvitationId,
        out ConsentChallengePayload payload)
    {
        if (!TryReadInvitation(challenge, out payload) ||
            payload.WorkspaceId != expectedWorkspaceId ||
            payload.TenantId != expectedTenantId ||
            payload.InvitationId != expectedInvitationId)
        {
            payload = default!;
            return false;
        }

        return true;
    }

    public bool TryReadInvitation(string challenge, out ConsentChallengePayload payload)
    {
        payload = default!;
        if (!TryVerifySignature(challenge, out var payloadBytes) ||
            !TryDecodePayload(payloadBytes, out var decodedPayload))
        {
            return false;
        }

        var fields = decodedPayload.Split('|');
        if (fields.Length != 8 ||
            fields[0] != "v1" ||
            fields[1] != "invitation" ||
            !Guid.TryParseExact(fields[2], "N", out var correlationGuid) ||
            !Guid.TryParseExact(fields[3], "N", out var workspaceId) ||
            workspaceId == Guid.Empty ||
            !Guid.TryParseExact(fields[4], "N", out var tenantId) ||
            tenantId == Guid.Empty ||
            !Guid.TryParseExact(fields[5], "N", out var invitationId) ||
            invitationId == Guid.Empty ||
            !TryParseExpiration(fields[6], out var expiration) ||
            expiration <= DateTimeOffset.UtcNow ||
            !TryFromBase64Url(fields[7], out var randomBytes) ||
            randomBytes.Length != 24)
        {
            return false;
        }

        payload = new ConsentChallengePayload(
            fields[1],
            correlationGuid.ToString("N"),
            workspaceId,
            tenantId,
            invitationId,
            expiration);
        return true;
    }

    public async Task<bool> TryValidateAndConsumeAsync(string challenge, Guid expectedWorkspaceId, Guid expectedTenantId, IConsentChallengeRepository repository, CancellationToken cancellationToken = default)
    {
        if (!TryRead(challenge, expectedWorkspaceId, expectedTenantId, out _)) return false;
        return await repository.TryConsumeAsync(expectedWorkspaceId, expectedTenantId, HashState(challenge), DateTimeOffset.UtcNow, cancellationToken);
    }

    public static string HashState(string challenge) => Base64Url(SHA256.HashData(Encoding.UTF8.GetBytes(challenge)));

    private void EnsureConfigured() => _ = key ?? throw new InvalidOperationException("Consent challenge signing is not configured.");

    private ConsentChallenge Sign(string payload, string correlationId, DateTimeOffset expiresAt)
    {
        var encodedPayload = Base64Url(Encoding.UTF8.GetBytes(payload));
        var signature = Base64Url(HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(encodedPayload)));
        return new ConsentChallenge($"{encodedPayload}.{signature}", correlationId, expiresAt);
    }

    private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private bool TryVerifySignature(string challenge, out byte[] payloadBytes)
    {
        payloadBytes = [];
        if (!IsConfigured || string.IsNullOrEmpty(challenge) || challenge.Length > MaximumChallengeLength)
        {
            return false;
        }

        var parts = challenge.Split('.', 2);
        if (parts.Length != 2 ||
            !TryFromBase64Url(parts[0], out payloadBytes) ||
            !TryFromBase64Url(parts[1], out var signature))
        {
            return false;
        }

        var expectedSignature = HMACSHA256.HashData(key!, Encoding.UTF8.GetBytes(parts[0]));
        if (signature.Length != expectedSignature.Length ||
            !CryptographicOperations.FixedTimeEquals(expectedSignature, signature))
        {
            payloadBytes = [];
            return false;
        }

        return true;
    }

    private static bool TryDecodePayload(byte[] payloadBytes, out string payload)
    {
        try
        {
            payload = StrictUtf8.GetString(payloadBytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            payload = string.Empty;
            return false;
        }
    }

    private static bool TryFromBase64Url(string value, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(value) ||
            value.Any(character => !char.IsAsciiLetterOrDigit(character) && character != '-' && character != '_') ||
            value.Length % 4 == 1)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/') +
                new string('=', (4 - value.Length % 4) % 4));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryFromBase64(string value, out byte[] bytes)
    {
        bytes = [];
        try
        {
            bytes = Convert.FromBase64String(value);
            return bytes.Length == 24 && Convert.ToBase64String(bytes) == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryParseExpiration(string value, out DateTimeOffset expiration)
    {
        expiration = default;
        if (!long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var unixSeconds))
        {
            return false;
        }

        try
        {
            expiration = DateTimeOffset.FromUnixTimeSeconds(unixSeconds);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }
}
