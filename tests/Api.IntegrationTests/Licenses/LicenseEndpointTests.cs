using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
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
                new("sku-1", "ENTERPRISEPACK", "Microsoft 365 E3", 10, 5),
                new("sku-2", "VISIO", "Visio", 2, 0)])
        };
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/licenses?search=E3&pageSize=1&page=1");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Microsoft 365 E3");
        body.Should().NotContain("VISIO");
        reader.Query!.Search.Should().Be("E3");
        reader.Query.PageSize.Should().Be(1);
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

    private static WebApplicationFactory<Program> CreateFactory(RecordingLicenseOverviewReader reader, GraphAuthorizationSnapshot snapshot) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
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
            services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader());
            services.RemoveAll<IGraphAuthorizationSnapshotReader>();
            services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticAuthorizationReader(snapshot));
            services.RemoveAll<ILicenseOverviewReader>();
            services.AddSingleton<ILicenseOverviewReader>(reader);
        }));

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static GraphAuthorizationSnapshot AllowedSnapshot() => GraphAuthorizationSnapshot.Available("user-1", ["Directory.Read.All", "User.Read.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);
    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader { public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader { public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "Example", "member", ModuleKeys: ["users", "devices", "licenses"])); }
    private sealed class RecordingLicenseOverviewReader : ILicenseOverviewReader
    {
        public int Calls { get; private set; }
        public LicenseOverviewQuery? Query { get; private set; }
        public GraphReadResult<IReadOnlyList<LicenseOverviewItem>> Result { get; init; } = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([]);
        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(WorkspaceContext context, LicenseOverviewQuery query, CancellationToken cancellationToken) { Calls++; Query = query; return Task.FromResult(Result); }
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
