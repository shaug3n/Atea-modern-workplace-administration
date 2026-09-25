using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Exchange;

public sealed record ExchangeMailboxListQuery(
    string? Search = null,
    int PageSize = 25,
    string? ContinuationToken = null);

public sealed record ExchangeMailboxDirectoryEntry(
    string UserId,
    string? DisplayName,
    string? Address,
    string VerificationStatus = "unverified");

public sealed record ExchangeMailboxListResponse(
    IReadOnlyList<ExchangeMailboxDirectoryEntry> Items,
    string? ContinuationToken,
    string InventorySource,
    bool IsCompleteExchangeInventory,
    string Limitation);

public sealed record ExchangeMailboxListResult(
    ExchangeMailboxListResponse Response,
    GraphOperationResult? Error);

public sealed record ExchangeMailboxOverviewResponse(
    string UserId,
    string VerificationStatus,
    string? MailboxType,
    ExchangeMailboxSettingsSummary? Settings,
    ExchangeVerificationError? Error,
    int HttpStatusCode);

public sealed record ExchangeMailboxSettingsSummary(
    string? TimeZone,
    string? Language,
    string? DateFormat,
    string? TimeFormat,
    string? AutomaticRepliesStatus);

public sealed record ExchangeVerificationError(string Category, string Message);
