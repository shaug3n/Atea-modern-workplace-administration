using System.Security.Cryptography;
using System.Text;
using System.Net.Http.Headers;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record InvitationPreviewResult(
    string WorkspaceName,
    DateTimeOffset ExpiresAt,
    string Flow,
    IReadOnlyCollection<string> PermissionScopes);

public sealed record InvitationConsentStartResult(
    string AuthorizationUrl,
    IReadOnlyCollection<string> Scopes,
    string Challenge,
    string CorrelationId,
    DateTimeOffset ExpiresAt);

public sealed record InvitationConsentResumeResult(
    bool Valid,
    string Status,
    Guid? TenantId,
    string CorrelationId);

public sealed class InvitationConsentUnavailableException() : Exception("Invitation consent is unavailable.");
public sealed class InvitationConsentNotFoundException() : Exception("Invitation is unavailable.");
public sealed class InvitationConsentConflictException() : Exception("Invitation consent is not available for this invitation.");

public interface IInvitationConsentService
{
    Task<InvitationPreviewResult?> PreviewAsync(string nonce, CancellationToken cancellationToken = default);
    Task<InvitationConsentStartResult> StartAsync(string nonce, CancellationToken cancellationToken = default);
    Task<InvitationConsentResumeResult> ResumeAsync(string nonce, string state, Guid? tenant, string? errorCode, CancellationToken cancellationToken = default);
}

public sealed class InvitationConsentService(
    IInvitationReadRepository invitations,
    IConsentChallengeRepository challenges,
    ConsentChallengeService challengeService,
    OnboardingOptions options) : IInvitationConsentService
{
    private const int MaximumStateLength = 4096;
    private const string ConsentFirstFlow = "consent_first";
    private const string SignInFlow = "sign_in";

    public async Task<InvitationPreviewResult?> PreviewAsync(string nonce, CancellationToken cancellationToken = default)
    {
        if (!IsValidNonce(nonce)) return null;
        var invitation = await FindInvitationAsync(nonce, cancellationToken);
        if (!IsAvailable(invitation, allowRedeemed: false)) return null;
        return new InvitationPreviewResult(
            invitation!.WorkspaceName,
            invitation.ExpiresAt,
            IsConsentEligible(invitation.Role) ? ConsentFirstFlow : SignInFlow,
            GraphScopeCatalog.CapabilityEvaluationScopes);
    }

    public async Task<InvitationConsentStartResult> StartAsync(string nonce, CancellationToken cancellationToken = default)
    {
        if (!IsValidNonce(nonce)) throw new InvitationConsentNotFoundException();
        if (!options.IsInvitationConsentConfigured || !challengeService.IsConfigured)
            throw new InvitationConsentUnavailableException();

        var invitation = await FindInvitationAsync(nonce, cancellationToken);
        if (!IsAvailable(invitation, allowRedeemed: false)) throw new InvitationConsentNotFoundException();
        if (!IsConsentEligible(invitation!.Role)) throw new InvitationConsentConflictException();

        try
        {
            var challenge = challengeService.CreateInvitation(
                invitation.WorkspaceId,
                invitation.TenantId,
                invitation.InvitationId,
                invitation.ExpiresAt);
            var persisted = new InvitationConsentChallengeRecord(
                ConsentChallengeService.HashState(challenge.Challenge),
                invitation.WorkspaceId,
                invitation.TenantId,
                invitation.InvitationId,
                "invitation",
                challenge.CorrelationId,
                challenge.ExpiresAt,
                null);
            await challenges.CreateInvitationAsync(persisted, cancellationToken);
            var authorizationUrl = BuildAuthorizationUrl(invitation.TenantId, challenge.Challenge);
            return new InvitationConsentStartResult(
                authorizationUrl,
                GraphScopeCatalog.CapabilityEvaluationScopes,
                challenge.Challenge,
                challenge.CorrelationId,
                challenge.ExpiresAt);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvitationConsentUnavailableException)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvitationConsentUnavailableException();
        }
    }

    public async Task<InvitationConsentResumeResult> ResumeAsync(
        string nonce,
        string state,
        Guid? tenant,
        string? errorCode = null,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidNonce(nonce) || string.IsNullOrEmpty(state) || state.Length > MaximumStateLength)
            return InvalidResume();
        if (!challengeService.TryReadInvitation(state, out var payload) ||
            (tenant.HasValue && tenant.Value != payload.TenantId))
        {
            return InvalidResume();
        }

        var invitation = await FindInvitationAsync(nonce, cancellationToken);
        if (invitation is null ||
            invitation.IsRevoked ||
            invitation.ExpiresAt <= DateTimeOffset.UtcNow ||
            invitation.WorkspaceId != payload.WorkspaceId ||
            invitation.TenantId != payload.TenantId ||
            invitation.InvitationId != payload.InvitationId)
        {
            return InvalidResume();
        }

        InvitationConsentChallengeRecord? stored;
        try
        {
            stored = await challenges.FindInvitationAsync(ConsentChallengeService.HashState(state), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvitationConsentUnavailableException();
        }

        if (stored is null ||
            stored.ConsumedAt is not null ||
            stored.ExpiresAt <= DateTimeOffset.UtcNow ||
            stored.Purpose != "invitation" ||
            stored.InvitationId != invitation.InvitationId ||
            stored.WorkspaceId != invitation.WorkspaceId ||
            stored.TenantId != invitation.TenantId ||
            stored.CorrelationId != payload.CorrelationId ||
            stored.ExpiresAt.ToUnixTimeSeconds() != payload.ExpiresAt.ToUnixTimeSeconds())
        {
            return InvalidResume();
        }

        if (!string.IsNullOrWhiteSpace(errorCode))
            return new InvitationConsentResumeResult(true, "consent_denied", null, payload.CorrelationId);

        return new InvitationConsentResumeResult(true, "ready_to_sign_in", invitation.TenantId, payload.CorrelationId);
    }

    private async Task<InvitationLookup?> FindInvitationAsync(string nonce, CancellationToken cancellationToken)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce))).ToLowerInvariant();
        try
        {
            return await invitations.FindByNonceHashAsync(hash, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            throw new InvitationConsentUnavailableException();
        }
    }

    private string BuildAuthorizationUrl(Guid tenantId, string challenge)
    {
        var scope = $"{options.ApiApplicationIdUri.TrimEnd('/')}/.default";
        return $"https://login.microsoftonline.com/{tenantId:D}/v2.0/adminconsent" +
            $"?client_id={Uri.EscapeDataString(options.CustomerClientId)}" +
            $"&scope={Uri.EscapeDataString(scope)}" +
            $"&redirect_uri={Uri.EscapeDataString(options.ConsentRedirectUri)}" +
            $"&state={Uri.EscapeDataString(challenge)}";
    }

    private static bool IsAvailable(InvitationLookup? invitation, bool allowRedeemed) =>
        invitation is not null &&
        !invitation.IsRevoked &&
        (allowRedeemed || !invitation.IsRedeemed) &&
        invitation.ExpiresAt > DateTimeOffset.UtcNow;

    private static bool IsConsentEligible(string role) => role is "customer_admin" or "workspace_owner";

    private static bool IsValidNonce(string? nonce) =>
        nonce is { Length: 43 } &&
        nonce.All(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_');

    private static InvitationConsentResumeResult InvalidResume() =>
        new(false, "invalid_callback", null, string.Empty);
}

public sealed class InvitationRequestSecurityMiddleware(RequestDelegate next, IOptions<OnboardingOptions> onboardingOptions)
{
    private const long MaximumCallbackBodyBytes = 8 * 1024;

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path;
        var isInvitationApi = path.StartsWithSegments("/api/invitations");
        var isPublicInvitationPage = path.StartsWithSegments("/invitations") ||
            string.Equals(path.Value, "/onboarding/consent/callback", StringComparison.OrdinalIgnoreCase);

        if (isInvitationApi || isPublicInvitationPage)
        {
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.Headers["Referrer-Policy"] = "no-referrer";
        }

        if (isInvitationApi && IsConsentPost(path))
        {
            if (context.Request.ContentLength is > MaximumCallbackBodyBytes)
            {
                context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
                return;
            }

            if (!MediaTypeHeaderValue.TryParse(context.Request.ContentType, out var contentType) ||
                !string.Equals(contentType.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
                return;
            }

            if (!HasSameOrigin(context.Request, onboardingOptions.Value.PublicBaseUrl))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { error = "invalid_origin" }, context.RequestAborted);
                return;
            }

            var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (bodySizeFeature is { IsReadOnly: false })
                bodySizeFeature.MaxRequestBodySize = MaximumCallbackBodyBytes;
        }

        await next(context);
    }

    private static bool IsConsentPost(PathString path) =>
        path.Value?.EndsWith("/consent/start", StringComparison.OrdinalIgnoreCase) == true ||
        path.Value?.EndsWith("/consent/resume", StringComparison.OrdinalIgnoreCase) == true;

    private static bool HasSameOrigin(HttpRequest request, string configuredPublicBaseUrl)
    {
        var originValues = request.Headers.Origin;
        if (originValues.Count != 1 ||
            !Uri.TryCreate(originValues[0], UriKind.Absolute, out var requestOrigin) ||
            !Uri.TryCreate(configuredPublicBaseUrl, UriKind.Absolute, out var publicUri) ||
            requestOrigin.UserInfo.Length != 0 ||
            requestOrigin.Query.Length != 0 ||
            requestOrigin.Fragment.Length != 0 ||
            requestOrigin.AbsolutePath != "/" ||
            !string.Equals(requestOrigin.GetLeftPart(UriPartial.Authority), publicUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }
}
