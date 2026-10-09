namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public static class FeedbackValidation
{
    private static readonly HashSet<string> Categories = new(StringComparer.Ordinal)
    {
        "Bug",
        "Improvement",
        "General"
    };

    public static IReadOnlyDictionary<string, string> Validate(FeedbackSubmissionRequest request)
    {
        var errors = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!Categories.Contains(request.Category ?? string.Empty))
            errors[nameof(request.Category)] = "invalid_category";
        if (string.IsNullOrWhiteSpace(request.Subject))
            errors[nameof(request.Subject)] = "required";
        else if (request.Subject.Length > 120)
            errors[nameof(request.Subject)] = "too_long";
        if (string.IsNullOrWhiteSpace(request.Message))
            errors[nameof(request.Message)] = "required";
        else if (request.Message.Length > 4_000)
            errors[nameof(request.Message)] = "too_long";
        return errors;
    }
}
