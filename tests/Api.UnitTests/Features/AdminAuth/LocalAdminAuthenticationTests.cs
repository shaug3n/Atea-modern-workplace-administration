using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using FluentAssertions;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Features.AdminAuth;

public sealed class LocalAdminAuthenticationTests
{
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void Correct_credentials_create_platform_admin_principal()
    {
        var options = Options();

        var principal = LocalAdminAuthentication.Authenticate(options, options.Username, options.Password);

        principal.Should().NotBeNull();
        principal!.FindFirstValue("oid").Should().Be(ObjectId.ToString());
        principal.FindFirstValue("name").Should().Be("Local Atea Admin");
        principal.IsInRole("PlatformAdmin").Should().BeTrue();
        principal.FindFirst("tid").Should().BeNull();
    }

    [Fact]
    public void Wrong_credentials_fail()
    {
        LocalAdminAuthentication.Authenticate(Options(), "admin", "wrong").Should().BeNull();
    }

    [Fact]
    public void Missing_required_configuration_disables_local_authentication()
    {
        LocalAdminAuthentication.IsConfigured(Development(), Options(username: "", password: "secret", objectId: ObjectId.ToString())).Should().BeFalse();
        LocalAdminAuthentication.IsConfigured(Development(), Options(username: "admin", password: "", objectId: ObjectId.ToString())).Should().BeFalse();
        LocalAdminAuthentication.IsConfigured(Development(), Options(username: "admin", password: "secret", objectId: "not-a-guid")).Should().BeFalse();
    }

    [Fact]
    public void Local_all_workspaces_is_ignored_outside_development()
    {
        var options = Options(allowAllWorkspaces: true);

        LocalAdminAuthentication.IsConfigured(new TestHostEnvironment("Production"), options).Should().BeFalse();
        LocalAdminAuthentication.IsConfigured(Development(), options).Should().BeTrue();
        LocalAdminAuthentication.IsAllWorkspacesAllowed(new TestHostEnvironment("Production"), options).Should().BeFalse();
    }

    [Fact]
    public void Gate_is_required_even_in_development()
    {
        LocalAdminAuthentication.IsConfigured(Development(), Options(enabled: false)).Should().BeFalse();
    }

    [Fact]
    public void Enabled_local_authentication_is_rejected_outside_development()
    {
        var act = () => LocalAdminAuthentication.ValidateEnvironment(new TestHostEnvironment("Production"), Options());

        act.Should().Throw<InvalidOperationException>().WithMessage("*Development*");
    }

    [Fact]
    public void Any_local_authentication_settings_are_rejected_outside_development()
    {
        var act = () => LocalAdminAuthentication.ValidateEnvironment(new TestHostEnvironment("Production"), Options(enabled: false, password: "configured"));

        act.Should().Throw<InvalidOperationException>();
    }

    private static LocalAdminOptions Options(string username = "admin", string password = "secret", string objectId = "22222222-2222-2222-2222-222222222222", bool enabled = true, bool allowAllWorkspaces = false) => new()
    {
        Enabled = enabled,
        Username = username,
        Password = password,
        ObjectId = objectId,
        DisplayName = "Local Atea Admin",
        AllowAllWorkspaces = allowAllWorkspaces
    };

    private static IHostEnvironment Development() => new TestHostEnvironment("Development");

    private sealed class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
