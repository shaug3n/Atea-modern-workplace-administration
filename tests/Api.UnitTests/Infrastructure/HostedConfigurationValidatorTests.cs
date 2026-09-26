using Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure;

public sealed class HostedConfigurationValidatorTests
{
    private static readonly Dictionary<string, string?> ValidHostedSettings = new()
    {
        ["AzureAd:ClientId"] = "11111111-1111-1111-1111-111111111111",
        ["AzureAd:Audience"] = "api://11111111-1111-1111-1111-111111111111",
        ["AzureAd:ClientSecret"] = "secret-value-that-must-not-appear-in-errors",
        ["PlatformAuthorization:HomeTenantId"] = "22222222-2222-2222-2222-222222222222",
        ["PlatformAuthorization:AdminObjectIds:0"] = "33333333-3333-3333-3333-333333333333",
        ["ConnectionStrings:WorkplaceDb"] = "Host=localhost;Database=workplace;Username=workplace;Password=db-secret",
        ["Onboarding:ConsentSigningKey"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(value => (byte)value).ToArray()),
        ["Users:ContinuationSigningKey"] = "01234567890123456789012345678901",
        ["Onboarding:PublicBaseUrl"] = "https://workplace.example",
        ["Onboarding:ConsentRedirectUri"] = "https://workplace.example/onboarding/consent/callback",
        ["HostedAuth:CustomerRedirectUri"] = "https://workplace.example/auth/callback",
        ["HostedAuth:PlatformAdminRedirectUri"] = "https://workplace.example/admin/auth/callback",
        ["DataProtection:BlobUri"] = "https://keys.blob.core.windows.net/dp/keys.xml",
        ["DataProtection:KeyIdentifier"] = "https://vault.vault.azure.net/keys/dp-key/version",
        ["DataProtection:ManagedIdentityClientId"] = "44444444-4444-4444-4444-444444444444"
    };

    [Fact]
    public void Valid_staging_configuration_is_accepted()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(ValidHostedSettings).Build();

        var act = () => HostedConfigurationValidator.Validate(configuration, Environment("Staging"));

        act.Should().NotThrow();
    }

    [Fact]
    public void Missing_and_malformed_hosted_values_fail_without_echoing_secrets()
    {
        var settings = new Dictionary<string, string?>(ValidHostedSettings)
        {
            ["AzureAd:ClientSecret"] = "secret-value-that-must-not-appear-in-errors",
            ["Onboarding:PublicBaseUrl"] = "http://workplace.example",
            ["PlatformAuthorization:HomeTenantId"] = "not-a-guid",
            ["ConnectionStrings:WorkplaceDb"] = null
        };
        settings.Remove("DataProtection:KeyIdentifier");
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var exception = FluentActions.Invoking(() => HostedConfigurationValidator.Validate(configuration, Environment("Staging")))
            .Should().Throw<InvalidOperationException>().Which;

        exception.Message.Should().Contain("Onboarding:PublicBaseUrl")
            .And.Contain("PlatformAuthorization:HomeTenantId")
            .And.Contain("ConnectionStrings:WorkplaceDb")
            .And.Contain("DataProtection:KeyIdentifier")
            .And.NotContain("secret-value-that-must-not-appear-in-errors")
            .And.NotContain("db-secret");
    }

    [Fact]
    public void Hosted_redirect_uris_must_share_origin_and_use_distinct_paths()
    {
        var settings = new Dictionary<string, string?>(ValidHostedSettings)
        {
            ["HostedAuth:PlatformAdminRedirectUri"] = "https://other.example/admin/auth/callback"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        FluentActions.Invoking(() => HostedConfigurationValidator.Validate(configuration, Environment("Production")))
            .Should().Throw<InvalidOperationException>().WithMessage("*HostedAuth:PlatformAdminRedirectUri*");
    }

    [Fact]
    public void Hosted_environment_rejects_local_password_login_settings()
    {
        var settings = new Dictionary<string, string?>(ValidHostedSettings)
        {
            ["AteaAdmin:LocalDevelopment:Enabled"] = "true"
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        FluentActions.Invoking(() => HostedConfigurationValidator.Validate(configuration, Environment("Staging")))
            .Should().Throw<InvalidOperationException>().WithMessage("*AteaAdmin:LocalDevelopment*");
    }

    [Fact]
    public void Development_keeps_local_fixture_configuration_unrestricted()
    {
        var configuration = new ConfigurationBuilder().Build();

        FluentActions.Invoking(() => HostedConfigurationValidator.Validate(configuration, Environment("Development")))
            .Should().NotThrow();
    }

    private static IHostEnvironment Environment(string name) => new TestHostEnvironment
    {
        EnvironmentName = name,
        ApplicationName = "Atea.UnifiedWorkplace.Api",
        ContentRootPath = AppContext.BaseDirectory,
        ContentRootFileProvider = new NullFileProvider()
    };

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = string.Empty;
        public string ApplicationName { get; set; } = string.Empty;
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
