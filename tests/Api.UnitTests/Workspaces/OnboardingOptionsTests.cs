using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class OnboardingOptionsTests
{
    [Fact]
    public void Accepts_local_http_public_url_in_development()
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "http://localhost:5173",
            ConsentRedirectUri = "http://localhost:5173/onboarding/consent/callback",
            ConsentSigningKey = Convert.ToBase64String(new byte[32])
        };

        options.Validate(new TestHostEnvironment("Development"));
    }

    [Fact]
    public void Rejects_placeholder_public_url()
    {
        var options = new OnboardingOptions { PublicBaseUrl = "https://workplace.example" };

        var action = () => options.Validate(new TestHostEnvironment("Development"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_invalid_or_relative_public_url()
    {
        var options = new OnboardingOptions { PublicBaseUrl = "/invitations" };

        var action = () => options.Validate(new TestHostEnvironment("Development"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_http_public_url_outside_development()
    {
        var options = new OnboardingOptions { PublicBaseUrl = "http://localhost:5173" };

        var action = () => options.Validate(new TestHostEnvironment("Production"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_missing_consent_signing_key_when_consent_redirect_is_configured()
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "http://localhost:5173",
            ConsentRedirectUri = "http://localhost:5173/onboarding/consent/callback"
        };

        var action = () => options.Validate(new TestHostEnvironment("Development"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData("not-base64")]
    [InlineData("AQIDBA==")]
    public void Rejects_malformed_or_short_consent_signing_key(string signingKey)
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "http://localhost:5173",
            ConsentRedirectUri = "http://localhost:5173/onboarding/consent/callback",
            ConsentSigningKey = signingKey
        };

        var action = () => options.Validate(new TestHostEnvironment("Development"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Rejects_missing_consent_redirect_when_consent_signing_key_is_configured()
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "http://localhost:5173",
            ConsentSigningKey = Convert.ToBase64String(new byte[32])
        };

        var action = () => options.Validate(new TestHostEnvironment("Development"));

        action.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Invitation_consent_requires_customer_client_api_uri_and_exact_public_callback()
    {
        var options = ValidInvitationOptions();

        options.IsInvitationConsentConfigured.Should().BeTrue();
        options.CustomerClientId = string.Empty;
        options.IsInvitationConsentConfigured.Should().BeFalse();

        options = ValidInvitationOptions();
        options.ApiApplicationIdUri = string.Empty;
        options.IsInvitationConsentConfigured.Should().BeFalse();

        options = ValidInvitationOptions();
        options.ConsentRedirectUri = "https://customer.example/onboarding/other";
        options.IsInvitationConsentConfigured.Should().BeFalse();
    }

    [Fact]
    public void Missing_optional_registration_values_disable_invitation_consent_without_breaking_startup()
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "https://customer.example"
        };

        options.IsInvitationConsentConfigured.Should().BeFalse();
        options.Validate(new TestHostEnvironment("Production")).AbsoluteUri
            .Should().Be("https://customer.example/");
    }

    [Fact]
    public void Rejects_non_local_http_and_non_exact_consent_redirect_outside_development()
    {
        var insecure = ValidInvitationOptions();
        insecure.ConsentRedirectUri = "http://customer.example/onboarding/consent/callback";

        var insecureAction = () => insecure.Validate(new TestHostEnvironment("Production"));

        insecureAction.Should().Throw<InvalidOperationException>();

        var wrongPath = ValidInvitationOptions();
        wrongPath.ConsentRedirectUri = "https://customer.example/callback";

        var pathAction = () => wrongPath.Validate(new TestHostEnvironment("Production"));

        pathAction.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Trusted_proxy_allowlist_is_disabled_when_any_entry_is_invalid()
    {
        var options = ValidInvitationOptions();
        options.TrustedProxyAddresses = "10.0.0.1,not-an-ip";

        var action = () => options.Validate(new TestHostEnvironment("Production"));

        action.Should().Throw<InvalidOperationException>();

        options.TrustedProxyAddresses = "10.0.0.1,2001:db8::1";
        options.Validate(new TestHostEnvironment("Production")).Should().BeOfType<Uri>();
        options.GetTrustedProxyAddresses().Select(address => address.ToString())
            .Should().Equal("10.0.0.1", "2001:db8::1");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static OnboardingOptions ValidInvitationOptions() => new()
    {
        PublicBaseUrl = "https://customer.example",
        ConsentRedirectUri = "https://customer.example/onboarding/consent/callback",
        ConsentSigningKey = Convert.ToBase64String(new byte[32]),
        CustomerClientId = "11111111-1111-1111-1111-111111111111",
        ApiApplicationIdUri = "api://22222222-2222-2222-2222-222222222222"
    };
}
