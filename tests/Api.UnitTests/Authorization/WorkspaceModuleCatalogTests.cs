using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class WorkspaceModuleCatalogTests
{
    [Fact]
    public void Existing_active_module_catalog_remains_unchanged()
    {
        WorkspaceModuleCatalog.All.Should().Equal("users", "devices", "licenses", "exchange");
        WorkspaceModuleCatalog.Core.Should().Equal("users", "devices", "licenses");
    }

    [Fact]
    public void Reserved_modules_are_not_known_or_normalized()
    {
        WorkspaceModuleCatalog.ReservedModules.Should().Equal(
            "authentication-campaigns",
            "license-hygiene",
            "about",
            "feedback");
        foreach (var module in WorkspaceModuleCatalog.ReservedModules)
        {
            WorkspaceModuleCatalog.All.Should().NotContain(module);
            WorkspaceModuleCatalog.Core.Should().NotContain(module);
        }
        WorkspaceModuleCatalog.ReservedModules.Should().OnlyContain(module => !WorkspaceModuleCatalog.IsKnown(module));
        WorkspaceModuleCatalog.Normalize(WorkspaceModuleCatalog.ReservedModules).Should().BeEmpty();
    }

    [Fact]
    public void Owners_do_not_receive_reserved_effective_modules()
    {
        var effective = WorkspaceModuleCatalog.EffectiveModules(
            "owner",
            ["users", "devices", "licenses", "exchange", .. WorkspaceModuleCatalog.ReservedModules],
            WorkspaceModuleCatalog.ReservedModules);

        effective.Should().Equal("users", "devices", "licenses", "exchange");
        foreach (var module in WorkspaceModuleCatalog.ReservedModules)
            effective.Should().NotContain(module);
    }
}
