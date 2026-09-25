using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Licenses;

public sealed class LicenseInventoryTests
{
    private static readonly WorkspaceContext Context = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com", "Reader", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Workspace", "member", ModuleKeys: ["licenses"]));

    [Fact]
    public async Task Read_only_license_viewer_can_load_inventory_without_assignment_permission()
    {
        var snapshot = GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"],
            [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);
        var reader = new StubReader();
        var response = await new LicenseOverviewService(reader, new SnapshotReader(snapshot)).GetAsync(Context, new LicenseOverviewRequest(), CancellationToken.None);

        CapabilityEvaluator.Evaluate(snapshot, Context.Membership)[Capability.LicensesAssign].State.Should().NotBe(CapabilityState.Allowed);
        response.Access.Authorization!.Capability.Should().Be(Capability.LicensesView);
        response.Items.Should().ContainSingle();
        reader.Calls.Should().Be(1);
    }

    [Fact]
    public void User_read_scope_alone_does_not_claim_subscribed_SKU_access()
    {
        var snapshot = GraphAuthorizationSnapshot.Available("reader", ["User.Read.All"],
            [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);
        CapabilityEvaluator.Evaluate(snapshot, Context.Membership)[Capability.LicensesView].State.Should().NotBe(CapabilityState.Allowed);
    }

    [Fact]
    public async Task Inventory_uses_Graph_part_number_and_exact_enabled_consumed_available_counts_across_pages()
    {
        var transport = new StubTransport(
            """{"value":[{"skuId":"11111111-1111-1111-1111-111111111111","skuPartNumber":"ENTERPRISEPACK","consumedUnits":7,"prepaidUnits":{"enabled":10}},""" +
            """{"skuId":"22222222-2222-2222-2222-222222222222","skuPartNumber":"OVERALLOCATED","consumedUnits":6,"prepaidUnits":{"enabled":5}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/subscribedSkus?$skiptoken=next"}""",
            """{"value":[{"skuId":"33333333-3333-3333-3333-333333333333","skuPartNumber":"VISIOCLIENT","consumedUnits":1,"prepaidUnits":{"enabled":2}}]}""");
        var result = await new GraphLicenseOverviewReader(new StubFactory(transport)).ReadAsync(Context, new LicenseOverviewQuery(), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.Should().HaveCount(3);
        result.Value[0].DisplayName.Should().Be("ENTERPRISEPACK");
        result.Value[0].Purchased.Should().Be(10);
        result.Value[0].Assigned.Should().Be(7);
        result.Value[0].Available.Should().Be(3);
        result.Value[1].Available.Should().Be(0);
        transport.Paths.Should().ContainInOrder(
            "/v1.0/subscribedSkus?$select=skuId,skuPartNumber,consumedUnits,prepaidUnits",
            "/v1.0/subscribedSkus?$skiptoken=next");
    }

    [Fact]
    public async Task Inventory_fails_entire_read_when_later_Graph_page_fails()
    {
        var transport = new StubTransport("""{"value":[{"skuId":"11111111-1111-1111-1111-111111111111","skuPartNumber":"E3","consumedUnits":1,"prepaidUnits":{"enabled":2}}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/subscribedSkus?$skiptoken=next"}""") { FailSecond = true };
        var result = await new GraphLicenseOverviewReader(new StubFactory(transport)).ReadAsync(Context, new LicenseOverviewQuery(), CancellationToken.None);
        result.Error!.Category.Should().Be("throttled");
    }

    [Fact]
    public async Task Missing_count_in_Graph_response_is_an_error_not_a_fabricated_zero()
    {
        var transport = new StubTransport("""{"value":[{"skuId":"11111111-1111-1111-1111-111111111111","skuPartNumber":"E3","prepaidUnits":{"enabled":2}}]}""");
        var result = await new GraphLicenseOverviewReader(new StubFactory(transport)).ReadAsync(Context, new LicenseOverviewQuery(), CancellationToken.None);
        result.Error!.Category.Should().Be("invalid_response");
    }

    private sealed class StubReader : ILicenseOverviewReader
    {
        public int Calls { get; private set; }
        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(WorkspaceContext context, LicenseOverviewQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([new("sku", "PART", "PART", 2, 3)]));
        }
    }

    private sealed class SnapshotReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class StubFactory(StubTransport transport) : IDelegatedGraphClientFactory
    {
        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken) => Task.FromResult(new GraphClientLease(transport, scopes));
    }

    private sealed class StubTransport(params string[] pages) : IGraphTransport
    {
        public IReadOnlyCollection<string> Scopes => [];
        public List<string> Paths { get; } = [];
        public bool FailSecond { get; init; }
        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Paths.Add(request.PathAndQuery);
            var response = FailSecond && Paths.Count == 2
                ? new GraphTransportResponse(new GraphOperationResult(false, "throttled", 429), "{}", 1, new Dictionary<string, IReadOnlyCollection<string>>())
                : new GraphTransportResponse(GraphOperationResult.Success(), pages[Paths.Count - 1], 1, new Dictionary<string, IReadOnlyCollection<string>>());
            return Task.FromResult(response);
        }
    }
}
