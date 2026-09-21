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

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
