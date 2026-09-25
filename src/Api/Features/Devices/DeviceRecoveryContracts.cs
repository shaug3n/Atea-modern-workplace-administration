using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class DeviceTarget
{
    public static bool IsSafe(string? id) => !string.IsNullOrWhiteSpace(id)
        && id.Length <= 200 && !id.Any(character => char.IsControl(character) || character is '/' or '\\' or '?' or '#' or '%');
}

public sealed record BitlockerRecoveryMetadata(string Id, string? DeviceId, DateTimeOffset? CreatedDateTime, string? VolumeType);
public sealed record BitlockerSecret(string Key);
public sealed record LapsMetadata(string Id, string? DeviceName, DateTimeOffset? LastBackupDateTime, DateTimeOffset? RefreshDateTime);
public sealed record LapsSecret(string? AccountName, string Password, DateTimeOffset? BackupDateTime);

public sealed record RecoveryResult<T>(
    string Status,
    T? Data = default,
    string? Error = null,
    string? Guidance = null,
    int? RetryAfterSeconds = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null);

public interface IGraphDeviceRecoveryReader
{
    Task<GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>> ListBitlockerAsync(string entraDeviceId, bool secretScope, CancellationToken cancellationToken);
    Task<GraphReadResult<BitlockerSecret>> GetBitlockerAsync(string keyId, CancellationToken cancellationToken);
    Task<GraphReadResult<LapsMetadata>> GetLapsMetadataAsync(string entraDeviceId, bool secretScope, CancellationToken cancellationToken);
    Task<GraphReadResult<LapsSecret>> GetLapsSecretAsync(string entraDeviceId, CancellationToken cancellationToken);
}
