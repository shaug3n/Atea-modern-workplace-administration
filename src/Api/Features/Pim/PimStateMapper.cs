namespace Atea.UnifiedWorkplace.Api.Features.Pim;

public static class PimStateMapper
{
    public static string ToPimRequirement(DateTimeOffset? endDateTime, DateTimeOffset? now = null) =>
        endDateTime is not null && endDateTime <= (now ?? DateTimeOffset.UtcNow)
            ? "eligibility_expired"
            : "activation_required";

    public static string ToCapabilityState(string? graphStatus) => ToPimRequirement(graphStatus) switch
    {
        "approval_required" => "pim_approval_required",
        "mfa_required" => "pim_mfa_required",
        "eligibility_expired" => "pim_eligibility_expired",
        "activation_required" => "pim_activation_required",
        _ => "temporarily_unavailable"
    };

    public static string ToPimRequirement(string? graphStatus)
    {
        if (string.IsNullOrWhiteSpace(graphStatus))
        {
            return "temporarily_unavailable";
        }

        var normalized = graphStatus.Trim();
        if (normalized.Contains("Approval", StringComparison.OrdinalIgnoreCase))
        {
            return "approval_required";
        }

        if (normalized.Contains("Mfa", StringComparison.OrdinalIgnoreCase) || normalized.Contains("MultiFactor", StringComparison.OrdinalIgnoreCase))
        {
            return "mfa_required";
        }

        if (normalized.Contains("Expired", StringComparison.OrdinalIgnoreCase))
        {
            return "eligibility_expired";
        }

        return normalized.Equals("Eligible", StringComparison.OrdinalIgnoreCase) || normalized.Equals("Active", StringComparison.OrdinalIgnoreCase)
            ? "activation_required"
            : "temporarily_unavailable";
    }
}
