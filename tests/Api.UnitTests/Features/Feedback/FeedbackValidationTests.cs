using Atea.UnifiedWorkplace.Api.Features.Feedback;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Features.Feedback;

public sealed class FeedbackValidationTests
{
    [Fact]
    public void Category_accepts_only_the_three_exact_values()
    {
        foreach (var category in new[] { "Bug", "Improvement", "General" })
        {
            FeedbackValidation.Validate(new FeedbackSubmissionRequest(category, "Subject", "Message"))
                .Should().NotContainKey(nameof(FeedbackSubmissionRequest.Category));
        }

        foreach (var category in new[] { "", "bug", "BUG", "Feature", " Bug", "General " })
        {
            FeedbackValidation.Validate(new FeedbackSubmissionRequest(category, "Subject", "Message"))
                .Should().ContainKey(nameof(FeedbackSubmissionRequest.Category));
        }
    }

    [Fact]
    public void Subject_and_message_limits_match_UTF16_character_counts()
    {
        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            new string('s', 120),
            new string('m', 4_000))).Should().BeEmpty();

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            new string('s', 121),
            "Message")).Should().ContainKey(nameof(FeedbackSubmissionRequest.Subject));

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            "Subject",
            new string('m', 4_001))).Should().ContainKey(nameof(FeedbackSubmissionRequest.Message));

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            string.Concat(Enumerable.Repeat("😀", 60)),
            "Message")).Should().BeEmpty();

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            string.Concat(Enumerable.Repeat("😀", 61)),
            "Message")).Should().ContainKey(nameof(FeedbackSubmissionRequest.Subject));

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            "Subject",
            string.Concat(Enumerable.Repeat("😀", 2_000)))).Should().BeEmpty();

        FeedbackValidation.Validate(new FeedbackSubmissionRequest(
            "Bug",
            "Subject",
            string.Concat(Enumerable.Repeat("😀", 2_001)))).Should().ContainKey(nameof(FeedbackSubmissionRequest.Message));
    }

    [Fact]
    public void Whitespace_only_subject_or_message_is_rejected()
    {
        FeedbackValidation.Validate(new FeedbackSubmissionRequest("Bug", " \t\n", "Message"))
            .Should().ContainKey(nameof(FeedbackSubmissionRequest.Subject));

        FeedbackValidation.Validate(new FeedbackSubmissionRequest("Bug", "Subject", "\t \r"))
            .Should().ContainKey(nameof(FeedbackSubmissionRequest.Message));
    }
}
