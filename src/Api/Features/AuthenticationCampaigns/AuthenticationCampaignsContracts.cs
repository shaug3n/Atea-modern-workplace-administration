using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public interface IAuthenticationCampaignsRegistrationReportReader
{
    Task<AuthenticationCampaignsReportReadResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}

public interface IAuthenticationCampaignsDirectoryReader
{
    Task<AuthenticationCampaignsDirectoryReadResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}

public interface IAuthenticationCampaignsService
{
    Task<AuthenticationCampaignsServiceResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken);
}

public sealed record AuthenticationCampaignsReportReadResult(
    IReadOnlyList<AuthenticationCampaignsReportRecord> Records,
    GraphOperationResult? Error,
    bool PartialData = false,
    IReadOnlyList<string>? CorrelationIds = null);

public sealed record AuthenticationCampaignsReportRecord(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    string? UserType,
    IReadOnlyList<string>? MethodsRegistered,
    bool? IsMfaRegistered,
    bool? IsMfaCapable,
    bool? IsPasswordlessCapable,
    bool? IsSystemPreferredAuthenticationMethodEnabled,
    IReadOnlyList<string>? SystemPreferredAuthenticationMethods,
    string? UserPreferredMethodForSecondaryAuthentication,
    DateTimeOffset? LastUpdatedDateTime);

public sealed record AuthenticationCampaignsDirectoryEntry(
    string Id,
    string? Department,
    string? OfficeLocation,
    string? CompanyName);

public sealed record AuthenticationCampaignsDirectoryReadResult(
    IReadOnlyDictionary<string, AuthenticationCampaignsDirectoryEntry> Users,
    GraphOperationResult? Error,
    bool PartialData = false,
    IReadOnlyList<string>? CorrelationIds = null);

public sealed record AuthenticationCampaignsServiceResult(
    AuthenticationCampaignsResponse? Response,
    GraphOperationResult? Error);

public sealed record AuthenticationCampaignsResponse(
    IReadOnlyList<AuthenticationCampaignsRegistration> Items,
    DateTimeOffset FetchedAt,
    DateTimeOffset? SourceLastUpdatedFrom,
    DateTimeOffset? SourceLastUpdatedTo,
    bool PartialData,
    int ObservedRecordCount,
    int DuplicateRecordCount,
    string DirectoryEnrichmentState,
    int EnrichedAccountCount,
    string? ReportErrorCategory,
    string? DirectoryErrorCategory);

public sealed record AuthenticationCampaignsRegistration(
    string Id,
    string? DisplayName,
    string? UserPrincipalName,
    string? UserType,
    IReadOnlyList<string>? MethodsRegistered,
    bool? IsMfaRegistered,
    bool? IsMfaCapable,
    bool? IsPasswordlessCapable,
    bool? IsSystemPreferredAuthenticationMethodEnabled,
    IReadOnlyList<string>? SystemPreferredAuthenticationMethods,
    string? UserPreferredMethodForSecondaryAuthentication,
    DateTimeOffset? LastUpdatedDateTime,
    string PasskeyRegistrationState,
    bool? IsGenericFido2Registered,
    string PhoneRegistrationState,
    string PhonePreferenceState,
    string? Department,
    string? OfficeLocation,
    string? CompanyName,
    string DirectoryJoinState);
