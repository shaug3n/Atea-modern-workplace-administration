using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Features.Exchange;

public interface IExchangeService
{
    Task<ExchangeMailboxListResult> ListAsync(
        WorkspaceContext context,
        ExchangeMailboxListQuery query,
        CancellationToken cancellationToken);

    Task<ExchangeMailboxOverviewResponse> VerifyAsync(
        WorkspaceContext context,
        string userObjectId,
        CancellationToken cancellationToken);
}

public sealed class ExchangeService(IDelegatedGraphClientFactory clientFactory) : IExchangeService
{
    private const string DirectorySelect = "id,displayName,mail,userPrincipalName";
    private const string DirectoryInventoryLimitation = "This is a directory-backed list of mail-enabled identities, not a complete Exchange inventory.";

    public async Task<ExchangeMailboxListResult> ListAsync(
        WorkspaceContext context,
        ExchangeMailboxListQuery query,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(query.ContinuationToken) && !IsSafeContinuationPath(query.ContinuationToken))
        {
            return FailedList(new GraphOperationResult(false, "invalid_continuation", StatusCodes.Status400BadRequest));
        }

        GraphTransportResponse response;
        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
            response = await lease.Transport.SendAsync(
                new GraphRequest(HttpMethod.Get, BuildDirectoryPath(query), Headers: HeadersFor(query)),
                cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return FailedList(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return FailedList(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (GraphAdapterException exception)
        {
            return FailedList(exception.Result);
        }

        if (!response.Result.IsSuccess)
        {
            return FailedList(response.Result);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var items = Values(document)
                .Select(MapDirectoryEntry)
                .Where(item => !string.IsNullOrWhiteSpace(item.UserId) && !string.IsNullOrWhiteSpace(item.Address))
                .ToArray();
            var continuationToken = document.RootElement.TryGetProperty("@odata.nextLink", out var nextLink)
                ? NormalizeGraphPath(nextLink.GetString())
                : null;

            return new ExchangeMailboxListResult(
                new ExchangeMailboxListResponse(items, continuationToken, "directory", false, DirectoryInventoryLimitation),
                null);
        }
        catch (JsonException)
        {
            return FailedList(new GraphOperationResult(false, "invalid_response"));
        }
    }

    public async Task<ExchangeMailboxOverviewResponse> VerifyAsync(
        WorkspaceContext context,
        string userObjectId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userObjectId);

        try
        {
            await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.ExchangeMailboxSettingsReadScopes, cancellationToken);
            var response = await lease.Transport.SendAsync(
                new GraphRequest(HttpMethod.Get, $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/mailboxSettings"),
                cancellationToken);

            if (!response.Result.IsSuccess)
            {
                return Failure(userObjectId, response.Result);
            }

            try
            {
                using var document = JsonDocument.Parse(response.Content);
                return new ExchangeMailboxOverviewResponse(
                    userObjectId,
                    "verified",
                    OptionalString(document.RootElement, "userPurpose") ?? "unknown",
                    MapSettings(document.RootElement),
                    null,
                    StatusCodes.Status200OK);
            }
            catch (JsonException)
            {
                return Failure(userObjectId, new GraphOperationResult(false, "invalid_response"));
            }
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return Failure(userObjectId, GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return Failure(userObjectId, GraphTokenAcquisitionErrorMapper.Map(exception));
        }
    }

    private static ExchangeMailboxListResult FailedList(GraphOperationResult error) =>
        new(
            new ExchangeMailboxListResponse([], null, "directory", false, DirectoryInventoryLimitation),
            error);

    private static string BuildDirectoryPath(ExchangeMailboxListQuery query)
    {
        if (!string.IsNullOrWhiteSpace(query.ContinuationToken))
        {
            return query.ContinuationToken!;
        }

        var pageSize = query.PageSize is <= 0 or > 100 ? 25 : query.PageSize;
        var parameters = new List<string>
        {
            $"$select={DirectorySelect}",
            $"$top={pageSize}",
            $"$filter={Uri.EscapeDataString("mail ne null")}"
        };

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            parameters.Add($"$search={Uri.EscapeDataString($"\"displayName:{search}\" OR \"mail:{search}\" OR \"userPrincipalName:{search}\"")}");
        }

        return $"/v1.0/users?{string.Join("&", parameters)}";
    }

    private static IReadOnlyDictionary<string, string>? HeadersFor(ExchangeMailboxListQuery query) =>
        string.IsNullOrWhiteSpace(query.Search)
            ? null
            : new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

    private static bool IsSafeContinuationPath(string? continuationToken) =>
        continuationToken is "/v1.0/users"
        || continuationToken?.StartsWith("/v1.0/users?", StringComparison.OrdinalIgnoreCase) == true;

    private static ExchangeMailboxDirectoryEntry MapDirectoryEntry(JsonElement element) =>
        new(
            OptionalString(element, "id") ?? string.Empty,
            OptionalString(element, "displayName"),
            OptionalString(element, "mail"));

    private static ExchangeMailboxSettingsSummary MapSettings(JsonElement element) =>
        new(
            OptionalString(element, "timeZone"),
            OptionalString(element, "language", "locale"),
            OptionalString(element, "dateFormat"),
            OptionalString(element, "timeFormat"),
            OptionalString(element, "automaticRepliesSetting", "status"));

    private static ExchangeMailboxOverviewResponse Failure(string userObjectId, GraphOperationResult error)
    {
        var (status, httpStatusCode) = error.Category switch
        {
            "not_found" => ("mailbox_missing", StatusCodes.Status404NotFound),
            "not_authorized" => ("forbidden", StatusCodes.Status403Forbidden),
            "consent_required" => ("consent_required", StatusCodes.Status403Forbidden),
            _ => ("unavailable", StatusCodes.Status503ServiceUnavailable)
        };

        return new ExchangeMailboxOverviewResponse(
            userObjectId,
            status,
            null,
            null,
            new ExchangeVerificationError(status, MessageFor(status)),
            httpStatusCode);
    }

    private static string MessageFor(string status) => status switch
    {
        "mailbox_missing" => "No Exchange mailbox was found for this directory identity.",
        "forbidden" => "The connected account is not authorized to verify this mailbox.",
        "consent_required" => "Delegated MailboxSettings.Read consent is required to verify this mailbox.",
        _ => "Exchange mailbox verification is temporarily unavailable."
    };

    private static IEnumerable<JsonElement> Values(JsonDocument document) =>
        document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
            : [];

    private static string? NormalizeGraphPath(string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink))
        {
            return null;
        }

        return Uri.TryCreate(nextLink, UriKind.Absolute, out var uri)
            ? uri.PathAndQuery
            : nextLink;
    }

    private static string? OptionalString(JsonElement element, string property, string? nestedProperty = null)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (nestedProperty is not null)
        {
            return value.ValueKind == JsonValueKind.Object && value.TryGetProperty(nestedProperty, out var nested)
                ? nested.GetString()
                : null;
        }

        return value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
