using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed class OnboardingOptions
{
    public string PublicBaseUrl { get; set; } = string.Empty;
    public string ConsentRedirectUri { get; set; } = string.Empty;
    public string ConsentSigningKey { get; set; } = string.Empty;

    public Uri Validate(IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(PublicBaseUrl) ||
            string.Equals(PublicBaseUrl.TrimEnd('/'), "https://workplace.example", StringComparison.OrdinalIgnoreCase) ||
            !Uri.TryCreate(PublicBaseUrl, UriKind.Absolute, out var publicUri) ||
            publicUri is null ||
            publicUri.UserInfo.Length > 0 ||
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
}
