namespace Atea.UnifiedWorkplace.Api.Features.Licenses;

/// <summary>
/// Version 2026-09-25. Product names are checked against Microsoft's licensing
/// service-plan reference: https://learn.microsoft.com/en-us/entra/identity/users/licensing-service-plan-reference
/// Update this list only after checking the part number and product name there.
/// Unknown part numbers deliberately have no inferred product name.
/// </summary>
public static class LicenseDisplayNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> Names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["SPE_E5"] = "Microsoft 365 E5",
        ["SPE_E3"] = "Microsoft 365 E3",
        ["DEVELOPERPACK_E5"] = "Microsoft 365 E5 Developer (without Windows and Audio Conferencing)",
        ["ENTERPRISEPREMIUM"] = "Office 365 E5",
        ["ENTERPRISEPACK"] = "Office 365 E3",
        ["VISIOCLIENT"] = "Visio Plan 2",
    };

    public static string? Resolve(string partNumber) => Names.TryGetValue(partNumber, out var name) ? name : null;
}
