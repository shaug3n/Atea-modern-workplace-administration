namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public sealed record FeedbackSubmissionRequest(string Category, string Subject, string Message);

public enum FeedbackCreateStatus
{
    Created,
    Replayed,
    KeyReused,
    KeyExpired
}

public sealed record FeedbackCreateResult(FeedbackCreateStatus Status, FeedbackSubmissionReceipt? Receipt);

public sealed record FeedbackSubmissionReceipt(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt);

public sealed record FeedbackPageResponse(IReadOnlyList<FeedbackSubmissionDto> Items, string? NextCursor);

public sealed record FeedbackSubmissionDto(
    Guid Id,
    string Category,
    string Subject,
    string Message,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);
