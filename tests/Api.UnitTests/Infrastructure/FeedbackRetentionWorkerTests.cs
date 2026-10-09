using System.Data.Common;
using Atea.UnifiedWorkplace.Api.Features.Feedback;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure;

public sealed class FeedbackRetentionWorkerTests
{
    [Fact]
    public async Task Startup_cleanup_drains_full_batches_until_a_short_batch()
    {
        var drained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaner = new FakeRetentionService((call, _, _) =>
        {
            if (call == 3)
            {
                drained.SetResult();
            }
            return Task.FromResult(call < 3 ? 500 : 23);
        });
        var scopes = new TestScopeFactory(cleaner);
        using var worker = CreateWorker(scopes, new CapturingLogger<FeedbackRetentionWorker>());

        await worker.StartAsync(CancellationToken.None);
        await drained.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopWorkerAsync(worker);

        cleaner.Calls.Should().Be(3);
        scopes.Created.Should().Be(1);
        scopes.Disposed.Should().Be(1);
    }

    [Fact]
    public async Task Startup_cleanup_uses_and_disposes_a_scope_when_host_cancels()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var cleaner = new FakeRetentionService(async (_, _, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return 0;
        });
        var scopes = new TestScopeFactory(cleaner);
        var logger = new CapturingLogger<FeedbackRetentionWorker>();
        using var worker = CreateWorker(scopes, logger);

        await worker.StartAsync(CancellationToken.None);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopWorkerAsync(worker);

        cleaner.CancellationToken.IsCancellationRequested.Should().BeTrue();
        scopes.Created.Should().Be(1);
        scopes.Disposed.Should().Be(1);
        logger.Entries.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(ExpectedOperationalFailures))]
    public async Task Expected_operational_failure_logs_only_a_static_message(Exception failure)
    {
        var cleaner = new FakeRetentionService((_, _, _) => Task.FromException<int>(failure));
        var logger = new CapturingLogger<FeedbackRetentionWorker>();
        using var worker = CreateWorker(new TestScopeFactory(cleaner), logger);

        await worker.StartAsync(CancellationToken.None);
        await logger.Logged.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await StopWorkerAsync(worker);

        cleaner.Calls.Should().Be(1);
        logger.Entries.Should().ContainSingle()
            .Which.Should().Be((LogLevel.Error, "Feedback retention cleanup failed.", null));
    }

    [Fact]
    public async Task Unexpected_programmer_fault_faults_the_worker()
    {
        var cleaner = new FakeRetentionService((_, _, _) =>
            Task.FromException<int>(new InvalidOperationException("programmer fault")));
        using var worker = CreateWorker(
            new TestScopeFactory(cleaner),
            new CapturingLogger<FeedbackRetentionWorker>());

        await worker.StartAsync(CancellationToken.None);

        await FluentActions.Awaiting(async () =>
                await worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("programmer fault");
    }

    private static async Task StopWorkerAsync(FeedbackRetentionWorker worker)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await worker.StopAsync(timeout.Token);
    }

    public static TheoryData<Exception> ExpectedOperationalFailures =>
    [
        new TestDatabaseException("database detail must not be logged"),
        new TimeoutException("timeout detail must not be logged")
    ];

    private static FeedbackRetentionWorker CreateWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<FeedbackRetentionWorker> logger) => new(scopeFactory, logger);

    private sealed class FakeRetentionService(
        Func<int, DateTimeOffset, CancellationToken, Task<int>> deleteBatch) : IFeedbackRetentionService
    {
        public int Calls { get; private set; }
        public CancellationToken CancellationToken { get; private set; }

        public Task<int> DeleteExpiredBatchAsync(DateTimeOffset nowUtc, CancellationToken cancellationToken)
        {
            Calls++;
            CancellationToken = cancellationToken;
            return deleteBatch(Calls, nowUtc, cancellationToken);
        }
    }

    private sealed class TestScopeFactory(IFeedbackRetentionService service) : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            return new TestScope(service, () => Disposed++);
        }
    }

    private sealed class TestScope(
        IFeedbackRetentionService service,
        Action onDispose) : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; } = new TestServiceProvider(service);

        public void Dispose() => onDispose();

        public ValueTask DisposeAsync()
        {
            onDispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class TestServiceProvider(IFeedbackRetentionService service) : IServiceProvider
    {
        public object? GetService(Type serviceType) =>
            serviceType == typeof(IFeedbackRetentionService) ? service : null;
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];
        public TaskCompletionSource Logged { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
            Logged.TrySetResult();
        }
    }

    private sealed class TestDatabaseException(string message) : DbException(message);
}
