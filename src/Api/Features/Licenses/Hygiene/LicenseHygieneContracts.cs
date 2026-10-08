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

public sealed record LicenseHygieneSourceStatus(
    string Freshness,
    bool PartialData,
    DateTimeOffset? FetchedAt,
    LicenseHygieneError? Error);

public sealed record LicenseHygieneCoverage(
    int RecordsAssessed,
    bool Completed,
    string StopReason,
    int MissingEvidenceRecords);

public sealed record LicenseHygieneError(
    string Category,
    string Message,
    int? StatusCode = null,
    int? RetryAfterSeconds = null);

public sealed record LicenseHygieneAccess(
    string State,
    string? ReasonCode = null,
    CapabilityDecision? Authorization = null);

public sealed record LicenseHygieneSkuRow(
    string SkuId,
    string PartNumber,
    string DisplayName,
    int Purchased,
    int Assigned,
    int Available);

public sealed record LicenseHygieneAssignedSku(
    string SkuId,
    string? PartNumber,
    string? DisplayName);

public sealed record LicenseHygieneDisabledAccountRow(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    IReadOnlyList<LicenseHygieneAssignedSku> AssignedLicenses,
    DateTimeOffset EvidenceAt,
    string EvidenceSource);

public sealed record LicenseHygieneResponse(
    LicenseHygieneAccess Access,
    LicenseHygieneSourceStatus Inventory,
    LicenseHygieneSourceStatus UserEvidence,
    LicenseHygieneCoverage Coverage,
    IReadOnlyList<LicenseHygieneSkuRow> CapacityItems,
    IReadOnlyList<LicenseHygieneDisabledAccountRow> DisabledAccounts);

public interface ILicenseHygieneUserReader
{
    Task<LicenseHygieneUserScanResult> ScanAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}
