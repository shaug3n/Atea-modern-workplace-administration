using System.Data.Common;
using Atea.UnifiedWorkplace.Api.Features.Feedback;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;

public interface IFeedbackRetentionService
{
    Task<int> DeleteExpiredBatchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken);
}

public sealed class FeedbackRetentionService(WorkplaceDbContext db) : IFeedbackRetentionService
{
    public const int BatchSize = 500;

    public Task<int> DeleteExpiredBatchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken) =>
        db.FeedbackSubmissions
            .Where(submission => submission.ExpiresAt <= nowUtc)
            .OrderBy(submission => submission.ExpiresAt)
            .ThenBy(submission => submission.Id)
            .Take(BatchSize)
            .ExecuteDeleteAsync(cancellationToken);
}

public sealed class FeedbackRetentionWorker(
    IServiceScopeFactory scopeFactory,
    ILogger<FeedbackRetentionWorker> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        do
        {
            await CleanupAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var retentionService = scope.ServiceProvider.GetRequiredService<IFeedbackRetentionService>();
            int deleted;
            do
            {
                deleted = await retentionService.DeleteExpiredBatchAsync(DateTimeOffset.UtcNow, cancellationToken);
            }
            while (deleted == FeedbackRetentionService.BatchSize);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (DbException)
        {
            logger.LogError("Feedback retention cleanup failed.");
        }
        catch (TimeoutException)
        {
            logger.LogError("Feedback retention cleanup failed.");
        }
    }
}
