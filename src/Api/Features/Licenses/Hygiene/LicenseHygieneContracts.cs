using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;

public sealed record LicenseHygieneScanLimits(
    int MaxRecords,
    int MaxPages,
    int PageSize,
    TimeSpan TimeBudget)
{
    public static LicenseHygieneScanLimits ProductionDefault { get; } =
        new(10_000, 100, 100, TimeSpan.FromSeconds(30));
}

public sealed record LicenseHygieneUserObservation(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    bool? AccountEnabled,
    IReadOnlyList<string>? AssignedSkuIds,
    bool AssignmentEvidenceMalformed = false);

public sealed record LicenseHygieneUserScanResult(
    IReadOnlyList<LicenseHygieneUserObservation> Users,
    int PagesRead,
    bool Completed,
    string StopReason,
    GraphOperationResult? Error,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt);

public interface ILicenseHygieneUserReader
{
    Task<LicenseHygieneUserScanResult> ScanAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}
