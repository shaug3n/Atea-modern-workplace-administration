using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Licenses;

public sealed class LicenseHygieneEndpointTests
{
    [Fact]
    public async Task Module_denial_does_not_call_either_graph_reader()
    {
        var inventory = new RecordingInventoryReader();
        var users = new RecordingUserReader(Completed([]));
        using var factory = CreateFactory(AllowedSnapshot(), ["licenses"], inventory, users);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/licenses/hygiene");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inventory.Calls.Should().Be(0);
        users.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Capability_denial_does_not_call_either_graph_reader()
    {
        var inventory = new RecordingInventoryReader();
        var users = new RecordingUserReader(Completed([]));
        using var factory = CreateFactory(
            GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"], []),
            ["license-hygiene"],
            inventory,
            users);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/licenses/hygiene");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.GetProperty("access").GetProperty("state").GetString().Should().Be(CapabilityState.Hidden);
        inventory.Calls.Should().Be(0);
        users.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("allowed")]
    [InlineData("read_only")]
    public async Task Authorized_capability_can_read_hygiene_snapshot(string state)
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded(
            [
                new("11111111-1111-1111-1111-111111111111", "ENTERPRISEPACK", "Office 365 E3", 2, 8, 10)
            ])
        };
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", "Disabled", "disabled@example.com", false, ["11111111-1111-1111-1111-111111111111"])
        ]));
        using var factory = CreateFactory(SnapshotFor(state), ["license-hygiene"], inventory, users);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/licenses/hygiene");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.GetProperty("access").GetProperty("state").GetString().Should().Be(state);
        body.RootElement.GetProperty("capacityItems").GetArrayLength().Should().Be(1);
        body.RootElement.GetProperty("disabledAccounts").GetArrayLength().Should().Be(1);
        inventory.Calls.Should().Be(1);
        users.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Graph_final_403_is_reported_without_suppressing_verified_user_evidence()
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(
                new GraphOperationResult(false, "not_authorized", 403))
        };
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", "Disabled", null, false, ["aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"])
        ]));
        using var factory = CreateFactory(AllowedSnapshot(), ["license-hygiene"], inventory, users);
        using var client = CreateClient(factory);

        var response = await client.GetAsync("/api/licenses/hygiene");
        var responseContent = await response.Content.ReadAsStringAsync();
        using var body = JsonDocument.Parse(responseContent);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.RootElement.GetProperty("inventory").GetProperty("error").GetProperty("category").GetString().Should().Be("not_authorized");
        body.RootElement.GetProperty("inventory").GetProperty("error").GetProperty("statusCode").GetInt32().Should().Be(403);
        body.RootElement.GetProperty("disabledAccounts").GetArrayLength().Should().Be(1);
        responseContent.Should().NotContain("raw Graph");
    }

    [Fact]
    public async Task Existing_license_read_and_assignment_gates_remain_unchanged()
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([])
        };
        var users = new RecordingUserReader(Completed([]));
        using var factory = CreateFactory(ReadOnlySnapshot(), ["license-hygiene", "licenses"], inventory, users);
        using var client = CreateClient(factory);

        var inventoryResponse = await client.GetAsync("/api/licenses");
        using var assign = new HttpRequestMessage(
            HttpMethod.Post,
            "/api/users/user-1/licenses/11111111-1111-1111-1111-111111111111")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        };
        assign.Headers.Add("Idempotency-Key", "hygiene-read-only-assignment");
        var assignmentResponse = await client.SendAsync(assign);

        inventoryResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        assignmentResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        inventory.Calls.Should().Be(1);
    }

    private static HttpClient CreateClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        GraphAuthorizationSnapshot snapshot,
        string[] modules,
        RecordingInventoryReader inventory,
        RecordingUserReader users) =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder =>
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                ["AzureAd:ClientId"] = "test-client-id"
            })).ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(modules));
                services.RemoveAll<IWorkspaceSettingsService>();
                services.AddSingleton<IWorkspaceSettingsService>(new FixedWorkspaceSettingsService(
                    new WorkspaceConfiguration(modules, [], new Dictionary<string, string>(), string.Empty, "light")));
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticAuthorizationReader(snapshot));
                services.RemoveAll<ILicenseOverviewReader>();
                services.AddSingleton<ILicenseOverviewReader>(inventory);
                services.RemoveAll<ILicenseHygieneUserReader>();
                services.AddSingleton<ILicenseHygieneUserReader>(users);
            }));

    private static GraphAuthorizationSnapshot SnapshotFor(string state) =>
        state == CapabilityState.Allowed ? AllowedSnapshot() : ReadOnlySnapshot();

    private static GraphAuthorizationSnapshot AllowedSnapshot() =>
        GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"],
        [
            new DirectoryRoleSnapshot(
                EntraRoleCatalog.GlobalReaderTemplateId,
                "Global Reader",
                DirectoryRoleAssignmentState.Active,
                "/")
        ]);

    private static GraphAuthorizationSnapshot ReadOnlySnapshot() =>
        GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"],
        [
            new DirectoryRoleSnapshot(
                EntraRoleCatalog.LicenseAdministratorTemplateId,
                "License Administrator",
                DirectoryRoleAssignmentState.Active,
                "/administrativeUnits/unit-1")
        ]);

    private static LicenseHygieneUserScanResult Completed(IReadOnlyList<LicenseHygieneUserObservation> users)
    {
        var fetchedAt = DateTimeOffset.UtcNow;
        return new LicenseHygieneUserScanResult(users, 1, true, "completed", null, fetchedAt, fetchedAt);
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class FixtureMembershipReader(string[] modules) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(
            Guid tenantId,
            Guid objectId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(
                WorkspaceId,
                "Example",
                "member",
                ModuleKeys: modules));
    }

    private sealed class FixedWorkspaceSettingsService(WorkspaceConfiguration configuration) : IWorkspaceSettingsService
    {
        public Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(configuration);

        public Task<WorkspaceSettingsResponse> UpdateAsync(
            WorkspaceContext context,
            WorkspaceSettingsRequest request,
            CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class RecordingInventoryReader : ILicenseOverviewReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<LicenseOverviewItem>> Result { get; init; } =
            GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(
            WorkspaceContext context,
            LicenseOverviewQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingUserReader(LicenseHygieneUserScanResult result) : ILicenseHygieneUserReader
    {
        public int Calls { get; private set; }

        public Task<LicenseHygieneUserScanResult> ScanAsync(WorkspaceContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class TestAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization"))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim("oid", ObjectId.ToString()),
                new Claim("tid", TenantId.ToString()),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }
    }
}
