using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Users;

public interface IUserQueryService
{
    Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken);
}

public sealed class UserQueryService(
    IUserDirectoryReader directoryReader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    UserContinuationTokenProtector continuationProtector,
    Func<DateTimeOffset>? utcNow = null) : IUserQueryService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken)
    {
        var normalizedQuery = UserSearchFilterContract.Normalize(request);
        var now = utcNow();

        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var decision = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.UsersView];
        if (decision.State != CapabilityState.Allowed)
        {
            return new UserDirectoryResponse(
                [],
                null,
                utcNow(),
                UserDirectoryFreshness.Unavailable,
                true,
                new UserDirectoryError(
                    "capability_required",
                    "Users cannot be read for the current capability state.",
                    decision.State));
        }

        var continuationPath = string.IsNullOrWhiteSpace(request.ContinuationToken)
            ? null
            : continuationProtector.Unprotect(request.ContinuationToken, context, normalizedQuery, now);

        var query = normalizedQuery with { ContinuationPath = continuationPath };
        var result = await directoryReader.SearchAsync(context, query, cancellationToken);
        var continuationToken = string.IsNullOrWhiteSpace(result.ContinuationLink)
            ? null
            : continuationProtector.Protect(result.ContinuationLink, context, normalizedQuery, now);

        if (result.Error is null)
        {
            return new UserDirectoryResponse(result.Items, continuationToken, utcNow(), UserDirectoryFreshness.Fresh, false);
        }

        return new UserDirectoryResponse(
            result.Items,
            continuationToken,
            utcNow(),
            FreshnessFor(result.Error.Category),
            true,
            new UserDirectoryError(
                result.Error.Category,
                MessageFor(result.Error.Category),
                null,
                result.Error.StatusCode,
                result.Error.RetryAfter is null ? null : (int)Math.Ceiling(result.Error.RetryAfter.Value.TotalSeconds)));
    }

    private static string FreshnessFor(string category) => category switch
    {
        "throttled" => UserDirectoryFreshness.Stale,
        _ => UserDirectoryFreshness.Unavailable
    };

    private static string MessageFor(string category) => category switch
    {
        "not_found" => "The directory page could not be found.",
        "throttled" => "Microsoft Graph throttled the directory request.",
        "not_authorized" => "The signed-in user is not authorized to read the directory.",
        "consent_required" => "Delegated Microsoft Graph consent is required.",
        _ => "The directory is temporarily unavailable."
    };
}

public sealed class UserContinuationTokenProtector
{
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;
    private static readonly TimeSpan DefaultLifetime = TimeSpan.FromMinutes(10);
    private readonly byte[] key;
    private readonly TimeSpan lifetime;

    public UserContinuationTokenProtector(string? signingKey, TimeSpan? lifetime = null)
    {
        this.lifetime = lifetime ?? DefaultLifetime;
        if (this.lifetime <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Continuation token lifetime must be positive.");
        }

        if (string.IsNullOrWhiteSpace(signingKey))
        {
            key = RandomNumberGenerator.GetBytes(32);
            return;
        }

        key = SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));
    }

    public string Protect(string continuationPath, WorkspaceContext context, UserSearchQuery query, DateTimeOffset issuedAt)
    {
        if (!IsUserContinuationPath(continuationPath))
        {
            throw new ArgumentException("Continuation path must be a users collection path.", nameof(continuationPath));
        }

        var issuedAtUnixSeconds = issuedAt.ToUnixTimeSeconds();
        var payload = new UserContinuationPayload(
            continuationPath,
            context.Membership.WorkspaceId,
            context.User.TenantId,
            context.User.ObjectId,
            UserSearchQueryFingerprint.Create(query),
            issuedAtUnixSeconds,
            issuedAt.Add(lifetime).ToUnixTimeSeconds());
        var plaintext = JsonSerializer.SerializeToUtf8Bytes(payload);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagSize];
        var associatedData = new[] { Version };
        using var aes = new AesGcm(key, TagSize);
        aes.Encrypt(nonce.AsSpan(), plaintext.AsSpan(), ciphertext.AsSpan(), tag.AsSpan(), associatedData.AsSpan());

        var token = new byte[1 + nonce.Length + tag.Length + ciphertext.Length];
        token[0] = Version;
        Buffer.BlockCopy(nonce, 0, token, 1, nonce.Length);
        Buffer.BlockCopy(tag, 0, token, 1 + nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, token, 1 + nonce.Length + tag.Length, ciphertext.Length);
        return Base64UrlEncode(token);
    }

    public string Unprotect(string token, WorkspaceContext context, UserSearchQuery query, DateTimeOffset now)
    {
        try
        {
            var protectedBytes = Base64UrlDecode(token);
            if (protectedBytes.Length <= 1 + NonceSize + TagSize || protectedBytes[0] != Version)
            {
                throw InvalidToken();
            }

            var nonce = protectedBytes.AsSpan(1, NonceSize).ToArray();
            var tag = protectedBytes.AsSpan(1 + NonceSize, TagSize).ToArray();
            var ciphertext = protectedBytes.AsSpan(1 + NonceSize + TagSize).ToArray();
            var plaintext = new byte[ciphertext.Length];
            var associatedData = new[] { Version };
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce.AsSpan(), ciphertext.AsSpan(), tag.AsSpan(), plaintext.AsSpan(), associatedData.AsSpan());
            var payload = JsonSerializer.Deserialize<UserContinuationPayload>(plaintext);
            var nowUnixSeconds = now.ToUnixTimeSeconds();
            if (payload is null ||
                !IsUserContinuationPath(payload.Path) ||
                payload.WorkspaceId != context.Membership.WorkspaceId ||
                payload.TenantId != context.User.TenantId ||
                payload.ActorId != context.User.ObjectId ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(payload.QueryFingerprint),
                    Encoding.UTF8.GetBytes(UserSearchQueryFingerprint.Create(query))) ||
                payload.IssuedAtUnixSeconds > nowUnixSeconds + 30 ||
                payload.ExpiresAtUnixSeconds <= nowUnixSeconds)
            {
                throw InvalidToken();
            }

            return payload.Path;
        }
        catch (CryptographicException exception)
        {
            throw InvalidToken(exception);
        }
        catch (FormatException exception)
        {
            throw InvalidToken(exception);
        }
        catch (JsonException exception)
        {
            throw InvalidToken(exception);
        }
        catch (ArgumentException exception)
        {
            throw InvalidToken(exception);
        }
    }

    private static UserSearchValidationException InvalidToken(Exception? innerException = null) =>
        innerException is null
            ? new UserSearchValidationException("continuationToken is invalid or expired.")
            : new UserSearchValidationException("continuationToken is invalid or expired.", innerException);

    private static bool IsUserContinuationPath(string path) =>
        path.Equals("/v1.0/users", StringComparison.Ordinal) ||
        path.StartsWith("/v1.0/users?", StringComparison.Ordinal);

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}

internal static class UserSearchQueryFingerprint
{
    public static string Create(UserSearchQuery query)
    {
        var canonical = string.Join("\n", [
            query.Search ?? string.Empty,
            query.PageSize.ToString(System.Globalization.CultureInfo.InvariantCulture),
            query.AccountStatus ?? string.Empty,
            query.TenantRole ?? string.Empty,
            query.License ?? string.Empty,
            query.UserType ?? string.Empty
        ]);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}

internal sealed record UserContinuationPayload(
    string Path,
    Guid WorkspaceId,
    Guid TenantId,
    Guid ActorId,
    string QueryFingerprint,
    long IssuedAtUnixSeconds,
    long ExpiresAtUnixSeconds);
