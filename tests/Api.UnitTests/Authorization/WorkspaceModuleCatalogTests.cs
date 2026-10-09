using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class WorkspaceModuleCatalogTests
{
    [Fact]
    public void Shipped_modules_are_known_without_changing_the_core_set()
    {
        WorkspaceModuleCatalog.All.Should().Equal(
            "users",
            "devices",
            "licenses",
            "exchange",
            "authentication-campaigns",
            "license-hygiene",
            "about",
            "feedback");
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
        WorkspaceModuleCatalog.ReservedModules.Should().NotContain("license-hygiene");
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
    public void Shipped_modules_are_not_reserved()
    {
        WorkspaceModuleCatalog.ReservedModules.Should().BeEmpty();
        WorkspaceModuleCatalog.Normalize(WorkspaceModuleCatalog.ReservedModules).Should().BeEmpty();
    }

    [Fact]
    public void About_and_feedback_are_known_and_normalized_but_not_implicitly_enabled()
    {
        WorkspaceModuleCatalog.IsKnown("about").Should().BeTrue();
        WorkspaceModuleCatalog.IsKnown("feedback").Should().BeTrue();
        WorkspaceModuleCatalog.All.Should().Equal(
            "users",
            "devices",
            "licenses",
            "exchange",
            "authentication-campaigns",
            "license-hygiene",
            "about",
            "feedback");
        WorkspaceModuleCatalog.Core.Should().Equal("users", "devices", "licenses");
        WorkspaceModuleCatalog.Normalize(["FEEDBACK", " About ", "unknown"])
            .Should().Equal("about", "feedback");

        WorkspaceModuleCatalog.EffectiveModules("owner", ["users", "devices", "licenses", "exchange"], [])
            .Should().Equal("users", "devices", "licenses", "exchange");
        WorkspaceModuleCatalog.EffectiveModules("member", ["about", "feedback"], [])
            .Should().BeEmpty();
        WorkspaceModuleCatalog.EffectiveModules("member", ["about", "feedback"], ["feedback"])
            .Should().Equal("feedback");
    }
}
