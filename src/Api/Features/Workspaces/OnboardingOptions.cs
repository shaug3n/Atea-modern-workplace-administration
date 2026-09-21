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

        if (!string.IsNullOrWhiteSpace(ConsentRedirectUri) &&
            (!Uri.TryCreate(ConsentRedirectUri, UriKind.Absolute, out var redirectUri) || redirectUri is null ||
             (redirectUri.Scheme != Uri.UriSchemeHttp && redirectUri.Scheme != Uri.UriSchemeHttps)))
        {
            throw new InvalidOperationException("Onboarding:ConsentRedirectUri must be an absolute HTTP(S) URL.");
        }

        if (!string.IsNullOrWhiteSpace(ConsentRedirectUri) && string.IsNullOrWhiteSpace(ConsentSigningKey))
        {
            throw new InvalidOperationException("Onboarding:ConsentSigningKey is required when consent is configured.");
        }

        return publicUri;
    }
}
