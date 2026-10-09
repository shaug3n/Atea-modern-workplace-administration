using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class WorkspaceModuleCatalogTests
{
    [Fact]
    public void Authentication_campaigns_is_known_but_is_not_a_core_module()
    {
        WorkspaceModuleCatalog.All.Should().Contain("authentication-campaigns");
        WorkspaceModuleCatalog.All.Should().Contain("license-hygiene");
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
    public void Hygiene_module_is_opt_in_and_existing_grants_do_not_gain_it()
    {
        WorkspaceModuleCatalog.ReservedModules.Should().Equal("about", "feedback");
        WorkspaceModuleCatalog.All.Should().Contain("license-hygiene");
        WorkspaceModuleCatalog.Core.Should().Equal("users", "devices", "licenses");
        WorkspaceModuleCatalog.IsKnown("license-hygiene").Should().BeTrue();
        WorkspaceModuleCatalog.ReservedModules.Should().OnlyContain(module => !WorkspaceModuleCatalog.IsKnown(module));

        var oldEnabledModules = WorkspaceModuleCatalog.Normalize(["users", "devices", "licenses", "exchange"]);
        oldEnabledModules.Should().Equal("users", "devices", "licenses", "exchange");
        oldEnabledModules.Should().NotContain("license-hygiene");
        WorkspaceModuleCatalog.Normalize(["license-hygiene"]).Should().Equal("license-hygiene");

        var ownerWithoutOptIn = WorkspaceModuleCatalog.EffectiveModules(
            "owner",
            ["users", "devices", "licenses", "exchange"],
            ["users", "devices", "licenses", "exchange"]);
        ownerWithoutOptIn.Should().Equal("users", "devices", "licenses", "exchange");
        ownerWithoutOptIn.Should().NotContain("license-hygiene");

        WorkspaceModuleCatalog.EffectiveModules(
            "owner",
            ["users", "devices", "licenses", "exchange", "license-hygiene"],
            [])
            .Should().Contain("license-hygiene");

        var previouslyGrantedMember = WorkspaceModuleCatalog.EffectiveModules(
            "member",
            ["users", "devices", "licenses", "exchange"],
            ["users", "devices", "licenses", "exchange"]);
        previouslyGrantedMember.Should().Equal("users", "devices", "licenses", "exchange");
        previouslyGrantedMember.Should().NotContain("license-hygiene");

        WorkspaceModuleCatalog.EffectiveModules(
            "member",
            ["users", "devices", "licenses", "exchange", "license-hygiene"],
            ["users", "devices", "licenses", "exchange"])
            .Should().NotContain("license-hygiene");
        WorkspaceModuleCatalog.EffectiveModules(
            "member",
            ["users", "devices", "licenses", "exchange", "license-hygiene"],
            ["users", "devices", "licenses", "exchange", "license-hygiene"])
            .Should().Contain("license-hygiene");
    }

    [Fact]
    public void Other_reserved_modules_remain_unknown_and_ungrantable()
    {
        WorkspaceModuleCatalog.ReservedModules.Should().Equal("about", "feedback");
        foreach (var module in WorkspaceModuleCatalog.ReservedModules)
        {
            WorkspaceModuleCatalog.All.Should().NotContain(module);
            WorkspaceModuleCatalog.Core.Should().NotContain(module);
            WorkspaceModuleCatalog.IsKnown(module).Should().BeFalse();
        }
        WorkspaceModuleCatalog.Normalize(WorkspaceModuleCatalog.ReservedModules).Should().BeEmpty();
    }
}
