using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using FluentAssertions;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Features.AdminAuth;

public sealed class LocalAdminEnvironmentTests
{
    [Theory]
    [InlineData("Staging")]
    [InlineData("Production")]
    public void Local_password_login_is_never_configured_outside_development(string environmentName)
    {
        var environment = new TestHostEnvironment(environmentName);
        var options = new LocalAdminOptions
        {
            Enabled = true,
            Username = "operator",
            Password = "local-only",
            ObjectId = "11111111-1111-1111-1111-111111111111",
            AllowAllWorkspaces = true
        };

        LocalAdminAuthentication.IsConfigured(environment, options).Should().BeFalse();
        LocalAdminAuthentication.IsAllWorkspacesAllowed(environment, options).Should().BeFalse();
        FluentActions.Invoking(() => LocalAdminAuthentication.ValidateEnvironment(environment, options))
            .Should().Throw<InvalidOperationException>().WithMessage("*only supported in the Development environment*");
    }

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Atea.UnifiedWorkplace.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
