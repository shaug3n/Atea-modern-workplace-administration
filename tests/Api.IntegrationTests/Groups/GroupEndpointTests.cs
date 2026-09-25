using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Groups;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Groups;

public sealed class GroupEndpointTests
{
    [Fact]
    public async Task Returns_catalog_and_normalizes_safe_search()
    {
        var reader = new RecordingGroupCatalogReader { Items = [new GroupCatalogItem("group-1", "Engineering", "engineering", true, [])] };
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/groups?search=Eng%27ineering&pageSize=10");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("Engineering");
        reader.Search.Should().Be("Eng'ineering");
        reader.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Returns_capability_state_without_reading_catalog_when_capability_is_not_available()
    {
        var reader = new RecordingGroupCatalogReader();
        using var factory = CreateFactory(reader, GraphAuthorizationSnapshot.Unavailable("consent_required", true));
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/groups");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("consent_required");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Rejects_invalid_catalog_query_without_reading_graph()
    {
        var reader = new RecordingGroupCatalogReader();
        using var factory = CreateFactory(reader, AllowedSnapshot());
        using var client = AuthenticatedClient(factory);

        var response = await client.GetAsync("/api/groups?pageSize=101");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reader.Calls.Should().Be(0);
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static WebApplicationFactory<Program> CreateFactory(RecordingGroupCatalogReader reader, GraphAuthorizationSnapshot snapshot) =>
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
            services.RemoveAll<IGroupCatalogReader>();
            services.AddSingleton<IGroupCatalogReader>(reader);
        }));

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private static GraphAuthorizationSnapshot AllowedSnapshot() => GraphAuthorizationSnapshot.Available("user-1", ["Directory.Read.All", "Group.Read.All", "GroupMember.ReadWrite.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GroupsAdministratorTemplateId, "Groups Administrator", DirectoryRoleAssignmentState.Active, "/")]);
    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader { public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader { public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "Example", "member", ModuleKeys: ["users", "devices", "licenses"])); }
    private sealed class RecordingGroupCatalogReader : IGroupCatalogReader
    {
        public int Calls { get; private set; }
        public string? Search { get; private set; }
        public int PageSize { get; private set; }
        public IReadOnlyList<GroupCatalogItem> Items { get; init; } = [];
        public Task<GraphReadResult<IReadOnlyList<GroupCatalogItem>>> ReadGroupsAsync(string? search, int pageSize, CancellationToken cancellationToken) { Calls++; Search = search; PageSize = pageSize; return Task.FromResult(GraphReadResult<IReadOnlyList<GroupCatalogItem>>.Succeeded(Items)); }
        public Task<GraphReadResult<GroupCatalogItem?>> ReadGroupAsync(string groupObjectId, CancellationToken cancellationToken) => Task.FromResult(GraphReadResult<GroupCatalogItem?>.Succeeded(Items.SingleOrDefault(item => item.Id == groupObjectId)));
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
