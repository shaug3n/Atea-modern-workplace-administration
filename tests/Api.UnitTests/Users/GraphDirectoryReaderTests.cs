using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class GraphDirectoryReaderTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace", "member"));

    [Fact]
    public async Task License_part_number_is_resolved_from_catalog_before_user_filter_is_sent()
    {
        var transport = new RecordingTransport(
            new GraphTransportResponse(GraphOperationResult.Success(), "{\"value\":[{\"skuId\":\"11111111-1111-1111-1111-111111111111\",\"skuPartNumber\":\"ENTERPRISEPACK\"}]}", 1, new Dictionary<string, IReadOnlyCollection<string>>()),
            new GraphTransportResponse(GraphOperationResult.Success(), "{\"value\":[]}", 1, new Dictionary<string, IReadOnlyCollection<string>>()));

        var factory = new RecordingFactory(transport);
        var result = await new GraphDirectoryReader(factory).SearchAsync(
            Workspace,
            new UserSearchQuery(License: "enterprisepack"),
            CancellationToken.None);

        result.Error.Should().BeNull();
        factory.RequestedScopes.Should().Equal("Directory.Read.All");
        transport.Requests.Select(request => request.PathAndQuery).Should().ContainInOrder(
            "/v1.0/subscribedSkus?$select=skuId,skuPartNumber",
            "/v1.0/users?$select=id,displayName,userPrincipalName,mail,accountEnabled,userType,givenName,surname,jobTitle,department,officeLocation,mobilePhone,usageLocation,onPremisesSyncEnabled,creationType&$top=25&$filter=assignedLicenses%2Fany%28a%3Aa%2FskuId%20eq%2011111111-1111-1111-1111-111111111111%29");
    }

    [Fact]
    public async Task Unknown_license_input_returns_explicit_invalid_filter_error_without_user_request()
    {
        var transport = new RecordingTransport(
            new GraphTransportResponse(GraphOperationResult.Success(), "{\"value\":[]}", 1, new Dictionary<string, IReadOnlyCollection<string>>()));

        var result = await new GraphDirectoryReader(new RecordingFactory(transport)).SearchAsync(
            Workspace,
            new UserSearchQuery(License: "not-a-sku"),
            CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_license_filter");
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task License_resolution_follows_subscribed_SKU_pagination_before_roster_query()
    {
        var transport = new RecordingTransport(
            new GraphTransportResponse(GraphOperationResult.Success(), """{"value":[],"@odata.nextLink":"https://graph.microsoft.com/v1.0/subscribedSkus?$skiptoken=next"}""", 1, new Dictionary<string, IReadOnlyCollection<string>>()),
            new GraphTransportResponse(GraphOperationResult.Success(), """{"value":[{"skuId":"11111111-1111-1111-1111-111111111111","skuPartNumber":"ENTERPRISEPACK"}]}""", 1, new Dictionary<string, IReadOnlyCollection<string>>()),
            new GraphTransportResponse(GraphOperationResult.Success(), """{"value":[{"id":"user-1","displayName":"Ada"}]}""", 1, new Dictionary<string, IReadOnlyCollection<string>>()));

        var result = await new GraphDirectoryReader(new RecordingFactory(transport)).SearchAsync(Workspace,
            new UserSearchQuery(License: "11111111-1111-1111-1111-111111111111"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Items.Should().ContainSingle().Which.Id.Should().Be("user-1");
        transport.Requests[1].PathAndQuery.Should().Be("/v1.0/subscribedSkus?$skiptoken=next");
    }

    private sealed class RecordingFactory(RecordingTransport transport) : IDelegatedGraphClientFactory
    {
        public IReadOnlyCollection<string> RequestedScopes { get; private set; } = [];
        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken) { RequestedScopes = scopes; return Task.FromResult(new GraphClientLease(transport, scopes)); }
    }

    private sealed class RecordingTransport(params GraphTransportResponse[] responses) : IGraphTransport
    {
        private int index;
        public IReadOnlyCollection<string> Scopes => [];
        public List<GraphRequest> Requests { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses[Math.Min(index++, responses.Length - 1)]);
        }
    }
}
