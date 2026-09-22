using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Devices;

public sealed class DeviceCommandServiceTests
{
    [Fact]
    public async Task Device_command_is_denied_when_only_read_write_scope_is_granted()
    {
        var commands = new RecordingCommands();
        var service = CreateService(commands, Snapshot(["DeviceManagementManagedDevices.Read.All", "DeviceManagementManagedDevices.ReadWrite.All"]));

        var result = await service.ExecuteAsync(Context(), "device-1", DeviceActionNames.Sync, "key-1", CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.Denied);
        result.RequiredCapability.Should().Be(Capability.DevicesPrivilegedManage);
        commands.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Denied_command_audit_uses_the_normalized_operation_name()
    {
        var audit = new RecordingAuditWriter();
        var service = CreateService(new RecordingCommands(), Snapshot(["DeviceManagementManagedDevices.ReadWrite.All"]), audit);

        var result = await service.ExecuteAsync(Context(), "device-1", DeviceActionNames.Wipe, "key-1", CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.Denied);
        audit.Events.Should().ContainSingle().Which.Action.Should().Be("devices.wipe");
    }

    [Theory]
    [InlineData(DeviceActionNames.Sync, "devices.sync")]
    [InlineData(DeviceActionNames.RemoteLock, "devices.remote-lock")]
    [InlineData(DeviceActionNames.Restart, "devices.restart")]
    [InlineData(DeviceActionNames.Retire, "devices.retire")]
    [InlineData(DeviceActionNames.Wipe, "devices.wipe")]
    public async Task Every_privileged_action_uses_the_privileged_capability_and_audits_safe_metadata(string action, string operation)
    {
        var audit = new RecordingAuditWriter();
        var commands = new RecordingCommands();
        var service = CreateService(commands, Snapshot(GraphScopeCatalog.DevicePrivilegedOperationScopes), audit);

        var result = await service.ExecuteAsync(Context(), "device-1", action, "key-1", CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.Succeeded);
        result.RequiredCapability.Should().Be(Capability.DevicesPrivilegedManage);
        commands.Actions.Should().ContainSingle().Which.Should().Be(action);
        audit.Events.Should().ContainSingle().Which.Action.Should().Be(operation);
        audit.Events.Single().SafeMetadataJson.Should().Be("{}");
        audit.Events.Single().SafeMetadataJson.Should().NotContain("key-1");
    }

    [Theory]
    [InlineData("device/1", "key-1")]
    [InlineData("device\\1", "key-1")]
    [InlineData("device\u0001", "key-1")]
    [InlineData("device-1", "")]
    public async Task Invalid_device_or_idempotency_input_does_not_dispatch(string deviceId, string key)
    {
        var commands = new RecordingCommands();
        var service = CreateService(commands, Snapshot(GraphScopeCatalog.DevicePrivilegedOperationScopes));

        var result = await service.ExecuteAsync(Context(), deviceId, DeviceActionNames.Sync, key, CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.InvalidTarget);
        commands.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("bad\u0001key")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Malformed_idempotency_input_does_not_dispatch(string key)
    {
        var commands = new RecordingCommands();
        var service = CreateService(commands, Snapshot(GraphScopeCatalog.DevicePrivilegedOperationScopes));

        var result = await service.ExecuteAsync(Context(), "device-1", DeviceActionNames.Sync, key, CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.InvalidTarget);
        result.Error.Should().Be("invalid_idempotency_key");
        commands.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_action_does_not_dispatch_or_expose_graph_details()
    {
        var commands = new RecordingCommands();
        var service = CreateService(commands, Snapshot(GraphScopeCatalog.DevicePrivilegedOperationScopes));

        var result = await service.ExecuteAsync(Context(), "device-1", "delete", "key-1", CancellationToken.None);

        result.Status.Should().Be(DeviceCommandStatus.InvalidTarget);
        result.Error.Should().Be("invalid_action");
        result.RequiredCapability.Should().Be(Capability.DevicesPrivilegedManage);
        commands.Calls.Should().Be(0);
    }

    private static DeviceCommandService CreateService(RecordingCommands commands, GraphAuthorizationSnapshot snapshot, RecordingAuditWriter? audit = null) =>
        new(commands, new StaticAuthorizationReader(snapshot), new MemoryIdempotencyService(), audit ?? new RecordingAuditWriter());

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member", null),
        new AuthorizationWorkspaceMembership(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Workspace", "member"));

    private static GraphAuthorizationSnapshot Snapshot(IReadOnlyCollection<string> scopes) => GraphAuthorizationSnapshot.Available(
        "actor-1", GraphScopeCatalog.DeviceReadScopes.Concat(scopes).Distinct().ToArray(),
        [new DirectoryRoleSnapshot(EntraRoleCatalog.IntuneAdministratorTemplateId, "Intune Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class RecordingCommands : IManagedDeviceCommands
    {
        public int Calls { get; private set; }
        public List<string> Actions { get; } = [];

        public Task<GraphOperationResult> ExecuteAsync(string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            Actions.Add(action);
            return Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
        }
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
