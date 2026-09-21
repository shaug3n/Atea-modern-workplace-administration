using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;

namespace Atea.UnifiedWorkplace.Api.Features.Audit;

public sealed record AuditContinuationCursor(
    Guid WorkspaceId,
    Guid RequesterObjectId,
    Guid? ActorObjectId,
    string? Action,
    string? Outcome,
    int PageSize,
    DateTimeOffset Timestamp,
    Guid Id);

public sealed class AuditContinuationTokenProtector(IDataProtectionProvider provider)
{
    private readonly IDataProtector protector = provider.CreateProtector("atea.unified-workplace.audit-continuation.v1");

    public string Protect(AuditContinuationCursor cursor)
    {
        var json = JsonSerializer.Serialize(cursor, JsonOptions);
        return protector.Protect(json);
    }

    public bool TryUnprotect(string token, out AuditContinuationCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 4096)
        {
            return false;
        }

        try
        {
            var json = protector.Unprotect(token);
            cursor = JsonSerializer.Deserialize<AuditContinuationCursor>(json, JsonOptions);
            return cursor is not null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
