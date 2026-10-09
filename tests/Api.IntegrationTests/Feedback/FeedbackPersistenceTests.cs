using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Feedback;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit.Sdk;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Feedback;

public sealed class FeedbackPersistenceTests : IAsyncLifetime
{
    private static readonly Guid WorkspaceA = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid WorkspaceB = Guid.Parse("66666666-6666-6666-6666-666666666666");
    private static readonly Guid UserA = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid UserB = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTimeOffset InitialUtc = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public async Task InitializeAsync()
    {
        try
        {
            await postgres.StartAsync();
            await using var db = CreateDbContext();
            await db.Database.MigrateAsync();
            db.Workspaces.AddRange(
                CreateWorkspace(WorkspaceA, Guid.Parse("11111111-1111-1111-1111-111111111111")),
                CreateWorkspace(WorkspaceB, Guid.Parse("44444444-4444-4444-4444-444444444444")));
            await db.SaveChangesAsync();
        }
        catch (DockerUnavailableException exception)
        {
            throw SkipException.ForSkip($"Docker daemon unavailable: {exception.Message}");
        }
    }

    public async Task DisposeAsync() => await postgres.DisposeAsync();

    [Fact]
    public async Task Created_submission_expires_exactly_90_UTC_days_later()
    {
        var localClock = new DateTimeOffset(2026, 10, 8, 14, 30, 0, TimeSpan.FromHours(2));
        await using var db = CreateDbContext();
        var result = await CreateService(db, () => localClock).CreateAsync(
            Context(WorkspaceA, UserA),
            new FeedbackSubmissionRequest("Bug", "Subject", "Message"),
            "non-utc-clock-key",
            CancellationToken.None);

        result.Status.Should().Be(FeedbackCreateStatus.Created);
        result.Receipt!.CreatedAt.Should().Be(InitialUtc.AddMinutes(30));
        result.Receipt.CreatedAt.Offset.Should().Be(TimeSpan.Zero);
        result.Receipt.ExpiresAt.Should().Be(result.Receipt.CreatedAt.AddDays(90));
        result.Receipt.ExpiresAt.Offset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public async Task Expired_at_now_is_hidden_before_cleanup()
    {
        var now = InitialUtc;
        await using var db = CreateDbContext();
        var service = CreateService(db, () => now);
        await service.CreateAsync(
            Context(WorkspaceA, UserA),
            new FeedbackSubmissionRequest("Bug", "Subject", "Message"),
            "boundary-key",
            CancellationToken.None);

        var justBeforeExpiry = InitialUtc.AddDays(90).AddTicks(-1);
        now = justBeforeExpiry;
        (await service.GetMineAsync(Context(WorkspaceA, UserA), null, CancellationToken.None))
            .Items.Should().ContainSingle();
        now = InitialUtc.AddDays(90);
        (await service.GetMineAsync(Context(WorkspaceA, UserA), null, CancellationToken.None))
            .Items.Should().BeEmpty();
        (await db.FeedbackSubmissions.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Cleanup_deletes_expired_submission_and_retry_metadata_in_bounded_batches()
    {
        await using var db = CreateDbContext();
        var expiredAt = InitialUtc.AddDays(90);
        var submissions = Enumerable.Range(0, 501)
            .Select(index => CreateSubmission(
                Guid.NewGuid(),
                WorkspaceA,
                UserA,
                InitialUtc,
                expiredAt,
                index.ToString("x64")))
            .Append(CreateSubmission(
                Guid.NewGuid(),
                WorkspaceA,
                UserA,
                InitialUtc,
                expiredAt.AddDays(1),
                "f".PadLeft(64, 'f')))
            .ToArray();
        db.FeedbackSubmissions.AddRange(submissions);
        await db.SaveChangesAsync();

        var cleanup = new FeedbackRetentionService(db);
        (await cleanup.DeleteExpiredBatchAsync(expiredAt, CancellationToken.None)).Should().Be(500);
        (await db.FeedbackSubmissions.CountAsync()).Should().Be(2);
        (await cleanup.DeleteExpiredBatchAsync(expiredAt, CancellationToken.None)).Should().Be(1);

        var remaining = await db.FeedbackSubmissions.ToListAsync();
        remaining.Should().ContainSingle();
        remaining[0].ExpiresAt.Should().BeAfter(expiredAt);
        remaining[0].RetryKeyHash.Should().Be("f".PadLeft(64, 'f'));
        remaining.Should().NotContain(submission => submission.RetryKeyHash.Length != 64);
    }

    [Fact]
    public async Task Keyset_pages_are_newest_first_and_stable_when_created_at_ties()
    {
        await using var db = CreateDbContext();
        var tiedCreatedAt = InitialUtc.AddMinutes(1);
        var submissions = Enumerable.Range(0, 21)
            .Select(index => CreateSubmission(
                new Guid(index + 1, 0, 0, new byte[8]),
                WorkspaceA,
                UserA,
                tiedCreatedAt,
                tiedCreatedAt.AddDays(90),
                index.ToString("x64")))
            .ToArray();
        db.FeedbackSubmissions.AddRange(submissions);
        await db.SaveChangesAsync();

        var service = CreateService(db, () => InitialUtc.AddMinutes(2));
        var firstPage = await service.GetMineAsync(Context(WorkspaceA, UserA), null, CancellationToken.None);
        firstPage.Items.Should().HaveCount(20);
        firstPage.Items.Select(item => item.Id).Should()
            .Equal(submissions.Select(item => item.Id).OrderByDescending(id => id).Take(20));
        firstPage.Items.Select(item => item.CreatedAt).Should().OnlyContain(createdAt => createdAt == tiedCreatedAt);

        var secondPage = await service.GetMineAsync(Context(WorkspaceA, UserA), firstPage.NextCursor, CancellationToken.None);
        secondPage.Items.Should().ContainSingle().Which.Id.Should().Be(submissions.Single(item =>
            item.Id == submissions.Select(submission => submission.Id).Min()).Id);
        secondPage.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task Retry_keys_are_isolated_by_owner_and_workspace_and_expire_at_the_exact_boundary()
    {
        var now = InitialUtc;
        await using var db = CreateDbContext();
        var service = CreateService(db, () => now);
        var request = new FeedbackSubmissionRequest("Bug", "Subject", "Message");
        var original = await service.CreateAsync(Context(WorkspaceA, UserA), request, "shared-key", CancellationToken.None);
        original.Status.Should().Be(FeedbackCreateStatus.Created);

        var changedPayload = await service.CreateAsync(
            Context(WorkspaceA, UserA),
            request with { Subject = "Changed" },
            "shared-key",
            CancellationToken.None);
        changedPayload.Status.Should().Be(FeedbackCreateStatus.KeyReused);

        var otherContext = await service.CreateAsync(Context(WorkspaceB, UserB), request, "shared-key", CancellationToken.None);
        otherContext.Status.Should().Be(FeedbackCreateStatus.Created);
        otherContext.Receipt!.Id.Should().NotBe(original.Receipt!.Id);
        (await db.FeedbackSubmissions.CountAsync()).Should().Be(2);

        var storedHash = await db.FeedbackSubmissions
            .Where(submission => submission.Id == original.Receipt.Id)
            .Select(submission => submission.RetryKeyHash)
            .SingleAsync();
        storedHash.Should().NotBe("shared-key").And.HaveLength(64);

        now = original.Receipt.ExpiresAt;
        var expiredRetry = await service.CreateAsync(Context(WorkspaceA, UserA), request, "shared-key", CancellationToken.None);
        expiredRetry.Status.Should().Be(FeedbackCreateStatus.KeyExpired);

        var cleanup = new FeedbackRetentionService(db);
        while (await cleanup.DeleteExpiredBatchAsync(now, CancellationToken.None) == 500)
        {
        }
        (await db.FeedbackSubmissions.AnyAsync(submission => submission.Id == original.Receipt.Id)).Should().BeFalse();

        var reusedAfterCleanup = await service.CreateAsync(Context(WorkspaceA, UserA), request, "shared-key", CancellationToken.None);
        reusedAfterCleanup.Status.Should().Be(FeedbackCreateStatus.Created);
        reusedAfterCleanup.Receipt!.Id.Should().NotBe(original.Receipt.Id);
    }

    private WorkplaceDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<WorkplaceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options);

    private static FeedbackService CreateService(WorkplaceDbContext db, Func<DateTimeOffset> utcNow) =>
        new(db, new NoOpAuditWriter(), utcNow);

    private static WorkspaceContext Context(Guid workspaceId, Guid userId) =>
        new(new AuthenticatedUser(Guid.NewGuid(), userId, "test@example.invalid", "Test", "Member"),
            new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(workspaceId, "Test workspace"));

    private static Workspace CreateWorkspace(Guid id, Guid tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        DisplayName = "Test workspace",
        ConnectionStatus = "active",
        CreatedAt = InitialUtc,
        UpdatedAt = InitialUtc
    };

    private static FeedbackSubmission CreateSubmission(
        Guid id,
        Guid workspaceId,
        Guid submitterId,
        DateTimeOffset createdAt,
        DateTimeOffset expiresAt,
        string retryKeyHash) => new()
        {
            Id = id,
            WorkspaceId = workspaceId,
            SubmitterObjectId = submitterId,
            Category = "Bug",
            Subject = "Subject",
            Message = "Message",
            CreatedAt = createdAt,
            ExpiresAt = expiresAt,
            RetryKeyHash = retryKeyHash,
            PayloadFingerprint = new string('a', 64)
        };
}
