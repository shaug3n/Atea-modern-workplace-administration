using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed class OnboardingOptions
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ConsentRedirectUri { get; set; } = string.Empty;
    public string ConsentSigningKey { get; set; } = string.Empty;
    public string CustomerClientId { get; set; } = string.Empty;
    public string ApiApplicationIdUri { get; set; } = string.Empty;
    public string TrustedProxyAddresses { get; set; } = string.Empty;

    public bool IsInvitationConsentConfigured
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CustomerClientId) ||
                string.IsNullOrWhiteSpace(ApiApplicationIdUri) ||
                string.IsNullOrWhiteSpace(ConsentSigningKey) ||
                !Uri.TryCreate(ApiApplicationIdUri, UriKind.Absolute, out _) ||
                !Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicUri) ||
                !Uri.TryCreate(ConsentRedirectUri, UriKind.Absolute, out var redirectUri) ||
                publicUri is null ||
                redirectUri is null ||
                publicUri.UserInfo.Length != 0 ||
                redirectUri.UserInfo.Length != 0 ||
                publicUri.Query.Length != 0 ||
                publicUri.Fragment.Length != 0 ||
                redirectUri.Query.Length != 0 ||
                redirectUri.Fragment.Length != 0 ||
                !string.Equals(publicUri.GetLeftPart(UriPartial.Authority), redirectUri.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(redirectUri.AbsolutePath, "/onboarding/consent/callback", StringComparison.Ordinal))
            {
                return false;
            }

            if ((!IsHttpsOrLocalhost(publicUri) || !IsHttpsOrLocalhost(redirectUri)) ||
                !TryReadSigningKey(ConsentSigningKey, out var key) ||
                key.Length < 32)
            {
                return false;
            }

            return true;
        }
    }

    public Uri Validate(IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUrl) ||
            string.Equals(PublicBaseUrl.TrimEnd('/'), "https://workplace.example", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicUri) ||
            publicUri is null ||
            publicUri.UserInfo.Length > 0 ||
            publicUri.Query.Length > 0 ||
            publicUri.Fragment.Length > 0 ||
            (publicUri.Scheme != Uri.UriSchemeHttp && publicUri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Onboarding:PublicBaseUrl must be a configured absolute HTTP(S) URL.");
        }

        if (publicUri.Scheme == Uri.UriSchemeHttp && !environment.IsDevelopment())
        {
            throw new InvalidOperationException("Onboarding:PublicBaseUrl must use HTTPS outside Development.");
        }

        var consentConfigured = !string.IsNullOrWhiteSpace(ConsentRedirectUri) || !string.IsNullOrWhiteSpace(ConsentSigningKey);
        if (consentConfigured && string.IsNullOrWhiteSpace(ConsentRedirectUri))
        {
            throw new InvalidOperationException("Onboarding:ConsentRedirectUri is required when consent is configured.");
        }

        if (consentConfigured &&
            (!Uri.TryCreate(ConsentRedirectUri, UriKind.Absolute, out var redirectUri) || redirectUri is null ||
             (redirectUri.Scheme != Uri.UriSchemeHttp && redirectUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("Onboarding:ConsentRedirectUri must be an absolute HTTP(S) URL.");
        }

        if (consentConfigured && string.IsNullOrWhiteSpace(ConsentSigningKey))
        {
            throw new InvalidOperationException("Onboarding:ConsentSigningKey is required when consent is configured.");
        }

        if (consentConfigured)
        {
            try
            {
                if (Convert.FromBase64String(ConsentSigningKey).Length < 32)
                {
                    throw new InvalidOperationException("Onboarding:ConsentSigningKey must decode to at least 32 bytes.");
                }
            }
            catch (FormatException exception)
            {
                throw new InvalidOperationException("Onboarding:ConsentSigningKey must be valid Base64.", exception);
            }
        }

        return publicUri;
    }

    private static bool IsHttpsOrLocalhost(Uri uri) =>
        uri.Scheme == Uri.UriSchemeHttps ||
        (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);

    private static bool TryReadSigningKey(string value, out byte[] key)
    {
        try
        {
            key = Convert.FromBase64String(value);
            return true;
        }
        catch (FormatException)
        {
            key = [];
            return false;
        }
    }
}
