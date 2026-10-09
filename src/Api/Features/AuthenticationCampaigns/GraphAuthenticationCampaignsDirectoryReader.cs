using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public sealed class GraphAuthenticationCampaignsDirectoryReader(IDelegatedGraphClientFactory clientFactory)
    : IAuthenticationCampaignsDirectoryReader
{
    private const string DirectoryPath = "/v1.0/users?$select=id,department,officeLocation,companyName&$top=999";

    public async Task<AuthenticationCampaignsDirectoryReadResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        GraphClientLease lease;
        try
        {
            lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryProfileReadScopes, cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return Failed(new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(StringComparer.OrdinalIgnoreCase), GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return Failed(new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(StringComparer.OrdinalIgnoreCase), GraphTokenAcquisitionErrorMapper.Map(exception));
        }

        await using (lease)
        {
            var users = new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(StringComparer.OrdinalIgnoreCase);
            var correlations = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var hasReadPage = false;
            string? path = DirectoryPath;

            try
            {
                while (path is not null)
                {
                    if (!seen.Add(path))
                    {
                        return Failed(users, InvalidResponse(), partialData: hasReadPage, correlations);
                    }

                    var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
                    AddCorrelation(response, correlations);
                    if (!response.Result.IsSuccess)
                    {
                        return Failed(users, response.Result, partialData: hasReadPage, correlations);
                    }

                    using var document = JsonDocument.Parse(response.Content);
                    if (!TryGetValues(document.RootElement, out var values))
                    {
                        return Failed(users, InvalidResponse(), partialData: hasReadPage, correlations);
                    }

                    var page = values.EnumerateArray().Select(MapEntry).ToArray();
                    foreach (var entry in page)
                    {
                        if (!string.IsNullOrWhiteSpace(entry.Id))
                        {
                            users[entry.Id] = entry;
                        }
                    }
                    hasReadPage = true;

                    if (!TryGetContinuationPath(document.RootElement, out path))
                    {
                        return Failed(users, InvalidResponse(), partialData: hasReadPage, correlations);
                    }
                }

                return new AuthenticationCampaignsDirectoryReadResult(users, null, CorrelationIds: correlations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                return Failed(users, InvalidResponse(), partialData: hasReadPage, correlations);
            }
        }
    }

    private static AuthenticationCampaignsDirectoryEntry MapEntry(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("A directory entry was not an object.");
        }

        return new AuthenticationCampaignsDirectoryEntry(
            OptionalString(element, "id") ?? string.Empty,
            OptionalString(element, "department"),
            OptionalString(element, "officeLocation"),
            OptionalString(element, "companyName"));
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

        if (!link.StartsWith("/v1.0/users?", StringComparison.Ordinal)
            || link.Contains('\\', StringComparison.Ordinal)
            || link.StartsWith("//", StringComparison.Ordinal)
            || link.Contains('#', StringComparison.Ordinal))
        {
            return false;
        }

        path = link;
        return true;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static AuthenticationCampaignsDirectoryReadResult Failed(
        IReadOnlyDictionary<string, AuthenticationCampaignsDirectoryEntry> users,
        GraphOperationResult error,
        bool partialData = false,
        IReadOnlyList<string>? correlations = null) =>
        new(users, error, partialData, correlations);

    private static GraphOperationResult InvalidResponse() => new(false, "invalid_response");

    private static void AddCorrelation(GraphTransportResponse response, List<string> correlations)
    {
        if (!string.IsNullOrWhiteSpace(response.Result.CorrelationId))
        {
            correlations.Add(response.Result.CorrelationId);
        }
    }
}
