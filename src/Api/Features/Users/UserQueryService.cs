using System.Security.Cryptography;
using System.Text;
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
        Validate(request);

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
            : continuationProtector.Unprotect(request.ContinuationToken);

        var query = new UserSearchQuery(
            Clean(request.Search),
            request.PageSize,
            continuationPath,
            Clean(request.AccountStatus),
            Clean(request.TenantRole),
            Clean(request.License),
            Clean(request.UserType));
        var result = await directoryReader.SearchAsync(context, query, cancellationToken);
        var continuationToken = string.IsNullOrWhiteSpace(result.ContinuationLink)
            ? null
            : continuationProtector.Protect(result.ContinuationLink);

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

    private static void Validate(UserSearchRequest request)
    {
        if (request.PageSize is < 1 or > 100)
        {
            throw new UserSearchValidationException("pageSize must be between 1 and 100.");
        }
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

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
    private readonly byte[] key;

    public UserContinuationTokenProtector(string? signingKey)
    {
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            key = RandomNumberGenerator.GetBytes(32);
            return;
        }

        key = SHA256.HashData(Encoding.UTF8.GetBytes(signingKey));
    }

    public string Protect(string continuationPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continuationPath);

        var plaintext = Encoding.UTF8.GetBytes(continuationPath);
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

    public string Unprotect(string token)
    {
        try
        {
            var protectedBytes = Base64UrlDecode(token);
            if (protectedBytes.Length <= 1 + NonceSize + TagSize || protectedBytes[0] != Version)
            {
                throw new UserSearchValidationException("continuationToken is invalid.");
            }

            var nonce = protectedBytes.AsSpan(1, NonceSize).ToArray();
            var tag = protectedBytes.AsSpan(1 + NonceSize, TagSize).ToArray();
            var ciphertext = protectedBytes.AsSpan(1 + NonceSize + TagSize).ToArray();
            var plaintext = new byte[ciphertext.Length];
            var associatedData = new[] { Version };
            using var aes = new AesGcm(key, TagSize);
            aes.Decrypt(nonce.AsSpan(), ciphertext.AsSpan(), tag.AsSpan(), plaintext.AsSpan(), associatedData.AsSpan());
            var path = Encoding.UTF8.GetString(plaintext);
            if (!path.StartsWith("/v1.0/users", StringComparison.Ordinal))
            {
                throw new UserSearchValidationException("continuationToken is invalid.");
            }

            return path;
        }
        catch (CryptographicException exception)
        {
            throw new UserSearchValidationException("continuationToken is invalid.", exception);
        }
        catch (FormatException exception)
        {
            throw new UserSearchValidationException("continuationToken is invalid.", exception);
        }
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        return Convert.FromBase64String(padded);
    }
}
