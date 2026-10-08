using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;

public sealed class GraphLicenseHygieneUserReader : ILicenseHygieneUserReader
{
    private const string UserSelect = "id,displayName,userPrincipalName,accountEnabled,assignedLicenses";
    private readonly IDelegatedGraphClientFactory clientFactory;
    private readonly LicenseHygieneScanLimits limits;

    public GraphLicenseHygieneUserReader(
        IDelegatedGraphClientFactory clientFactory,
        LicenseHygieneScanLimits? limits = null)
    {
        this.clientFactory = clientFactory;
        this.limits = limits ?? LicenseHygieneScanLimits.ProductionDefault;

        if (this.limits.MaxRecords is < 1 or > 10_000
            || this.limits.MaxPages is < 1 or > 100
            || this.limits.PageSize is < 1 or > 100
            || this.limits.TimeBudget <= TimeSpan.Zero
            || this.limits.TimeBudget > TimeSpan.FromSeconds(30))
        {
            throw new ArgumentOutOfRangeException(nameof(limits), "License hygiene scan limits must remain within the approved Graph scan budget.");
        }
    }

    public async Task<LicenseHygieneUserScanResult> ScanAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var users = new List<LicenseHygieneUserObservation>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var requestedPaths = new HashSet<string>(StringComparer.Ordinal);
        var pagesRead = 0;
        GraphClientLease? lease = null;

        using var deadline = new CancellationTokenSource(limits.TimeBudget);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

        try
        {
            lease = await clientFactory.CreateForCurrentUserAsync(["Directory.Read.All"], linked.Token)
                .WaitAsync(linked.Token);
            string? path = $"/v1.0/users?$select={UserSelect}&$top={limits.PageSize}";

            while (path is not null)
            {
                if (!requestedPaths.Add(path))
                {
                    return Result(false, "invalid_response", new GraphOperationResult(false, "invalid_response"));
                }

                var response = await lease.Transport
                    .SendAsync(new GraphRequest(HttpMethod.Get, path), linked.Token)
                    .WaitAsync(linked.Token);
                if (deadline.IsCancellationRequested)
                {
                    throw new OperationCanceledException(linked.Token);
                }

                if (!response.Result.IsSuccess)
                {
                    return Result(false, response.Result.Category, response.Result);
                }

                if (!TryReadPage(response.Content, out var pageUsers, out var nextPath)
                    || pageUsers.Any(user => ids.Contains(user.Id))
                    || pageUsers.Select(user => user.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() != pageUsers.Count)
                {
                    return Result(false, "invalid_response", new GraphOperationResult(false, "invalid_response"));
                }

                if (deadline.IsCancellationRequested)
                {
                    throw new OperationCanceledException(linked.Token);
                }

                pagesRead++;
                var remaining = limits.MaxRecords - users.Count;
                var acceptedCount = Math.Min(remaining, pageUsers.Count);
                for (var index = 0; index < acceptedCount; index++)
                {
                    users.Add(pageUsers[index]);
                    ids.Add(pageUsers[index].Id);
                }

                if (deadline.IsCancellationRequested)
                {
                    throw new OperationCanceledException(linked.Token);
                }

                if (acceptedCount < pageUsers.Count)
                {
                    return Result(false, "record_limit");
                }

                if (nextPath is null)
                {
                    return Result(true, "completed");
                }

                if (users.Count >= limits.MaxRecords)
                {
                    return Result(false, "record_limit");
                }

                if (pagesRead >= limits.MaxPages)
                {
                    return Result(false, "page_limit");
                }

                path = nextPath;
            }

            return Result(true, "completed");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            return Result(false, "time_limit", new GraphOperationResult(false, "timeout"));
        }
        finally
        {
            if (lease is not null)
            {
                await lease.DisposeAsync();
            }
        }

        LicenseHygieneUserScanResult Result(bool completed, string stopReason, GraphOperationResult? error = null) =>
            new(users.ToArray(), pagesRead, completed, stopReason, error, startedAt, DateTimeOffset.UtcNow);
    }

    private static bool TryReadPage(
        string content,
        out List<LicenseHygieneUserObservation> users,
        out string? nextPath)
    {
        users = [];
        nextPath = null;

        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("value", out var value)
                || value.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var user in value.EnumerateArray())
            {
                if (!TryReadUser(user, out var observation))
                {
                    return false;
                }

                users.Add(observation);
            }

            if (!root.TryGetProperty("@odata.nextLink", out var nextLink)
                || nextLink.ValueKind == JsonValueKind.Null)
            {
                return true;
            }

            if (nextLink.ValueKind != JsonValueKind.String
                || !TryNormalizeContinuation(nextLink.GetString(), out nextPath))
            {
                return false;
            }

            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryReadUser(JsonElement user, out LicenseHygieneUserObservation observation)
    {
        observation = default!;
        if (user.ValueKind != JsonValueKind.Object
            || !user.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(idElement.GetString()))
        {
            return false;
        }

        bool? accountEnabled = user.TryGetProperty("accountEnabled", out var enabled)
            && enabled.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? enabled.GetBoolean()
                : null;

        IReadOnlyList<string>? skuIds = null;
        var assignmentMalformed = false;
        if (user.TryGetProperty("assignedLicenses", out var assignments) && assignments.ValueKind != JsonValueKind.Null)
        {
            if (assignments.ValueKind != JsonValueKind.Array)
            {
                assignmentMalformed = true;
            }
            else
            {
                var parsedSkuIds = new List<string>();
                foreach (var assignment in assignments.EnumerateArray())
                {
                    if (assignment.ValueKind != JsonValueKind.Object
                        || !assignment.TryGetProperty("skuId", out var skuId)
                        || skuId.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(skuId.GetString()))
                    {
                        assignmentMalformed = true;
                        break;
                    }

                    parsedSkuIds.Add(skuId.GetString()!);
                }

                if (!assignmentMalformed)
                {
                    skuIds = parsedSkuIds.ToArray();
                }
            }
        }

        observation = new LicenseHygieneUserObservation(
            idElement.GetString()!,
            OptionalString(user, "displayName"),
            OptionalString(user, "userPrincipalName"),
            accountEnabled,
            skuIds,
            assignmentMalformed);
        return true;
    }

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryNormalizeContinuation(string? link, out string? path)
    {
        path = null;
        if (!Uri.TryCreate(link, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.IdnHost, "graph.microsoft.com", StringComparison.OrdinalIgnoreCase)
            || uri.Port != 443
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Fragment)
            || !string.Equals(uri.AbsolutePath, "/v1.0/users", StringComparison.Ordinal)
            || uri.Query.Length <= 1)
        {
            return false;
        }

        path = uri.PathAndQuery;
        return path.StartsWith("/v1.0/users?", StringComparison.Ordinal);
    }
}
