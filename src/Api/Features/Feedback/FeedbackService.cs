using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public interface IFeedbackService
{
    Task<FeedbackCreateResult> CreateAsync(
        WorkspaceContext context,
        FeedbackSubmissionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken);

    Task<FeedbackPageResponse> GetMineAsync(WorkspaceContext context, string? cursor, CancellationToken cancellationToken);
}

public sealed class FeedbackService(WorkplaceDbContext db, IAuditWriter auditWriter) : IFeedbackService
{
    private const string RetryKeyIndex = "IX_FeedbackSubmissions_RetryKey";
    private const int PageSize = 20;
    private static readonly TimeSpan RetentionPeriod = TimeSpan.FromDays(90);

    public async Task<FeedbackCreateResult> CreateAsync(
        WorkspaceContext context,
        FeedbackSubmissionRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var retryKeyHash = Hash(idempotencyKey);
        var fingerprint = Fingerprint(request);
        var nowUtc = DateTimeOffset.UtcNow;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var existing = await FindByRetryKeyAsync(context, retryKeyHash, cancellationToken);
        if (existing is not null)
            return ResolveExisting(existing, fingerprint, nowUtc);

        var submission = new FeedbackSubmission
        {
            Id = Guid.NewGuid(),
            WorkspaceId = context.Membership.WorkspaceId,
            SubmitterObjectId = context.User.ObjectId,
            Category = request.Category,
            Subject = request.Subject,
            Message = request.Message,
            CreatedAt = nowUtc,
            ExpiresAt = nowUtc.Add(RetentionPeriod),
            RetryKeyHash = retryKeyHash,
            PayloadFingerprint = fingerprint
        };
        db.FeedbackSubmissions.Add(submission);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsRetryKeyUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.Entry(submission).State = EntityState.Detached;
            existing = await FindByRetryKeyAsync(context, retryKeyHash, cancellationToken);
            if (existing is null)
                throw;
            return ResolveExisting(existing, fingerprint, nowUtc);
        }

        await auditWriter.WriteAsync(new AuditEvent
        {
            WorkspaceId = context.Membership.WorkspaceId,
            TenantId = context.User.TenantId,
            ActorTenantId = context.User.TenantId,
            ActorObjectId = context.User.ObjectId,
            Action = "feedback.submitted",
            TargetType = "feedback_submission",
            TargetId = submission.Id.ToString("D"),
            Outcome = "success",
            Timestamp = nowUtc,
            SafeMetadataJson = "{}"
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new FeedbackCreateResult(
            FeedbackCreateStatus.Created,
            new FeedbackSubmissionReceipt(submission.Id, submission.CreatedAt, submission.ExpiresAt));
    }

    public async Task<FeedbackPageResponse> GetMineAsync(
        WorkspaceContext context,
        string? cursor,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTimeOffset.UtcNow;
        var query = db.FeedbackSubmissions
            .AsNoTracking()
            .Where(submission =>
                submission.WorkspaceId == context.Membership.WorkspaceId
                && submission.SubmitterObjectId == context.User.ObjectId
                && submission.ExpiresAt > nowUtc);
        if (cursor is not null)
        {
            if (!FeedbackCursor.TryDecode(cursor, out var createdAt, out var id))
                throw new ArgumentException("invalid_cursor", nameof(cursor));
            query = query.Where(submission =>
                submission.CreatedAt < createdAt
                || (submission.CreatedAt == createdAt && submission.Id.CompareTo(id) < 0));
        }

        var rows = await query
            .OrderByDescending(submission => submission.CreatedAt)
            .ThenByDescending(submission => submission.Id)
            .Take(PageSize + 1)
            .Select(submission => new FeedbackSubmissionDto(
                submission.Id,
                submission.Category,
                submission.Subject,
                submission.Message,
                submission.CreatedAt,
                submission.ExpiresAt))
            .ToListAsync(cancellationToken);
        var hasNextPage = rows.Count > PageSize;
        var items = rows.Take(PageSize).ToArray();
        var nextCursor = hasNextPage
            ? FeedbackCursor.Encode(items[^1].CreatedAt, items[^1].Id)
            : null;
        return new FeedbackPageResponse(items, nextCursor);
    }

    private Task<FeedbackSubmission?> FindByRetryKeyAsync(
        WorkspaceContext context,
        string retryKeyHash,
        CancellationToken cancellationToken) =>
        db.FeedbackSubmissions.AsNoTracking().SingleOrDefaultAsync(
            submission =>
                submission.WorkspaceId == context.Membership.WorkspaceId
                && submission.SubmitterObjectId == context.User.ObjectId
                && submission.RetryKeyHash == retryKeyHash,
            cancellationToken);

    private static FeedbackCreateResult ResolveExisting(
        FeedbackSubmission existing,
        string fingerprint,
        DateTimeOffset nowUtc)
    {
        if (existing.ExpiresAt <= nowUtc)
            return new FeedbackCreateResult(FeedbackCreateStatus.KeyExpired, null);
        if (!CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(existing.PayloadFingerprint),
                Convert.FromHexString(fingerprint)))
            return new FeedbackCreateResult(FeedbackCreateStatus.KeyReused, null);
        return new FeedbackCreateResult(
            FeedbackCreateStatus.Replayed,
            new FeedbackSubmissionReceipt(existing.Id, existing.CreatedAt, existing.ExpiresAt));
    }

    private static bool IsRetryKeyUniqueViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: RetryKeyIndex
        };

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static string Fingerprint(FeedbackSubmissionRequest request) =>
        Hash(JsonSerializer.Serialize(new[] { request.Category, request.Subject, request.Message }));
}

internal static class FeedbackCursor
{
    public static string Encode(DateTimeOffset createdAt, Guid id)
    {
        var value = $"{createdAt.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:N}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(value))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string cursor, out DateTimeOffset createdAt, out Guid id)
    {
        createdAt = default;
        id = default;
        if (cursor.Length is 0 or > 128)
            return false;
        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - base64.Length % 4) % 4), '=');
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var parts = value.Split(':', StringSplitOptions.None);
            if (parts.Length != 2
                || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                || ticks < DateTime.MinValue.Ticks
                || ticks > DateTime.MaxValue.Ticks
                || !Guid.TryParseExact(parts[1], "N", out id))
                return false;
            createdAt = new DateTimeOffset(ticks, TimeSpan.Zero);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
