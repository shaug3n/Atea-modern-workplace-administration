using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Overview;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IOverviewDataReader
{
    Task<GraphReadResult<OverviewData>> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken);
}

public sealed class GraphOverviewDataReader(IDelegatedGraphClientFactory clientFactory) : IOverviewDataReader
{
    public async Task<GraphReadResult<OverviewData>> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.DirectoryReadScopes, cancellationToken);
        var headers = new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

        var total = await ReadCountAsync(lease.Transport, "/v1.0/users?$count=true&$top=1", headers, cancellationToken);
        if (total.Error is not null)
        {
            return GraphReadResult<OverviewData>.Failed(total.Error);
        }

        var assigned = await ReadCountAsync(
            lease.Transport,
            "/v1.0/users?$filter=assignedLicenses/$count%20ne%200&$count=true&$top=1",
            headers,
            cancellationToken);
        if (assigned.Error is not null)
        {
            return GraphReadResult<OverviewData>.Failed(assigned.Error);
        }

        return GraphReadResult<OverviewData>.Succeeded(new OverviewData(total.Count, assigned.Count, 0));
    }

    private static async Task<CountResult> ReadCountAsync(
        IGraphTransport transport,
        string path,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, path, Headers: headers), cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return new CountResult(0, response.Result);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (!document.RootElement.TryGetProperty("@odata.count", out var count) || !count.TryGetInt32(out var value))
            {
                return new CountResult(0, new GraphOperationResult(false, "invalid_response"));
            }

            return new CountResult(value, null);
        }
        catch (JsonException)
        {
            return new CountResult(0, new GraphOperationResult(false, "invalid_response"));
        }
    }

    private sealed record CountResult(int Count, GraphOperationResult? Error);
}
