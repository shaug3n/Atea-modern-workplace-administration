using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Security;

public sealed record IdempotencyScope(
    Guid WorkspaceId,
    Guid ActorObjectId,
    string Operation,
    string TargetId,
    string Key);

public sealed record IdempotentOperationResult(
    int StatusCode,
    string ResultCategory,
    string SafeResultJson,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public enum IdempotencyOutcomeKind
{
    Created,
    Replayed,
    KeyReused
}

public sealed record IdempotencyOutcome(IdempotencyOutcomeKind Kind, IdempotentOperationResult Result);

public interface IIdempotencyService
{
    Task<IdempotencyOutcome> ExecuteAsync(
        IdempotencyScope scope,
        object requestPayload,
        Func<Task<IdempotentOperationResult>> execute,
        CancellationToken cancellationToken);
}

public sealed class IdempotencyService(WorkplaceDbContext dbContext, Func<DateTimeOffset>? utcNow = null) : IIdempotencyService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<IdempotencyOutcome> ExecuteAsync(
        IdempotencyScope scope,
        object requestPayload,
        Func<Task<IdempotentOperationResult>> execute,
        CancellationToken cancellationToken)
    {
        var fingerprint = Fingerprint(requestPayload);
        var existing = await dbContext.IdempotencyRecords.SingleOrDefaultAsync(record =>
            record.WorkspaceId == scope.WorkspaceId
            && record.ActorObjectId == scope.ActorObjectId
            && record.Operation == scope.Operation
            && record.TargetId == scope.TargetId
            && record.Key == scope.Key,
            cancellationToken);

        if (existing is not null)
        {
            return existing.RequestFingerprint == fingerprint
                ? new IdempotencyOutcome(IdempotencyOutcomeKind.Replayed, ToResult(existing))
                : Reused();
        }

        var result = await execute();
        dbContext.IdempotencyRecords.Add(new IdempotencyRecord
        {
            WorkspaceId = scope.WorkspaceId,
            ActorObjectId = scope.ActorObjectId,
            Operation = scope.Operation,
            TargetId = scope.TargetId,
            Key = scope.Key,
            RequestFingerprint = fingerprint,
            StatusCode = result.StatusCode,
            ResultCategory = result.ResultCategory,
            SafeResultJson = result.SafeResultJson,
            GraphCorrelationId = result.GraphCorrelationId,
            GraphRequestId = result.GraphRequestId,
            CreatedAt = utcNow()
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        return new IdempotencyOutcome(IdempotencyOutcomeKind.Created, result);
    }

    internal static string Fingerprint(object payload)
    {
        var json = JsonSerializer.Serialize(payload, JsonOptions);
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static IdempotencyOutcome Reused() =>
        new(
            IdempotencyOutcomeKind.KeyReused,
            new IdempotentOperationResult(
                StatusCodes.Status409Conflict,
                "idempotency_key_reused",
                """{"status":"idempotency_key_reused","error":"idempotency_key_reused"}"""));

    private static IdempotentOperationResult ToResult(IdempotencyRecord record) =>
        new(record.StatusCode, record.ResultCategory, record.SafeResultJson, record.GraphCorrelationId, record.GraphRequestId);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };
}

public sealed class MemoryIdempotencyService : IIdempotencyService
{
    private readonly object gate = new();
    private readonly List<IdempotencyRecord> records = [];

    public IReadOnlyList<IdempotencyRecord> Records
    {
        get
        {
            lock (gate)
            {
                return records.ToArray();
            }
        }
    }

    public Task<IdempotencyOutcome> ExecuteAsync(
        IdempotencyScope scope,
        object requestPayload,
        Func<Task<IdempotentOperationResult>> execute,
        CancellationToken cancellationToken)
    {
        return ExecuteCoreAsync(scope, requestPayload, execute);
    }

    private async Task<IdempotencyOutcome> ExecuteCoreAsync(
        IdempotencyScope scope,
        object requestPayload,
        Func<Task<IdempotentOperationResult>> execute)
    {
        var fingerprint = IdempotencyService.Fingerprint(requestPayload);
        lock (gate)
        {
            var existing = records.SingleOrDefault(record =>
                record.WorkspaceId == scope.WorkspaceId
                && record.ActorObjectId == scope.ActorObjectId
                && record.Operation == scope.Operation
                && record.TargetId == scope.TargetId
                && record.Key == scope.Key);
            if (existing is not null)
            {
                return existing.RequestFingerprint == fingerprint
                    ? new IdempotencyOutcome(
                        IdempotencyOutcomeKind.Replayed,
                        new IdempotentOperationResult(existing.StatusCode, existing.ResultCategory, existing.SafeResultJson, existing.GraphCorrelationId, existing.GraphRequestId))
                    : new IdempotencyOutcome(
                        IdempotencyOutcomeKind.KeyReused,
                        new IdempotentOperationResult(StatusCodes.Status409Conflict, "idempotency_key_reused", """{"status":"idempotency_key_reused","error":"idempotency_key_reused"}"""));
            }
        }

        var result = await execute();
        lock (gate)
        {
            records.Add(new IdempotencyRecord
            {
                WorkspaceId = scope.WorkspaceId,
                ActorObjectId = scope.ActorObjectId,
                Operation = scope.Operation,
                TargetId = scope.TargetId,
                Key = scope.Key,
                RequestFingerprint = fingerprint,
                StatusCode = result.StatusCode,
                ResultCategory = result.ResultCategory,
                SafeResultJson = result.SafeResultJson,
                GraphCorrelationId = result.GraphCorrelationId,
                GraphRequestId = result.GraphRequestId,
                CreatedAt = DateTimeOffset.UtcNow
            });
        }

        return new IdempotencyOutcome(IdempotencyOutcomeKind.Created, result);
    }
}
