using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public sealed class OverviewGraphReader(IDelegatedGraphClientFactory clientFactory) : IOverviewGraphReader
{
    private const string TotalUsersPath = "/v1.0/users?$count=true&$top=1";
    private const string AssignedUsersPath = "/v1.0/users?$filter=assignedLicenses/$count%20ne%200&$count=true&$top=1";
    private static readonly IReadOnlyDictionary<string, string> CountHeaders =
        new Dictionary<string, string> { ["ConsistencyLevel"] = "eventual" };

    public async Task<GraphReadResult<int>> ReadUserCountAsync(
        WorkspaceContext context,
        string delegatedScope,
        CancellationToken cancellationToken)
    {
        if (delegatedScope is not ("Directory.Read.All" or "User.Read.All"))
        {
            return GraphReadResult<int>.Failed(new GraphOperationResult(false, "invalid_scope"));
        }

        await using var lease = await clientFactory.CreateForCurrentUserAsync([delegatedScope], cancellationToken);
        var count = await ReadCountAsync(lease.Transport, TotalUsersPath, cancellationToken);
        return count.Error is null
            ? GraphReadResult<int>.Succeeded(count.Value, count.CorrelationId, count.RequestId)
            : GraphReadResult<int>.Failed(count.Error);
    }

    public async Task<GraphReadResult<OverviewLicenseCounts>> ReadLicenseCountsAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(["Directory.Read.All"], cancellationToken);

        var total = await ReadCountAsync(lease.Transport, TotalUsersPath, cancellationToken);
        if (total.Error is not null)
        {
            return GraphReadResult<OverviewLicenseCounts>.Failed(total.Error);
        }

        var assigned = await ReadCountAsync(lease.Transport, AssignedUsersPath, cancellationToken);
        if (assigned.Error is not null)
        {
            return GraphReadResult<OverviewLicenseCounts>.Failed(assigned.Error);
        }

        if (assigned.Value > total.Value)
        {
            return GraphReadResult<OverviewLicenseCounts>.Failed(
                new GraphOperationResult(false, "invalid_response"));
        }

        return GraphReadResult<OverviewLicenseCounts>.Succeeded(
            new OverviewLicenseCounts(total.Value, assigned.Value),
            assigned.CorrelationId,
            assigned.RequestId);
    }

    private static async Task<CountResult> ReadCountAsync(
        IGraphTransport transport,
        string path,
        CancellationToken cancellationToken)
    {
        var response = await transport.SendAsync(
            new GraphRequest(HttpMethod.Get, path, Headers: CountHeaders),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return new CountResult(0, response.Result, response.Result.CorrelationId, response.Result.RequestId);
        }

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("@odata.count", out var count)
                || !count.TryGetInt32(out var value)
                || value < 0)
            {
                return CountResult.Invalid;
            }

            return new CountResult(value, null, response.Result.CorrelationId, response.Result.RequestId);
        }
        catch (Exception exception) when (exception is JsonException or InvalidOperationException)
        {
            return CountResult.Invalid;
        }
    }

    private sealed record CountResult(
        int Value,
        GraphOperationResult? Error,
        string? CorrelationId = null,
        string? RequestId = null)
    {
        public static CountResult Invalid { get; } = new(0, new GraphOperationResult(false, "invalid_response"));
    }
}
