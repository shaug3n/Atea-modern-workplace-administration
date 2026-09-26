using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;

public static class HostedConfigurationValidator
{
    private static readonly string[] OperatorAllowlistKeys = ["PlatformAuthorization:AdminObjectIds"];

    public static void Validate(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsDevelopment()) return;

        var errors = new List<string>();
        var clientId = Required(configuration, "AzureAd:ClientId", errors);
        Required(configuration, "AzureAd:Audience", errors);
        Required(configuration, "AzureAd:ClientSecret", errors);
        var tenantId = Required(configuration, "PlatformAuthorization:HomeTenantId", errors);
        Required(configuration, "ConnectionStrings:WorkplaceDb", errors);
        var consentKey = Required(configuration, "Onboarding:ConsentSigningKey", errors);
        var continuationKey = Required(configuration, "Users:ContinuationSigningKey", errors);
        var publicBaseUrl = Required(configuration, "Onboarding:PublicBaseUrl", errors);
        var consentRedirect = Required(configuration, "Onboarding:ConsentRedirectUri", errors);
        var customerRedirect = Required(configuration, "HostedAuth:CustomerRedirectUri", errors);
        var adminRedirect = Required(configuration, "HostedAuth:PlatformAdminRedirectUri", errors);
        var blobUri = Required(configuration, "DataProtection:BlobUri", errors);
        var keyIdentifier = Required(configuration, "DataProtection:KeyIdentifier", errors);
        var managedIdentityClientId = Required(configuration, "DataProtection:ManagedIdentityClientId", errors);

        if (!Guid.TryParse(clientId, out _)) errors.Add("AzureAd:ClientId");
        if (!Guid.TryParse(tenantId, out _)) errors.Add("PlatformAuthorization:HomeTenantId");
        if (!Guid.TryParse(managedIdentityClientId, out _)) errors.Add("DataProtection:ManagedIdentityClientId");

        var allowlist = configuration.GetSection(OperatorAllowlistKeys[0]).Get<string[]>() ?? [];
        if (allowlist.Length == 0 || allowlist.Any(id => !Guid.TryParse(id, out _))) errors.Add(OperatorAllowlistKeys[0]);

        if (consentKey is not null && !IsStrongBase64Key(consentKey)) errors.Add("Onboarding:ConsentSigningKey");
        if (continuationKey is not null && continuationKey.Length < 32) errors.Add("Users:ContinuationSigningKey");

        var baseUri = ParseHttps(publicBaseUrl, "Onboarding:PublicBaseUrl", errors);
        var consentUri = ParseHttps(consentRedirect, "Onboarding:ConsentRedirectUri", errors);
        var customerUri = ParseHttps(customerRedirect, "HostedAuth:CustomerRedirectUri", errors);
        var adminUri = ParseHttps(adminRedirect, "HostedAuth:PlatformAdminRedirectUri", errors);
        ParseHttps(blobUri, "DataProtection:BlobUri", errors);
        ParseHttps(keyIdentifier, "DataProtection:KeyIdentifier", errors);

        if (baseUri is not null)
        {
            if (baseUri.AbsolutePath != "/" || !string.IsNullOrEmpty(baseUri.Query) || !string.IsNullOrEmpty(baseUri.Fragment)) errors.Add("Onboarding:PublicBaseUrl");
            foreach (var (uri, key) in new[]
            {
                (consentUri, "Onboarding:ConsentRedirectUri"),
                (customerUri, "HostedAuth:CustomerRedirectUri"),
                (adminUri, "HostedAuth:PlatformAdminRedirectUri")
            })
            {
                if (uri is not null && !SameOrigin(baseUri, uri)) errors.Add(key);
            }
        }

        if (consentUri is not null && customerUri is not null && adminUri is not null)
        {
            if (consentUri.AbsoluteUri == customerUri.AbsoluteUri || consentUri.AbsoluteUri == adminUri.AbsoluteUri || customerUri.AbsoluteUri == adminUri.AbsoluteUri)
                errors.Add("Onboarding:ConsentRedirectUri, HostedAuth:CustomerRedirectUri, HostedAuth:PlatformAdminRedirectUri");
            if (consentUri.AbsolutePath == customerUri.AbsolutePath || consentUri.AbsolutePath == adminUri.AbsolutePath || customerUri.AbsolutePath == adminUri.AbsolutePath)
                errors.Add("Onboarding:ConsentRedirectUri, HostedAuth:CustomerRedirectUri, HostedAuth:PlatformAdminRedirectUri");
        }

        if (configuration.GetValue<bool>("AteaAdmin:LocalDevelopment:Enabled") ||
            !string.IsNullOrWhiteSpace(configuration["AteaAdmin:LocalDevelopment:Username"]) ||
            !string.IsNullOrWhiteSpace(configuration["AteaAdmin:LocalDevelopment:Password"]) ||
            !string.IsNullOrWhiteSpace(configuration["AteaAdmin:LocalDevelopment:ObjectId"]) ||
            !string.IsNullOrWhiteSpace(configuration["AteaAdmin:LocalDevelopment:DisplayName"]))
            errors.Add("AteaAdmin:LocalDevelopment");

        if (errors.Count > 0)
        {
            var distinctErrors = errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
            throw new InvalidOperationException($"Hosted configuration is invalid. Check: {string.Join(", ", distinctErrors)}.");
        }
    }

    private static string? Required(IConfiguration configuration, string key, ICollection<string> errors)
    {
        var value = configuration[key]?.Trim();
        if (string.IsNullOrWhiteSpace(value)) errors.Add(key);
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static Uri? ParseHttps(string? value, string key, ICollection<string> errors)
    {
        if (value is null) return null;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(uri.UserInfo))
        {
            errors.Add(key);
            return null;
        }
        return uri;
    }

    private static bool SameOrigin(Uri left, Uri right) =>
        string.Equals(left.Scheme, right.Scheme, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(left.Host, right.Host, StringComparison.OrdinalIgnoreCase) && left.Port == right.Port;

    private static bool IsStrongBase64Key(string value)
    {
        try { return Convert.FromBase64String(value).Length >= 32; }
        catch (FormatException) { return false; }
    }
}

public sealed class HostedConfigurationValidationService(IConfiguration configuration, IHostEnvironment environment) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        HostedConfigurationValidator.Validate(configuration, environment);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
