using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Users;

public sealed class UserEndpointsTests
{
    [Fact]
    public async Task Users_endpoint_returns_live_page_with_safe_continuation_and_selected_fields()
    {
        var reader = new RecordingDirectoryReader
        {
            Result = new PagedResult<UserSummary>(
                [new UserSummary("user-1", "Ada Lovelace", "ada@example.com", "ada@example.com", true, "Member")],
                ["corr-1"],
                "/v1.0/users?$skiptoken=raw-next")
        };
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users?search=ada&pageSize=50&accountStatus=enabled&userType=Member");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain("\"items\"");
        body.Should().Contain("\"continuationToken\"");
        body.Should().Contain("\"fetchedAt\"");
        body.Should().Contain("\"freshness\":\"fresh\"");
        body.Should().Contain("\"partialData\":false");
        body.Should().NotContain("raw-next");
        body.Should().NotContain("@odata.nextLink");
        body.Should().NotContain("Authorization");
        reader.Query.Should().NotBeNull();
        reader.Query!.Search.Should().Be("ada");
        reader.Query.PageSize.Should().Be(50);
        reader.Query.AccountStatus.Should().Be("enabled");
        reader.Query.UserType.Should().Be("Member");
        reader.Context.Should().NotBeNull();
        reader.Context!.Membership.WorkspaceId.Should().Be(WorkspaceId);
    }

    [Theory]
    [InlineData("not_found", 404, "unavailable")]
    [InlineData("throttled", 429, "stale")]
    public async Task Users_endpoint_translates_graph_failures_without_raw_payload(string category, int statusCode, string freshness)
    {
        var reader = new RecordingDirectoryReader
        {
            Result = new PagedResult<UserSummary>(
                [],
                ["corr-1"],
                null,
                new GraphOperationResult(false, category, statusCode, TimeSpan.FromSeconds(20), "corr-1", "req-1"))
        };
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        body.Should().Contain($"\"freshness\":\"{freshness}\"");
        body.Should().Contain("\"partialData\":true");
        body.Should().Contain($"\"category\":\"{category}\"");
        body.Should().NotContain("raw graph");
        body.Should().NotContain("request-id");
    }

    [Fact]
    public async Task Users_endpoint_returns_capability_aware_partial_state_without_calling_graph()
    {
        var reader = new RecordingDirectoryReader();
        using var factory = CreateFactory(reader, GraphAuthorizationSnapshot.Available("user-1", [], []));
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        reader.Calls.Should().Be(0);
        body.Should().Contain("\"partialData\":true");
        body.Should().Contain("\"category\":\"capability_required\"");
        body.Should().Contain("\"state\":\"hidden\"");
    }

    [Fact]
    public async Task Users_endpoint_rejects_non_numeric_page_size_without_calling_graph()
    {
        var reader = new RecordingDirectoryReader();
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users?pageSize=not-a-number");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("pageSize");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Users_endpoint_rejects_an_explicitly_empty_page_size_without_calling_graph()
    {
        var reader = new RecordingDirectoryReader();
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync("/api/users?pageSize=");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("pageSize");
        reader.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("license")]
    [InlineData("tenantRole")]
    public async Task Users_endpoint_forwards_supported_directory_filters(string field)
    {
        var reader = new RecordingDirectoryReader();
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync($"/api/users?{field}=unsupported");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        reader.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData("accountStatus=unknown")]
    [InlineData("userType=Member%27%20or%201%20eq%201")]
    public async Task Users_endpoint_rejects_unsupported_or_unsafe_filters(string query)
    {
        var reader = new RecordingDirectoryReader();
        using var factory = CreateFactory(reader);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");

        var response = await client.GetAsync($"/api/users?{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        reader.Calls.Should().Be(0);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingDirectoryReader reader,
        GraphAuthorizationSnapshot? snapshot = null) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["AzureAd:Audience"] = "api://atea-unified-workplace-api",
                    ["AzureAd:ClientId"] = "test-client-id",
                    ["Users:ContinuationSigningKey"] = "0123456789abcdef0123456789abcdef"
                });
            });
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options =>
                {
                    options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme;
                    options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme;
                }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader());
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton<IUserDirectoryReader>(reader);
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticCapabilityReader(snapshot ?? GraphAuthorizationSnapshot.Available(
                    "user-1",
                    ["Directory.Read.All"],
                    [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")])));
            });
        });

    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public int Calls { get; private set; }
        public WorkspaceContext? Context { get; private set; }
        public UserSearchQuery? Query { get; private set; }
        public PagedResult<UserSummary> Result { get; init; } = new([], [], null);

        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            Context = context;
            Query = query;
            return Task.FromResult(Result);
        }

        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) => Task.FromResult<UserDetails?>(null);
    }

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(WorkspaceId, "Contoso Workplace", "member"));
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
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
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("aud", "api://atea-unified-workplace-api")
            };
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme)), Scheme)));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.ContentType = "application/json";
            return Response.WriteAsync("{\"error\":\"authentication_required\"}");
        }
    }
}
