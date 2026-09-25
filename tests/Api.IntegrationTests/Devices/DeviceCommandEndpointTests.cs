using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Devices;

public sealed class DeviceCommandEndpointTests
{
    [Fact]
    public async Task Laps_reveal_is_post_only_no_store_and_returns_no_secret_for_invalid_reason()
    {
        var recovery = new RecordingRecoveryService();
        using var factory = CreateFactory(null, AllowedSnapshot, recoveryService: recovery);
        using var client = AuthenticatedClient(factory);

        var get = await client.GetAsync("/api/devices/device-1/recovery/laps/reveal");
        get.StatusCode.Should().NotBe(HttpStatusCode.OK);
        (await get.Content.ReadAsStringAsync()).Should().NotContain("fixture-password");

        var bad = await client.PostAsJsonAsync("/api/devices/device-1/recovery/laps/reveal", new { reason = " " });
        bad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        bad.Headers.CacheControl!.NoStore.Should().BeTrue();
        recovery.Calls.Should().Be(0);

        var good = await client.PostAsJsonAsync("/api/devices/device-1/recovery/laps/reveal", new { reason = "Incident 123" });
        good.StatusCode.Should().Be(HttpStatusCode.OK);
        good.Headers.CacheControl!.NoStore.Should().BeTrue();
        (await good.Content.ReadAsStringAsync()).Should().Contain("fixture-password");
    }

    [Fact]
    public async Task Read_write_only_access_is_denied_for_privileged_device_actions()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, ReadWriteOnlySnapshot);
        using var client = AuthenticatedClient(factory);

        var response = await PostAsync(client, DeviceActionNames.Sync, "read-write-key");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain(Capability.DevicesPrivilegedManage);
        commands.Calls.Should().Be(0);
    }

    [Fact]
    public void Production_composition_resolves_the_graph_managed_device_command_adapter()
    {
        using var factory = CreateFactory(null, AllowedSnapshot);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IManagedDeviceCommands>().Should().BeOfType<GraphManagedDeviceCommands>();
    }

    [Fact]
    public async Task Duplicate_action_request_is_replayed_without_a_second_graph_call()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot);
        using var client = AuthenticatedClient(factory);

        var first = await PostAsync(client, DeviceActionNames.Wipe, "same-key");
        var replay = await PostAsync(client, DeviceActionNames.Wipe, "same-key");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        (await replay.Content.ReadAsStringAsync()).Should().Contain("\"replayed\":true");
        commands.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Invalid_action_is_rejected_at_the_command_boundary()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot);
        using var client = AuthenticatedClient(factory);

        var response = await PostAsync(client, "delete", "invalid-key");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("invalid_action");
        commands.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Request_without_workspace_membership_is_denied_before_command_dispatch()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot, hasMembership: false);
        using var client = AuthenticatedClient(factory);

        var response = await PostAsync(client, DeviceActionNames.Restart, "workspace-key");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"denied\"");
        body.Should().Contain($"\"requiredCapability\":\"{Capability.DevicesPrivilegedManage}\"");
        body.Should().Contain("workspace_membership_required");
        commands.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_target_returns_typed_privileged_command_result()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot);
        using var client = AuthenticatedClient(factory);

        var response = await PostAsync(client, DeviceActionNames.Sync, "invalid-target-key", deviceId: "%20");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("\"status\":\"invalid_target\"");
        body.Should().Contain($"\"requiredCapability\":\"{Capability.DevicesPrivilegedManage}\"");
    }

    [Fact]
    public async Task Missing_idempotency_key_returns_typed_privileged_command_result()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot);
        using var client = AuthenticatedClient(factory);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/devices/device-1/actions/sync");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("\"error\":\"idempotency_key_required\"");
        body.Should().Contain($"\"requiredCapability\":\"{Capability.DevicesPrivilegedManage}\"");
    }

    [Fact]
    public async Task Oversized_idempotency_key_returns_typed_privileged_command_result()
    {
        var commands = new RecordingCommands();
        using var factory = CreateFactory(commands, AllowedSnapshot);
        using var client = AuthenticatedClient(factory);

        var response = await PostAsync(client, DeviceActionNames.Sync, new string('a', 257));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("\"error\":\"invalid_idempotency_key\"");
        body.Should().Contain($"\"requiredCapability\":\"{Capability.DevicesPrivilegedManage}\"");
        commands.Calls.Should().Be(0);
    }

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string action, string key, string deviceId = "device-1")
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/devices/{deviceId}/actions/{action}");
        request.Headers.Add("Idempotency-Key", key);
        return await client.SendAsync(request);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingCommands? commands, GraphAuthorizationSnapshot snapshot, bool hasMembership = true, IDeviceRecoveryService? recoveryService = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id"
            }))
            .ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(hasMembership));
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticAuthorizationReader(snapshot));
                if (commands is not null)
                {
                    services.RemoveAll<IManagedDeviceCommands>();
                    services.AddSingleton<IManagedDeviceCommands>(commands);
                }
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService, MemoryIdempotencyService>();
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter, NoOpAuditWriter>();
                if (recoveryService is not null)
                {
                    services.RemoveAll<IDeviceRecoveryService>();
                    services.AddSingleton(recoveryService);
                }
            }));

    private sealed class RecordingRecoveryService : IDeviceRecoveryService
    {
        public int Calls { get; private set; }
        public Task<RecoveryResult<IReadOnlyList<BitlockerRecoveryMetadata>>> GetBitlockerMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RecoveryResult<LapsMetadata>> GetLapsMetadataAsync(WorkspaceContext context, string deviceId, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RecoveryResult<BitlockerSecret>> RevealBitlockerAsync(WorkspaceContext context, string deviceId, string keyId, string reason, CancellationToken cancellationToken) => throw new NotImplementedException();
        public Task<RecoveryResult<LapsSecret>> RevealLapsAsync(WorkspaceContext context, string deviceId, string reason, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new RecoveryResult<LapsSecret>("succeeded", new LapsSecret("Admin", "fixture-password", null)));
        }
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static GraphAuthorizationSnapshot AllowedSnapshot => GraphAuthorizationSnapshot.Available(
        "actor-1", ["DeviceManagementManagedDevices.Read.All", "DeviceManagementManagedDevices.PrivilegedOperations.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.IntuneAdministratorTemplateId, "Intune Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private static GraphAuthorizationSnapshot ReadWriteOnlySnapshot => GraphAuthorizationSnapshot.Available(
        "actor-1", ["DeviceManagementManagedDevices.Read.All", "DeviceManagementManagedDevices.ReadWrite.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.IntuneAdministratorTemplateId, "Intune Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixtureMembershipReader(bool hasMembership) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(hasMembership ? new WorkspaceMembership(WorkspaceId, "Example", "member", ModuleKeys: ["users", "devices", "licenses"]) : null);
    }

    private sealed class RecordingCommands : IManagedDeviceCommands
    {
        public int Calls { get; private set; }
        public Task<GraphOperationResult> ExecuteAsync(string deviceObjectId, string action, string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
        }
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
            var claims = new[] { new Claim("oid", ObjectId.ToString()), new Claim("tid", TenantId.ToString()), new Claim("aud", "api://atea-unified-workplace-api") };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
