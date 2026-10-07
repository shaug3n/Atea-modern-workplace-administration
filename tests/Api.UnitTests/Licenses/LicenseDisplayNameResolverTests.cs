using Atea.UnifiedWorkplace.Api.Features.Licenses;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Licenses;

public sealed class LicenseDisplayNameResolverTests
{
    [Theory]
    [InlineData("EMSPREMIUM", "Enterprise Mobility + Security E5")]
    [InlineData("EMS", "Enterprise Mobility + Security E3")]
    [InlineData("AAD_PREMIUM", "Microsoft Entra ID P1")]
    [InlineData("AAD_PREMIUM_P2", "Microsoft Entra ID P2")]
    [InlineData("SPB", "Microsoft 365 Business Premium")]
    public void Resolves_verified_part_numbers_case_insensitively(string partNumber, string expected)
    {
        LicenseDisplayNameResolver.Resolve(partNumber).Should().Be(expected);
        LicenseDisplayNameResolver.Resolve(partNumber.ToLowerInvariant()).Should().Be(expected);
    }

    [Fact]
    public void Unknown_part_number_has_no_inferred_name() =>
        LicenseDisplayNameResolver.Resolve("NOT_A_REAL_SKU").Should().BeNull();
}
