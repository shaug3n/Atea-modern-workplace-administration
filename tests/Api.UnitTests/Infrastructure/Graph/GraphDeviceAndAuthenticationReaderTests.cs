using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Identity.Client;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure.Graph;

public sealed class GraphDeviceAndAuthenticationReaderTests
{
    [Fact]
    public async Task Device_detail_uses_a_targeted_get_and_keeps_only_supported_fields()
    {
        var transport = new RecordingTransport("""{"id":"device-1","deviceName":"WIN-01","azureADDeviceId":"aad-1","serialNumber":"serial","unsupportedSecret":"must-not-leak"}""");
        var result = await new GraphManagedDeviceReader(new RecordingFactory(transport)).GetAsync("device-1", CancellationToken.None);
        result.Value!.AzureAdDeviceId.Should().Be("aad-1");
        transport.Requests.Single().PathAndQuery.Should().StartWith("/v1.0/deviceManagement/managedDevices/device-1?$select=");
        System.Text.Json.JsonSerializer.Serialize(result.Value).Should().NotContain("must-not-leak");
    }

    [Fact]
    public async Task Device_detail_reports_missing_delegated_consent_when_token_acquisition_requires_it()
    {
        var result = await new GraphManagedDeviceReader(new ConsentRequiredFactory()).GetAsync("device-1", CancellationToken.None);
        result.Error!.Category.Should().Be("consent_required");
    }

    [Fact]
    public async Task Bitlocker_metadata_filter_never_selects_key_and_reveal_selects_only_key()
    {
        var metadataTransport = new RecordingTransport("""{"value":[{"id":"key-1","deviceId":"aad-1","createdDateTime":"2026-09-01T00:00:00Z","volumeType":"1","key":"must-not-leak"}]}""");
        var metadata = await new GraphDeviceRecoveryReader(new RecordingFactory(metadataTransport)).ListBitlockerAsync("aad-1", false, CancellationToken.None);
        metadata.Value.Should().ContainSingle().Which.Id.Should().Be("key-1");
        System.Text.Json.JsonSerializer.Serialize(metadata.Value).Should().NotContain("must-not-leak");
        metadataTransport.Requests.Single().PathAndQuery.Should().Contain("deviceId");
        metadataTransport.Requests.Single().PathAndQuery.Should().NotContain("$select=key");

        var secretTransport = new RecordingTransport("""{"id":"key-1","key":"recovery-secret"}""");
        var secret = await new GraphDeviceRecoveryReader(new RecordingFactory(secretTransport)).GetBitlockerAsync("key-1", CancellationToken.None);
        secret.Value.Key.Should().Be("recovery-secret");
        secretTransport.Requests.Single().PathAndQuery.Should().EndWith("?$select=key");
    }

    [Fact]
    public async Task Bitlocker_metadata_follows_Graph_pagination_for_the_target_device()
    {
        var transport = new PagedTransport([
            """{"value":[{"id":"key-1","deviceId":"aad-1"}],"@odata.nextLink":"https://graph.microsoft.com/v1.0/informationProtection/bitlocker/recoveryKeys?$skiptoken=page-2"}""",
            """{"value":[{"id":"key-2","deviceId":"aad-1"}]}"""
        ]);
        var result = await new GraphDeviceRecoveryReader(new RecordingFactory(transport)).ListBitlockerAsync("aad-1", false, CancellationToken.None);
        result.Value.Select(key => key.Id).Should().Equal("key-1", "key-2");
        transport.Requests.Should().HaveCount(2);
        transport.Requests[1].PathAndQuery.Should().Be("/v1.0/informationProtection/bitlocker/recoveryKeys?$skiptoken=page-2");
    }

    [Fact]
    public async Task Bitlocker_reveal_rejects_a_Graph_response_for_another_key_id()
    {
        var transport = new RecordingTransport("""{"id":"different-key","key":"must-not-return"}""");
        var result = await new GraphDeviceRecoveryReader(new RecordingFactory(transport)).GetBitlockerAsync("key-1", CancellationToken.None);
        result.Error!.Category.Should().Be("invalid_response");
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task Laps_reveal_decodes_utf16_base64_and_rejects_malformed_secret()
    {
        var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("Pāssword!"));
        var transport = new RecordingTransport($$"""{"id":"aad-1","credentials":[{"accountName":"LocalAdmin","passwordBase64":"{{encoded}}"}]}""");
        var secret = await new GraphDeviceRecoveryReader(new RecordingFactory(transport)).GetLapsSecretAsync("aad-1", CancellationToken.None);
        secret.Value.Password.Should().Be("Pāssword!");
        transport.Requests.Single().PathAndQuery.Should().EndWith("?$select=credentials");

        var malformed = new RecordingTransport("""{"id":"aad-1","credentials":[{"accountName":"LocalAdmin","passwordBase64":"?"}]}""");
        var rejected = await new GraphDeviceRecoveryReader(new RecordingFactory(malformed)).GetLapsSecretAsync("aad-1", CancellationToken.None);
        rejected.Error!.Category.Should().Be("invalid_response");
    }

    [Fact]
    public async Task Laps_reveal_chooses_the_latest_backed_up_credential()
    {
        var oldPassword = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("old-password"));
        var newPassword = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("new-password"));
        var transport = new RecordingTransport($$"""{"id":"aad-1","credentials":[{"accountName":"Admin","backupDateTime":"2026-08-01T00:00:00Z","passwordBase64":"{{oldPassword}}"},{"accountName":"Admin","backupDateTime":"2026-09-01T00:00:00Z","passwordBase64":"{{newPassword}}"}]}""");
        var result = await new GraphDeviceRecoveryReader(new RecordingFactory(transport)).GetLapsSecretAsync("aad-1", CancellationToken.None);
        result.Value.Password.Should().Be("new-password");
    }

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

    [Theory]
    [InlineData(DeviceActionNames.Sync, "syncDevice")]
    [InlineData(DeviceActionNames.RemoteLock, "remoteLock")]
    [InlineData(DeviceActionNames.Restart, "rebootNow")]
    [InlineData(DeviceActionNames.Retire, "retire")]
    [InlineData(DeviceActionNames.Wipe, "wipe")]
    public async Task Privileged_actions_use_v1_intune_mapping(string action, string graphAction)
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphManagedDeviceCommands(factory).ExecuteAsync("device-1", action, "key-1", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        factory.Scopes.Single().Should().Equal(GraphScopeCatalog.DevicePrivilegedOperationScopes);
        transport.Requests.Single().Method.Should().Be(HttpMethod.Post);
        transport.Requests.Single().PathAndQuery.Should().Be($"/v1.0/deviceManagement/managedDevices/device-1/{graphAction}");
        transport.Requests.Single().Headers!["Idempotency-Key"].Should().Be("key-1");
    }

    [Fact]
    public async Task Wipe_uses_the_fixed_safe_payload()
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphManagedDeviceCommands(factory).ExecuteAsync("device-1", DeviceActionNames.Wipe, "key-wipe", CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        transport.RequestBodies.Single().Should().Be("{\"keepEnrollmentData\":false,\"keepUserData\":false,\"persistEsimDataPlan\":false}");
    }

    [Theory]
    [InlineData("bad\u0001key")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public async Task Managed_device_commands_reject_malformed_idempotency_keys_before_graph_dispatch(string idempotencyKey)
    {
        var transport = new RecordingTransport("{}");
        var factory = new RecordingFactory(transport);

        var result = await new GraphManagedDeviceCommands(factory).ExecuteAsync("device-1", DeviceActionNames.Sync, idempotencyKey, CancellationToken.None);

        result.Category.Should().Be("invalid_request");
        transport.Requests.Should().BeEmpty();
        factory.Scopes.Should().BeEmpty();
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
            {"temporaryAccessPass":"fixture-tap-value","id":"tap-1","startDateTime":"2026-09-23T10:00:00Z","lifetimeInMinutes":60,"isUsableOnce":true,"secret":"must-not-be-exposed"}
            """);
        var factory = new RecordingFactory(transport);

        var result = await new GraphAuthenticationMethodCommands(factory)
            .CreateTemporaryAccessPassAsync("user-1", "tap-key", CancellationToken.None);

        result.TemporaryAccessPass.Should().Be("fixture-tap-value");
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
    [InlineData("{\"temporaryAccessPass\":\"fixture-tap-value\",\"lifetimeInMinutes\":60,\"isUsableOnce\":true}")]
    [InlineData("{\"temporaryAccessPass\":\"fixture-tap-value\",\"id\":\"tap-1\",\"lifetimeInMinutes\":30,\"isUsableOnce\":true}")]
    [InlineData("{\"temporaryAccessPass\":\"fixture-tap-value\",\"id\":\"tap-1\",\"lifetimeInMinutes\":60,\"isUsableOnce\":false}")]
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

    private sealed class ConsentRequiredFactory : IDelegatedGraphClientFactory
    {
        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken) =>
            throw new MsalUiRequiredException("consent_required", "Delegated consent is required.");
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

    private sealed class PagedTransport(IEnumerable<string> pages) : IGraphTransport
    {
        private readonly Queue<string> pages = new(pages);
        public IReadOnlyCollection<string> Scopes { get; } = [];
        public List<GraphRequest> Requests { get; } = [];
        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(new GraphTransportResponse(GraphOperationResult.Success(), pages.Dequeue(), 1, new Dictionary<string, IReadOnlyCollection<string>>()));
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
