namespace Atea.UnifiedWorkplace.Api.Features.Users;

public static class UserWriteReasonValidation
{
    public const int MaximumLength = 1000;

    public static bool TryNormalize(string? reason, out string normalized, out string error)
    {
        normalized = reason?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
        {
            error = "reason_required";
            return false;
        }

        if (normalized.Length > MaximumLength)
        {
            error = "reason_too_long";
            return false;
        }

        error = string.Empty;
        return true;
    }
}
