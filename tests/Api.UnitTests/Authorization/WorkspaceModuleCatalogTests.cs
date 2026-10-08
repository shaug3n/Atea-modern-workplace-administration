using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class WorkspaceModuleCatalogTests
{
    [Fact]
    public void Authentication_campaigns_is_known_but_is_not_a_core_module()
    {
        WorkspaceModuleCatalog.All.Should().Contain("authentication-campaigns");
        WorkspaceModuleCatalog.Core.Should().Equal("users", "devices", "licenses");
        WorkspaceModuleCatalog.Core.Should().NotContain("authentication-campaigns");
        WorkspaceModuleCatalog.IsKnown("authentication-campaigns").Should().BeTrue();
        WorkspaceModuleCatalog.ReservedModules.Should().NotContain("authentication-campaigns");
    }

    [Fact]
    public void Authentication_campaigns_can_be_explicitly_granted_and_enabled()
    {
        WorkspaceModuleCatalog.Normalize(["authentication-campaigns"]).Should().Contain("authentication-campaigns");
        WorkspaceModuleCatalog.EffectiveModules(
            "member",
            ["authentication-campaigns"],
            ["authentication-campaigns"]).Should().Contain("authentication-campaigns");
    }

    [Fact]
    public void Disabled_authentication_campaigns_are_not_effective_even_when_granted()
    {
        WorkspaceModuleCatalog.EffectiveModules(
            "member",
            ["users"],
            ["users", "authentication-campaigns"]).Should().NotContain("authentication-campaigns");
    }

    [Fact]
    public void Other_reserved_modules_remain_unknown_and_ungrantable()
    {
        WorkspaceModuleCatalog.ReservedModules.Should().Equal("license-hygiene", "about", "feedback");
        foreach (var module in WorkspaceModuleCatalog.ReservedModules)
        {
            WorkspaceModuleCatalog.All.Should().NotContain(module);
            WorkspaceModuleCatalog.Core.Should().NotContain(module);
            WorkspaceModuleCatalog.IsKnown(module).Should().BeFalse();
        }
        WorkspaceModuleCatalog.Normalize(WorkspaceModuleCatalog.ReservedModules).Should().BeEmpty();
    }

    [Fact]
    public void Owners_receive_enabled_modules_without_making_other_reserved_modules_available()
    {
        var effective = WorkspaceModuleCatalog.EffectiveModules(
            "owner",
            ["users", "devices", "licenses", "exchange", "authentication-campaigns", .. WorkspaceModuleCatalog.ReservedModules],
            WorkspaceModuleCatalog.ReservedModules);

        effective.Should().Contain("authentication-campaigns");
        foreach (var module in WorkspaceModuleCatalog.ReservedModules)
        {
            effective.Should().NotContain(module);
        }
    }
}
