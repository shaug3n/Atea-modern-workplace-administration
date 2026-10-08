namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

public sealed class FeedbackSubmission
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid WorkspaceId { get; set; }
    public Guid SubmitterObjectId { get; set; }
    public string Category { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string RetryKeyHash { get; set; } = string.Empty;
    public string PayloadFingerprint { get; set; } = string.Empty;
}
