using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure.Graph;

public sealed class GraphDeviceAndAuthenticationReaderTests
{
    [Fact]
    public async Task Managed_device_reader_uses_intune_read_scope_and_maps_safe_inventory_fields()
    {
        var transport = new RecordingTransport("""
            {"value":[{"id":"device-1","deviceName":"LAPTOP-01","operatingSystem":"Windows","osVersion":"11","complianceState":"compliant","managementState":"managed","managedDeviceOwnerType":"company","lastSyncDateTime":"2026-09-22T08:00:00Z","userId":"user-1","azureADDeviceId":"aad-1","serialNumber":"serial-1","manufacturer":"Contoso","model":"Model X"}]}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadAsync(new DeviceSearchQuery("LAPTOP-01", 25, "compliant", "Windows"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.Items.Should().ContainSingle().Which.DeviceName.Should().Be("LAPTOP-01");
        transport.Requests.Single().PathAndQuery.Should().Contain("deviceManagement/managedDevices");
        transport.Requests.Single().PathAndQuery.Should().Contain("contains%28deviceName%2C%27LAPTOP-01%27%29");
    }

    [Fact]
    public async Task Managed_device_reader_preserves_graph_next_link_for_safe_server_side_pagination()
    {
        var transport = new RecordingTransport("""
            {"value":[{"id":"device-1","deviceName":"LAPTOP-01"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/deviceManagement/managedDevices?$skiptoken=opaque"}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadAsync(new DeviceSearchQuery(PageSize: 1), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.ContinuationLink.Should().Be("/v1.0/deviceManagement/managedDevices?$skiptoken=opaque");
    }

    [Fact]
    public async Task Managed_device_reader_maps_an_unprovisioned_target_tenant_to_a_guidable_error()
    {
        var transport = new FailedTransport("{\"error\":{\"code\":\"BadRequest\",\"message\":\"Request not applicable to target tenant.\"}}");
        var result = await new GraphManagedDeviceReader(new RecordingFactory(transport)).ReadAsync(new DeviceSearchQuery(PageSize: 25), CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("not_provisioned");
        result.Error.StatusCode.Should().Be(400);
    }

    [Fact]
    public async Task Associated_device_reader_issues_one_targeted_user_id_filter()
    {
        var transport = new RecordingTransport("""
            {"value":[{"id":"device-1","userId":"user-1"}]}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadForUserAsync("user-1", CancellationToken.None);

        result.Error.Should().BeNull();
        transport.Requests.Should().ContainSingle();
        transport.Requests.Single().PathAndQuery.Should()
            .Contain("%24filter=userId%20eq%20%27user-1%27");
    }

    [Fact]
    public async Task Associated_device_reader_maps_graph_unsupported_user_id_filter_to_safe_category()
    {
        var transport = new FailedTransport("""
            {"error":{"code":"Request_BadRequest","message":"The userId filter is not supported for this resource."}}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadForUserAsync("user-1", CancellationToken.None);

        result.Error!.Category.Should().Be("association_query_unsupported");
    }

    [Fact]
    public async Task Associated_device_reader_preserves_not_provisioned_for_target_tenant_400()
    {
        var transport = new FailedTransport("""
            {"error":{"code":"BadRequest","message":"Request not applicable to target tenant."}}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadForUserAsync("user-1", CancellationToken.None);

        result.Error!.Category.Should().Be("not_provisioned");
    }

    [Fact]
    public async Task Associated_device_reader_preserves_safe_category_for_unrelated_bad_request()
    {
        var transport = new FailedTransport("""
            {"error":{"code":"BadRequest","message":"The request is invalid for this tenant."}}
            """);
        var reader = new GraphManagedDeviceReader(new RecordingFactory(transport));

        var result = await reader.ReadForUserAsync("user-1", CancellationToken.None);

        result.Error!.Category.Should().Be("temporarily_unavailable");
    }

    [Fact]
    public async Task Authentication_method_reader_normalizes_method_type_without_returning_secret_values()
    {
        var transport = new RecordingTransport("""
            {"value":[{"id":"method-1","@odata.type":"#microsoft.graph.fido2AuthenticationMethod","displayName":"YubiKey","createdDateTime":"2026-09-20T08:00:00Z","model":"YubiKey 5"}]}
            """);
        var reader = new GraphAuthenticationMethodReader(new RecordingFactory(transport));

        var result = await reader.ReadAsync("user-1", CancellationToken.None);

        result.Error.Should().BeNull();
        result.Value.Should().ContainSingle().Which.Type.Should().Be("fido2AuthenticationMethod");
        result.Value.Single().DisplayName.Should().Be("YubiKey");
        transport.Requests.Single().PathAndQuery.Should().Contain("/users/user-1/authentication/methods");
        transport.Requests.Single().PathAndQuery.Should().NotContain("keyMaterial");
    }

    [Fact]
    public async Task Managed_device_sync_uses_the_device_action_endpoint_and_write_scope()
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphManagedDeviceCommands(factory).ExecuteAsync("device-1", DeviceActionNames.Sync, "key-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.Scopes.Single().Should().Equal(GraphScopeCatalog.DeviceWriteScopes);
        transport.Requests.Single().Method.Should().Be(HttpMethod.Post);
        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/deviceManagement/managedDevices/device-1/sync");
        transport.Requests.Single().Headers!["Idempotency-Key"].Should().Be("key-1");
    }

    [Fact]
    public async Task Remote_lock_uses_graphs_camel_case_action_name()
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        await new GraphManagedDeviceCommands(factory).ExecuteAsync("device-1", DeviceActionNames.RemoteLock, "key-remote", CancellationToken.None);

        transport.Requests.Single().PathAndQuery.Should().EndWith("/remoteLock");
    }

    [Fact]
    public async Task Authentication_method_remove_maps_fido2_to_the_specific_graph_collection()
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphAuthenticationMethodCommands(factory).RemoveAsync("user-1", "method-1", "fido2AuthenticationMethod", "key-2", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.Scopes.Single().Should().Equal(GraphScopeCatalog.AuthenticationMethodWriteScopes);
        transport.Requests.Single().Method.Should().Be(HttpMethod.Delete);
        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/users/user-1/authentication/fido2Methods/method-1");
    }

    [Fact]
    public async Task Temporary_access_pass_uses_single_use_sixty_minute_payload_and_maps_only_safe_fields()
    {
        var transport = new RecordingTransport("""
            {"temporaryAccessPass":"ABC123","id":"tap-1","startDateTime":"2026-09-23T10:00:00Z","lifetimeInMinutes":60,"isUsableOnce":true,"secret":"must-not-be-exposed"}
            """);
        var factory = new RecordingFactory(transport);

        var result = await new GraphAuthenticationMethodCommands(factory)
            .CreateTemporaryAccessPassAsync("user-1", "tap-key", CancellationToken.None);

        result.TemporaryAccessPass.Should().Be("ABC123");
        result.Id.Should().Be("tap-1");
        result.LifetimeInMinutes.Should().Be(60);
        result.IsUsableOnce.Should().BeTrue();
        factory.Scopes.Single().Should().Equal(GraphScopeCatalog.AuthenticationMethodWriteScopes);
        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/users/user-1/authentication/temporaryAccessPassMethods");
        transport.Requests.Single().Headers!["Idempotency-Key"].Should().Be("tap-key");
        transport.RequestBodies.Single().Should().Be("{\"lifetimeInMinutes\":60,\"isUsableOnce\":true}");
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task Revoke_sessions_uses_the_v1_user_action_and_session_scope()
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        await new GraphUserSessionCommands(factory).RevokeAsync("user-1", "key-1", CancellationToken.None);

        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/users/user-1/revokeSignInSessions");
        transport.Requests.Single().Method.Should().Be(HttpMethod.Post);
        factory.Scopes.Single().Should().Equal(GraphScopeCatalog.UserSessionWriteScopes);
    }

    [Theory]
    [InlineData("{\"id\":\"tap-1\",\"lifetimeInMinutes\":60,\"isUsableOnce\":true}")]
    [InlineData("{\"temporaryAccessPass\":\"ABC123\",\"lifetimeInMinutes\":60,\"isUsableOnce\":true}")]
    [InlineData("{\"temporaryAccessPass\":\"ABC123\",\"id\":\"tap-1\",\"lifetimeInMinutes\":30,\"isUsableOnce\":true}")]
    [InlineData("{\"temporaryAccessPass\":\"ABC123\",\"id\":\"tap-1\",\"lifetimeInMinutes\":60,\"isUsableOnce\":false}")]
    public async Task Temporary_access_pass_rejects_missing_or_contradictory_response_contract(string content)
    {
        var transport = new RecordingTransport(content);
        var result = await new GraphAuthenticationMethodCommands(new RecordingFactory(transport))
            .CreateTemporaryAccessPassAsync("user-1", "tap-key", CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be("invalid_response");
        result.TemporaryAccessPass.Should().BeNull();
        transport.Requests.Should().ContainSingle();
    }

    [Theory]
    [InlineData("user/1", "key-1")]
    [InlineData("user\\1", "key-1")]
    [InlineData("user\u0001", "key-1")]
    [InlineData("user-1", "")]
    public async Task Session_adapter_rejects_unsafe_target_or_blank_idempotency_without_graph_dispatch(string userObjectId, string idempotencyKey)
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphUserSessionCommands(factory).RevokeAsync(userObjectId, idempotencyKey, CancellationToken.None);

        result.Category.Should().Be("invalid_request");
        transport.Requests.Should().BeEmpty();
        factory.Scopes.Should().BeEmpty();
    }

    private sealed class RecordingFactory(IGraphTransport transport) : IDelegatedGraphClientFactory
    {
        public List<IReadOnlyCollection<string>> Scopes { get; } = [];

        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        {
            Scopes.Add(scopes);
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingTransport(string content) : IGraphTransport
    {
        public IReadOnlyCollection<string> Scopes { get; } = [];
        public List<GraphRequest> Requests { get; } = [];
        public List<string> RequestBodies { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            RequestBodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
            return Task.FromResult(new GraphTransportResponse(GraphOperationResult.Success(), content, 1, new Dictionary<string, IReadOnlyCollection<string>>()));
        }
    }

    private sealed class FailedTransport(string content) : IGraphTransport
    {
        public IReadOnlyCollection<string> Scopes { get; } = [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new GraphTransportResponse(
                new GraphOperationResult(false, "temporarily_unavailable", 400),
                content,
                1,
                new Dictionary<string, IReadOnlyCollection<string>>()));
    }
}
