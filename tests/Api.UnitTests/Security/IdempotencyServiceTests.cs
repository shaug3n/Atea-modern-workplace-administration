using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Security;

public sealed class IdempotencyServiceTests
{
    [Fact]
    public async Task Exact_replay_returns_original_safe_result_without_running_mutation_again()
    {
        var service = new MemoryIdempotencyService();
        var scope = new IdempotencyScope(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "users.update",
            "user-1",
            "same-key");
        var calls = 0;

        var first = await service.ExecuteAsync(scope, new { displayName = "Ada" }, () =>
        {
            calls++;
            return Task.FromResult(new IdempotentOperationResult(200, "succeeded", """{"status":"succeeded"}"""));
        }, CancellationToken.None);
        var replay = await service.ExecuteAsync(scope, new { displayName = "Ada" }, () =>
        {
            calls++;
            return Task.FromResult(new IdempotentOperationResult(200, "succeeded", """{"status":"succeeded"}"""));
        }, CancellationToken.None);

        first.Kind.Should().Be(IdempotencyOutcomeKind.Created);
        replay.Kind.Should().Be(IdempotencyOutcomeKind.Replayed);
        replay.Result.SafeResultJson.Should().Be("""{"status":"succeeded"}""");
        calls.Should().Be(1);
    }

    [Fact]
    public async Task Changed_payload_with_same_scope_and_key_returns_reused_conflict()
    {
        var service = new MemoryIdempotencyService();
        var scope = new IdempotencyScope(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "users.update",
            "user-1",
            "same-key");

        await service.ExecuteAsync(scope, new { displayName = "Ada" }, () =>
            Task.FromResult(new IdempotentOperationResult(200, "succeeded", """{"status":"succeeded"}""")), CancellationToken.None);
        var reused = await service.ExecuteAsync(scope, new { displayName = "Grace" }, () =>
            Task.FromResult(new IdempotentOperationResult(200, "succeeded", """{"status":"succeeded"}""")), CancellationToken.None);

        reused.Kind.Should().Be(IdempotencyOutcomeKind.KeyReused);
        reused.Result.StatusCode.Should().Be(409);
        reused.Result.ResultCategory.Should().Be("idempotency_key_reused");
    }

    [Fact]
    public async Task Safe_metadata_does_not_store_request_body_password_token_or_raw_graph_response()
    {
        var service = new MemoryIdempotencyService();
        var scope = new IdempotencyScope(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "users.create",
            "new",
            "create-key");

        await service.ExecuteAsync(scope, new { displayName = "Ada", temporaryPassword = "Secret-123", access_token = "token" }, () =>
            Task.FromResult(new IdempotentOperationResult(201, "succeeded", """{"status":"succeeded","temporaryCredentialNotice":null}""", "corr-1", "req-1")), CancellationToken.None);

        var record = service.Records.Should().ContainSingle().Subject;
        record.RequestFingerprint.Should().NotContain("Ada");
        record.RequestFingerprint.Should().NotContain("Secret-123");
        record.RequestFingerprint.Should().NotContain("token");
        record.SafeResultJson.Should().NotContain("Secret-123");
        record.SafeResultJson.Should().NotContain("raw graph");
    }

    [Fact]
    public async Task Concurrent_same_scope_and_key_runs_mutation_once()
    {
        var service = new MemoryIdempotencyService();
        var scope = new IdempotencyScope(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "users.update",
            "user-1",
            "same-key");
        var releaseMutation = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;

        Task<IdempotencyOutcome> Execute() => service.ExecuteAsync(scope, new { displayName = "Ada" }, async () =>
        {
            Interlocked.Increment(ref calls);
            await releaseMutation.Task;
            return new IdempotentOperationResult(200, "succeeded", """{"status":"succeeded"}""");
        }, CancellationToken.None);

        var first = Execute();
        var second = Execute();
        await Task.Delay(50);
        releaseMutation.SetResult();
        var results = await Task.WhenAll(first, second);

        calls.Should().Be(1);
        results.Should().ContainSingle(result => result.Kind == IdempotencyOutcomeKind.Created);
        results.Should().ContainSingle(result => result.Kind == IdempotencyOutcomeKind.Replayed);
    }
}
