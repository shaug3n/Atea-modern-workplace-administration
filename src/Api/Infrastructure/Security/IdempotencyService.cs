using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;

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
    KeyReused,
    InProgress
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
        var reservation = NewReservation(scope, fingerprint, utcNow());
        dbContext.IdempotencyRecords.Add(reservation);
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsUniqueConstraintViolation(exception))
        {
            dbContext.ChangeTracker.Clear();
            var existing = await FindRecordAsync(scope, cancellationToken);
            return ExistingOutcome(existing, fingerprint);
        }

        var result = await execute();
        reservation.StatusCode = result.StatusCode;
        reservation.ResultCategory = result.ResultCategory;
        reservation.SafeResultJson = result.SafeResultJson;
        reservation.GraphCorrelationId = result.GraphCorrelationId;
        reservation.GraphRequestId = result.GraphRequestId;
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

    private Task<IdempotencyRecord?> FindRecordAsync(IdempotencyScope scope, CancellationToken cancellationToken) =>
        dbContext.IdempotencyRecords.AsNoTracking().SingleOrDefaultAsync(record =>
            record.WorkspaceId == scope.WorkspaceId
            && record.ActorObjectId == scope.ActorObjectId
            && record.Operation == scope.Operation
            && record.TargetId == scope.TargetId
            && record.Key == scope.Key,
            cancellationToken);

    private static IdempotencyOutcome ExistingOutcome(IdempotencyRecord? existing, string fingerprint)
    {
        if (existing is null || string.Equals(existing.ResultCategory, InProgressCategory, StringComparison.Ordinal))
        {
            return InProgress();
        }

        return existing.RequestFingerprint == fingerprint
            ? new IdempotencyOutcome(IdempotencyOutcomeKind.Replayed, ToResult(existing))
            : Reused();
    }

    private static IdempotencyRecord NewReservation(IdempotencyScope scope, string fingerprint, DateTimeOffset createdAt) => new()
    {
        WorkspaceId = scope.WorkspaceId,
        ActorObjectId = scope.ActorObjectId,
        Operation = scope.Operation,
        TargetId = scope.TargetId,
        Key = scope.Key,
        RequestFingerprint = fingerprint,
        StatusCode = StatusCodes.Status409Conflict,
        ResultCategory = InProgressCategory,
        SafeResultJson = InProgressJson,
        CreatedAt = createdAt
    };

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    private static IdempotencyOutcome InProgress() =>
        new(IdempotencyOutcomeKind.InProgress, new IdempotentOperationResult(StatusCodes.Status409Conflict, InProgressCategory, InProgressJson));

    private const string InProgressCategory = "idempotency_in_progress";
    private const string InProgressJson = """{"status":"temporarily_unavailable","error":"idempotency_in_progress"}""";
}

public sealed class MemoryIdempotencyService : IIdempotencyService
{
    private readonly object gate = new();
    private readonly List<IdempotencyRecord> records = [];
    private readonly Dictionary<string, InFlightOperation> inFlight = [];

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
        var scopeKey = ScopeKey(scope);
        Task<IdempotencyOutcome>? pending = null;
        var owner = false;
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
                if (existing.RequestFingerprint != fingerprint)
                {
                    return new IdempotencyOutcome(
                        IdempotencyOutcomeKind.KeyReused,
                        new IdempotentOperationResult(StatusCodes.Status409Conflict, "idempotency_key_reused", """{"status":"idempotency_key_reused","error":"idempotency_key_reused"}"""));
                }

                if (string.Equals(existing.ResultCategory, "idempotency_in_progress", StringComparison.Ordinal))
                {
                    if (inFlight.TryGetValue(scopeKey, out var inFlightOperation) && inFlightOperation.RequestFingerprint == fingerprint)
                    {
                        pending = inFlightOperation.Outcome;
                    }
                    else
                    {
                        return new IdempotencyOutcome(
                            IdempotencyOutcomeKind.InProgress,
                            new IdempotentOperationResult(StatusCodes.Status409Conflict, "idempotency_in_progress", """{"status":"temporarily_unavailable","error":"idempotency_in_progress"}"""));
                    }
                }
                else
                {
                    return new IdempotencyOutcome(
                        IdempotencyOutcomeKind.Replayed,
                        new IdempotentOperationResult(existing.StatusCode, existing.ResultCategory, existing.SafeResultJson, existing.GraphCorrelationId, existing.GraphRequestId));
                }
            }
            else
            {
                var reservation = new IdempotencyRecord
                {
                    WorkspaceId = scope.WorkspaceId,
                    ActorObjectId = scope.ActorObjectId,
                    Operation = scope.Operation,
                    TargetId = scope.TargetId,
                    Key = scope.Key,
                    RequestFingerprint = fingerprint,
                    StatusCode = StatusCodes.Status409Conflict,
                    ResultCategory = "idempotency_in_progress",
                    SafeResultJson = """{"status":"temporarily_unavailable","error":"idempotency_in_progress"}""",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                records.Add(reservation);
                pending = ExecuteAndStoreAsync(scopeKey, reservation, execute);
                inFlight[scopeKey] = new InFlightOperation(fingerprint, pending);
                owner = true;
            }
        }

        try
        {
            var outcome = await pending!;
            return owner
                ? outcome
                : new IdempotencyOutcome(IdempotencyOutcomeKind.Replayed, outcome.Result);
        }
        catch when (!owner)
        {
            return new IdempotencyOutcome(
                IdempotencyOutcomeKind.InProgress,
                new IdempotentOperationResult(StatusCodes.Status409Conflict, "idempotency_in_progress", """{"status":"temporarily_unavailable","error":"idempotency_in_progress"}"""));
        }
    }

    private async Task<IdempotencyOutcome> ExecuteAndStoreAsync(
        string scopeKey,
        IdempotencyRecord reservation,
        Func<Task<IdempotentOperationResult>> execute)
    {
        try
        {
            var result = await execute();
            lock (gate)
            {
                reservation.StatusCode = result.StatusCode;
                reservation.ResultCategory = result.ResultCategory;
                reservation.SafeResultJson = result.SafeResultJson;
                reservation.GraphCorrelationId = result.GraphCorrelationId;
                reservation.GraphRequestId = result.GraphRequestId;
            }

            return new IdempotencyOutcome(IdempotencyOutcomeKind.Created, result);
        }
        finally
        {
            lock (gate)
            {
                inFlight.Remove(scopeKey);
            }
        }
    }

    private static string ScopeKey(IdempotencyScope scope) =>
        string.Join('\u001f', scope.WorkspaceId, scope.ActorObjectId, scope.Operation, scope.TargetId, scope.Key);

    private sealed record InFlightOperation(string RequestFingerprint, Task<IdempotencyOutcome> Outcome);
}
