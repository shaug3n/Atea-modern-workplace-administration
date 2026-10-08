using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public sealed class GraphAuthenticationCampaignsReportReader(IDelegatedGraphClientFactory clientFactory)
    : IAuthenticationCampaignsRegistrationReportReader
{
    private const string ReportPath = "/v1.0/reports/authenticationMethods/userRegistrationDetails";

    public async Task<AuthenticationCampaignsReportReadResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        GraphClientLease lease;
        try
        {
            lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthenticationCampaignReportScopes, cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return Failed([], GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return Failed([], GraphTokenAcquisitionErrorMapper.Map(exception));
        }

        await using (lease)
        {
            var records = new List<AuthenticationCampaignsReportRecord>();
            var correlations = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var hasReadPage = false;
            string? path = ReportPath;

            try
            {
                while (path is not null)
                {
                    if (!seen.Add(path))
                    {
                        return Failed(records, InvalidResponse(), partialData: hasReadPage, correlations);
                    }

                    var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
                    AddCorrelation(response, correlations);
                    if (!response.Result.IsSuccess)
                    {
                        return Failed(records, response.Result, partialData: hasReadPage, correlations);
                    }

                    using var document = JsonDocument.Parse(response.Content);
                    if (!TryGetValues(document.RootElement, out var values))
                    {
                        return Failed(records, InvalidResponse(), partialData: hasReadPage, correlations);
                    }

                    var page = values.EnumerateArray().Select(MapRecord).ToArray();
                    records.AddRange(page);
                    hasReadPage = true;

                    if (!TryGetContinuationPath(document.RootElement, out path))
                    {
                        return Failed(records, InvalidResponse(), partialData: hasReadPage, correlations);
                    }
                }

                return new AuthenticationCampaignsReportReadResult(records, null, CorrelationIds: correlations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
            {
                return Failed(records, InvalidResponse(), partialData: hasReadPage, correlations);
            }
        }
    }

    private static AuthenticationCampaignsReportRecord MapRecord(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("A report entry was not an object.");
        }

        return new AuthenticationCampaignsReportRecord(
            OptionalString(element, "id") ?? string.Empty,
            OptionalString(element, "userDisplayName"),
            OptionalString(element, "userPrincipalName"),
            OptionalString(element, "userType"),
            OptionalStringCollection(element, "methodsRegistered"),
            OptionalBoolean(element, "isMfaRegistered"),
            OptionalBoolean(element, "isMfaCapable"),
            OptionalBoolean(element, "isPasswordlessCapable"),
            OptionalBoolean(element, "isSystemPreferredAuthenticationMethodEnabled"),
            OptionalStringCollection(element, "systemPreferredAuthenticationMethods"),
            OptionalString(element, "userPreferredMethodForSecondaryAuthentication"),
            OptionalDateTimeOffset(element, "lastUpdatedDateTime"));
    }

    private static bool TryGetValues(JsonElement root, out JsonElement values)
    {
        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("value", out values)
            && values.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        values = default;
        return false;
    }

    private static bool TryGetContinuationPath(JsonElement root, out string? path)
    {
        path = null;
        if (!root.TryGetProperty("@odata.nextLink", out var next) || next.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (next.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var link = next.GetString();
        if (string.IsNullOrWhiteSpace(link))
        {
            return false;
        }

        if (link.Contains("://", StringComparison.Ordinal)
            && Uri.TryCreate(link, UriKind.Absolute, out var absolute))
        {
            if (absolute.Scheme != Uri.UriSchemeHttps
                || !absolute.Host.Equals("graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
                || absolute.Port != 443
                || !string.IsNullOrEmpty(absolute.UserInfo)
                || !string.IsNullOrEmpty(absolute.Fragment))
            {
                return false;
            }

            link = absolute.PathAndQuery;
        }

        if (!link.StartsWith(ReportPath + "?", StringComparison.Ordinal)
            || link.Contains('\\', StringComparison.Ordinal)
            || link.StartsWith("//", StringComparison.Ordinal)
            || link.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        path = link;
        return true;
    }

    private static IReadOnlyList<string>? OptionalStringCollection(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException($"The {property} field was not an array.");
        }

        var values = new List<string>();
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                throw new InvalidOperationException($"The {property} array contained a non-string value.");
            }

            values.Add(item.GetString() ?? string.Empty);
        }

        return values;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? OptionalBoolean(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : null;

    private static AuthenticationCampaignsReportReadResult Failed(
        IReadOnlyList<AuthenticationCampaignsReportRecord> records,
        GraphOperationResult error,
        bool partialData = false,
        IReadOnlyList<string>? correlations = null) =>
        new(records, error, partialData, correlations);

    private static GraphOperationResult InvalidResponse() => new(false, "invalid_response");

    private static void AddCorrelation(GraphTransportResponse response, List<string> correlations)
    {
        if (!string.IsNullOrWhiteSpace(response.Result.CorrelationId))
        {
            correlations.Add(response.Result.CorrelationId);
        }
    }
}
