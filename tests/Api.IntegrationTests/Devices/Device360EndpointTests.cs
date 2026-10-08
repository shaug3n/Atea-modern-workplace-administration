using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Devices;

public sealed class Device360EndpointTests
{
    [Fact]
    public async Task device360_reads_require_authentication_and_devices_module()
    {
        var service = new RecordingService();
        using var unauthenticatedFactory = CreateFactory(service, hasModule: true);
        using var unauthenticated = unauthenticatedFactory.CreateClient();
        var anonymousResponse = await unauthenticated.GetAsync("/api/devices/managed-device-123/apps");

        anonymousResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        service.Calls.Should().Be(0);

        using var moduleDeniedFactory = CreateFactory(service, hasModule: false);
        using var moduleDenied = AuthenticatedClient(moduleDeniedFactory);
        var deniedResponse = await moduleDenied.GetAsync("/api/devices/managed-device-123/apps");

        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        service.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData(Device360Status.Succeeded, 200)]
    [InlineData(Device360Status.Partial, 200)]
    [InlineData(Device360Status.Unsupported, 200)]
    [InlineData(Device360Status.NoReportedPolicies, 200)]
    [InlineData(Device360Status.InvalidTarget, 400)]
    [InlineData(Device360Status.DeviceNotFound, 404)]
    [InlineData(Device360Status.CapabilityRequired, 403)]
    [InlineData(Device360Status.MissingScope, 403)]
    [InlineData(Device360Status.ConsentRequired, 403)]
    [InlineData(Device360Status.GraphForbidden, 403)]
    [InlineData(Device360Status.Throttled, 429)]
    [InlineData(Device360Status.Failed, 502)]
    [InlineData(Device360Status.TemporarilyUnavailable, 503)]
    public async Task device360_routes_pass_the_managed_device_id_and_map_status(string status, int expectedStatus)
    {
        var service = new RecordingService { ResponseStatus = status };
        using var factory = CreateFactory(service, hasModule: true);
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/devices/managed-device-123/apps");

        ((int)response.StatusCode).Should().Be(expectedStatus);
        service.Calls.Should().Be(1);
        service.LastManagedDeviceId.Should().Be("managed-device-123");
        response.Headers.CacheControl!.NoStore.Should().BeTrue();
    }

    [Fact]
    public async Task all_device360_routes_forward_the_managed_device_id()
    {
        var service = new RecordingService();
        using var factory = CreateFactory(service, hasModule: true);
        using var client = AuthenticatedClient(factory);
        var routes = new Dictionary<string, string>
        {
            ["/api/devices/managed-device-123/compliance-policies"] = "compliance",
            ["/api/devices/managed-device-123/configuration/reported"] = "reported",
            ["/api/devices/managed-device-123/configuration/assignments"] = "assignments",
            ["/api/devices/managed-device-123/apps"] = "apps",
            ["/api/devices/managed-device-123/protection"] = "protection"
        };

        foreach (var (route, method) in routes)
        {
            var response = await client.GetAsync(route);
            response.StatusCode.Should().Be(HttpStatusCode.OK);
            response.Headers.CacheControl!.NoStore.Should().BeTrue();
            service.LastManagedDeviceId.Should().Be("managed-device-123");
            service.LastMethod.Should().Be(method);
        }

        service.Calls.Should().Be(routes.Count);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingService service, bool hasModule) =>
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
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(hasModule));
                services.RemoveAll<IDevice360Service>();
                services.AddSingleton<IDevice360Service>(service);
            }));

    private sealed class RecordingService : IDevice360Service
    {
        public int Calls { get; private set; }
        public string? LastManagedDeviceId { get; private set; }
        public string ResponseStatus { get; set; } = Device360Status.Succeeded;

        public Task<Device360SectionResponse<IReadOnlyList<DeviceCompliancePolicyState>>> GetCompliancePolicyStatesAsync(WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<DeviceCompliancePolicyState>>(managedDeviceId, [], "compliance");

        public Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationState>>> GetDeviceConfigurationStatesAsync(WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<DeviceConfigurationState>>(managedDeviceId, [], "reported");

        public Task<Device360SectionResponse<IReadOnlyList<DeviceConfigurationAssignmentTarget>>> GetConfigurationAssignmentsAsync(WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<DeviceConfigurationAssignmentTarget>>(managedDeviceId, [], "assignments");

        public Task<Device360SectionResponse<IReadOnlyList<DeviceDetectedApp>>> GetDetectedAppsAsync(WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken) =>
            Respond<IReadOnlyList<DeviceDetectedApp>>(managedDeviceId, [], "apps");

        public Task<Device360SectionResponse<DeviceWindowsProtectionState>> GetWindowsProtectionStateAsync(WorkspaceContext context, string managedDeviceId, CancellationToken cancellationToken) =>
            Respond<DeviceWindowsProtectionState>(managedDeviceId, new DeviceWindowsProtectionState(), "protection");

        private Task<Device360SectionResponse<T>> Respond<T>(string managedDeviceId, T data, string method)
        {
            Calls++;
            LastManagedDeviceId = managedDeviceId;
            LastMethod = method;
            return Task.FromResult(new Device360SectionResponse<T>(ResponseStatus, data, DateTimeOffset.UtcNow));
        }

        public string? LastMethod { get; private set; }
    }

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class FixtureMembershipReader(bool hasModule) : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(
                WorkspaceId, "Example", "member", ModuleKeys: hasModule ? ["devices"] : []));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.ContainsKey("Authorization")) return Task.FromResult(AuthenticateResult.NoResult());
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
