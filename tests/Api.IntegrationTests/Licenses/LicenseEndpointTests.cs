using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Licenses;

public sealed class LicenseEndpointTests
{
    [Fact]
    public async Task Returns_safe_filtered_paginated_license_overview()
    {
        var reader = new RecordingLicenseOverviewReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([
                new("sku-1", "ENTERPRISEPACK", "Office 365 E3", 10, 5),
                new("sku-2", "VISIO", "Visio", 2, 0)])
        };
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/licenses?search=E3&pageSize=1&page=1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Office 365 E3");
        body.Should().NotContain("VISIO");
        reader.Query!.Search.Should().Be("E3");
        reader.Query.PageSize.Should().Be(1);
    }

    [Theory]
    [InlineData("Office 365 E3")]
    [InlineData("ENTERPRISEPACK")]
    public async Task Search_finds_license_by_friendly_name_or_part_number_without_changing_contract(string search)
    {
        var reader = new RecordingLicenseOverviewReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([
                new("sku-1", "ENTERPRISEPACK", "Office 365 E3", 7, 3, 10),
                new("sku-2", "LONG_UNKNOWN_PRODUCT_CODE_2026", "LONG_UNKNOWN_PRODUCT_CODE_2026", 1, 2, 3)])
        };
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync($"/api/licenses?search={Uri.EscapeDataString(search)}");
        using var body = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        root.GetProperty("total").GetInt32().Should().Be(1);
        var item = root.GetProperty("items")[0];
        item.GetProperty("skuId").GetString().Should().Be("sku-1");
        item.GetProperty("partNumber").GetString().Should().Be("ENTERPRISEPACK");
        item.GetProperty("displayName").GetString().Should().Be("Office 365 E3");
        item.GetProperty("purchased").GetInt32().Should().Be(10);
        item.GetProperty("assigned").GetInt32().Should().Be(7);
        item.GetProperty("available").GetInt32().Should().Be(3);
    }

    [Fact]
    public async Task Rejects_unsafe_license_filter_without_calling_graph()
    {
        var reader = new RecordingLicenseOverviewReader();
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/licenses?filter=skuId%20eq%20%27x%27");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Returns_permission_state_without_reading_graph_when_license_capability_is_hidden()
    {
        var reader = new RecordingLicenseOverviewReader();
        using var factory = CreateFactory(reader, GraphAuthorizationSnapshot.Unavailable("consent_required", true));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/licenses");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("consent_required");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Assignee_route_is_available_to_license_reader_and_returns_paged_roster()
    {
        using var factory = CreateFactory(new RecordingLicenseOverviewReader(), AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/licenses/11111111-1111-1111-1111-111111111111/assignees?pageSize=1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("user-1");
        body.Should().Contain("next-page");
    }

    [Fact]
    public async Task Users_export_has_csv_and_explicit_limit_headers()
    {
        using var factory = CreateFactory(new RecordingLicenseOverviewReader(), AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users/export.csv?search=Ada");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Headers.GetValues("X-Export-Row-Count").Should().ContainSingle().Which.Should().Be("1");
        response.Headers.GetValues("X-Export-Truncated").Should().ContainSingle().Which.Should().Be("false");
        body.Should().Contain("Ada Lovelace");
    }

    [Fact]
    public async Task License_module_grant_allows_roster_but_does_not_open_users_export()
    {
        using var factory = CreateFactory(new RecordingLicenseOverviewReader(), AllowedSnapshot(), ["licenses"]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        (await client.GetAsync("/api/licenses/11111111-1111-1111-1111-111111111111/assignees")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/api/users/export.csv")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync("/api/devices/export.csv")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Read_only_license_viewer_can_read_inventory_but_cannot_assign_a_license()
    {
        var reader = new RecordingLicenseOverviewReader { Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([new("sku-1", "E3", "E3", 7, 3, 10)]) };
        using var factory = CreateFactory(reader, AllowedSnapshot(), ["licenses"]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        (await client.GetAsync("/api/licenses")).StatusCode.Should().Be(HttpStatusCode.OK);
        var assign = new HttpRequestMessage(HttpMethod.Post, "/api/users/user-1/licenses/11111111-1111-1111-1111-111111111111");
        assign.Headers.Add("Idempotency-Key", "read-only-attempt");
        assign.Content = new StringContent("{}", System.Text.Encoding.UTF8, "application/json");
        (await client.SendAsync(assign)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task License_and_assignee_exports_are_separate_authorized_csv_routes()
    {
        var reader = new RecordingLicenseOverviewReader { Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([new("sku-1", "E3", "E3", 7, 3, 10)]) };
        using var factory = CreateFactory(reader, AllowedSnapshot(), ["licenses"]);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var inventory = await client.GetAsync("/api/licenses/export.csv?search=E3");
        var assignees = await client.GetAsync("/api/licenses/11111111-1111-1111-1111-111111111111/assignees/export.csv");

        inventory.StatusCode.Should().Be(HttpStatusCode.OK);
        (await inventory.Content.ReadAsStringAsync()).Should().Contain("\"10\",\"7\",\"3\"");
        assignees.StatusCode.Should().Be(HttpStatusCode.OK);
        (await assignees.Content.ReadAsStringAsync()).Should().Contain("Ada Lovelace");
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingLicenseOverviewReader reader, GraphAuthorizationSnapshot snapshot, string[]? modules = null) =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
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
            services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(modules ?? ["users", "devices", "licenses"]));
            services.RemoveAll<IGraphAuthorizationSnapshotReader>();
            services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticAuthorizationReader(snapshot));
            services.RemoveAll<ILicenseOverviewReader>();
            services.AddSingleton<ILicenseOverviewReader>(reader);
            services.RemoveAll<ILicenseAssigneeService>();
            services.AddSingleton<ILicenseAssigneeService>(new FakeAssignees());
            services.RemoveAll<IUserQueryService>();
            services.AddSingleton<IUserQueryService>(new FakeUsers());
            services.RemoveAll<IAuditWriter>();
            services.AddSingleton<IAuditWriter>(new NoOpAuditWriter());
        }));

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static GraphAuthorizationSnapshot AllowedSnapshot() => GraphAuthorizationSnapshot.Available("user-1", ["Directory.Read.All", "User.Read.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);
    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader { public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class FixtureMembershipReader(string[] modules) : IWorkspaceMembershipReader { public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "Example", "member", ModuleKeys: modules)); }
    private sealed class RecordingLicenseOverviewReader : ILicenseOverviewReader
    {
        public int Calls { get; private set; }
        public LicenseOverviewQuery? Query { get; private set; }
        public GraphReadResult<IReadOnlyList<LicenseOverviewItem>> Result { get; init; } = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([]);
        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(WorkspaceContext context, LicenseOverviewQuery query, CancellationToken cancellationToken) { Calls++; Query = query; return Task.FromResult(Result); }
    }
    private sealed class FakeAssignees : ILicenseAssigneeService
    {
        public Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, string skuId, int pageSize, string? continuationToken, CancellationToken cancellationToken) =>
            Task.FromResult(new UserDirectoryResponse([new UserSummary("user-1", "Ada Lovelace", "ada@example.com", null)], continuationToken is null ? "next-page" : null, DateTimeOffset.UtcNow, "fresh", false));
    }
    private sealed class FakeUsers : IUserQueryService
    {
        public Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new UserDirectoryResponse([new UserSummary("user-1", "Ada Lovelace", "ada@example.com", null)], null, DateTimeOffset.UtcNow, "fresh", false));
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
