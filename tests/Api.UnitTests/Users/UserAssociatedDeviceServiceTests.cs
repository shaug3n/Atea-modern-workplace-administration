using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserAssociatedDeviceServiceTests
{
    [Fact]
    public async Task Associated_device_service_does_not_fallback_to_an_inventory_scan_when_filter_is_rejected()
    {
        var inventoryReader = new RecordingDeviceReader
        {
            AssociatedResult = GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Failed(
                new GraphOperationResult(false, "association_query_unsupported", 400))
        };
        var service = new UserAssociatedDeviceService(
            inventoryReader,
            new StaticAuthorizationReader(AllowedSnapshot()),
            () => DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        var response = await service.GetAsync(Context(), "user-1", CancellationToken.None);

        response.Items.Should().BeEmpty();
        response.Error!.Category.Should().Be("association_query_unsupported");
        inventoryReader.ReadAsyncCalls.Should().Be(0);
        inventoryReader.ReadForUserAsyncCalls.Should().Be(1);
    }

    [Fact]
    public async Task Associated_device_service_returns_read_only_access_for_global_reader()
    {
        var reader = new RecordingDeviceReader();
        var service = new UserAssociatedDeviceService(
            reader,
            new StaticAuthorizationReader(AllowedSnapshot(EntraRoleCatalog.GlobalReaderTemplateId)),
            () => DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        var response = await service.GetAsync(Context(), "user-1", CancellationToken.None);

        response.Access.State.Should().Be(CapabilityState.ReadOnly);
        response.Items.Should().BeEmpty();
        reader.ReadForUserAsyncCalls.Should().Be(1);
    }

    [Fact]
    public async Task Associated_device_service_returns_consent_required_without_reading_devices()
    {
        var reader = new RecordingDeviceReader();
        var service = new UserAssociatedDeviceService(
            reader,
            new StaticAuthorizationReader(GraphAuthorizationSnapshot.Unavailable(CapabilityState.ConsentRequired, true)),
            () => DateTimeOffset.Parse("2026-09-22T10:00:00Z"));

        var response = await service.GetAsync(Context(), "user-1", CancellationToken.None);

        response.Access.State.Should().Be(CapabilityState.ConsentRequired);
        response.Error!.Category.Should().Be("capability_required");
        reader.ReadForUserAsyncCalls.Should().Be(0);
    }

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member"),
        new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", "admin"));

    private static GraphAuthorizationSnapshot AllowedSnapshot(string roleTemplateId = EntraRoleCatalog.GlobalAdministratorTemplateId) =>
        GraphAuthorizationSnapshot.Available("user-1", GraphScopeCatalog.DeviceReadScopes,
            [new DirectoryRoleSnapshot(roleTemplateId, roleTemplateId, DirectoryRoleAssignmentState.Active,
                roleTemplateId == EntraRoleCatalog.GlobalReaderTemplateId ? "/administrativeUnits/unit-1" : "/")]);

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class RecordingDeviceReader : IManagedDeviceReader
    {
        public int ReadAsyncCalls { get; private set; }
        public int ReadForUserAsyncCalls { get; private set; }
        public GraphReadResult<IReadOnlyList<ManagedDeviceSummary>> AssociatedResult { get; set; } =
            GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>.Succeeded([]);

        public Task<GraphReadResult<PagedResult<ManagedDeviceSummary>>> ReadAsync(DeviceSearchQuery query, CancellationToken cancellationToken)
        {
            ReadAsyncCalls++;
            return Task.FromResult(GraphReadResult<PagedResult<ManagedDeviceSummary>>.Succeeded(new PagedResult<ManagedDeviceSummary>([], [], null)));
        }

        public Task<GraphReadResult<IReadOnlyList<ManagedDeviceSummary>>> ReadForUserAsync(string userObjectId, CancellationToken cancellationToken)
        {
            ReadForUserAsyncCalls++;
            return Task.FromResult(AssociatedResult);
        }
    }
}
