using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Groups;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Users;

public sealed class UserMutationEndpointTests
{
    [Fact]
    public async Task Global_reader_receives_structured_read_only_without_graph_mutation()
    {
        var commands = new RecordingUserCommands();
        using var factory = CreateFactory(commands, snapshot: ReaderSnapshot);
        using var client = AuthenticatedClient(factory);

        var request = JsonPatch("/api/users/user-1", """{"displayName":"Ada Updated"}""", "edit-key");
        var response = await client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        body.Should().Contain("\"error\":\"capability_required\"");
        body.Should().Contain("\"state\":\"read_only\"");
        body.Should().Contain("\"capability\":\"users.update\"");
        commands.UpdateCalls.Should().Be(0);
    }

    [Fact]
    public async Task User_administrator_can_create_user_and_receives_temporary_credential_once()
    {
        var commands = new RecordingUserCommands();
        using var factory = CreateFactory(commands);
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync("/api/users", Json("""{"displayName":"Ada Lovelace","givenName":"Ada","surname":"Lovelace","userPrincipalName":"ada@example.com","mailNickname":"ada","jobTitle":"Principal Engineer","department":"Digital Workplace","officeLocation":"Oslo","mobilePhone":"+47 22 00 00 00","usageLocation":"NO","accountEnabled":true}""", "create-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        body.Should().Contain("\"status\":\"succeeded\"");
        body.Should().Contain("\"temporaryPassword\"");
        body.Should().Contain("\"forceChangePasswordNextSignIn\":true");
        commands.CreateCalls.Should().Be(1);
        body.Should().NotContain("access_token");
        body.Should().NotContain("raw graph");
    }

    [Fact]
    public async Task Duplicate_browser_submission_performs_one_graph_mutation_and_returns_success_for_replay()
    {
        var commands = new RecordingUserCommands();
        using var factory = CreateFactory(commands);
        using var client = AuthenticatedClient(factory);

        var first = await client.SendAsync(JsonPatch("/api/users/user-1", """{"displayName":"Ada Updated"}""", "same-edit-key"));
        var replay = await client.SendAsync(JsonPatch("/api/users/user-1", """{"displayName":"Ada Updated"}""", "same-edit-key"));
        var changed = await client.SendAsync(JsonPatch("/api/users/user-1", """{"displayName":"Grace Hopper"}""", "same-edit-key"));
        var replayBody = await replay.Content.ReadAsStringAsync();
        var changedBody = await changed.Content.ReadAsStringAsync();

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        replay.StatusCode.Should().Be(HttpStatusCode.OK);
        replayBody.Should().Contain("\"replayed\":true");
        changed.StatusCode.Should().Be(HttpStatusCode.Conflict);
        changedBody.Should().Contain("idempotency_key_reused");
        commands.UpdateCalls.Should().Be(1);
    }

    [Fact]
    public async Task Mutation_requires_idempotency_key()
    {
        using var factory = CreateFactory(new RecordingUserCommands());
        using var client = AuthenticatedClient(factory);

        var response = await client.PatchAsync("/api/users/user-1", new StringContent("""{"displayName":"Ada"}""", Encoding.UTF8, "application/json"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("idempotency_key_required");
    }

    [Fact]
    public async Task License_route_rejects_body_sku_mismatch_without_graph_mutation()
    {
        var licenses = new RecordingLicenseCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), licenseCommands: licenses);
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync(
            "/api/users/user-1/licenses/route-sku",
            Json("""{"skuId":"body-sku","disabledPlans":[]}""", "license-mismatch-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("target_mismatch");
        licenses.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task License_route_without_body_sends_route_sku_to_graph()
    {
        var licenses = new RecordingLicenseCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), licenseCommands: licenses);
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/user-1/licenses/route-sku");
        request.Headers.Add("Idempotency-Key", "license-route-key");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        licenses.Assignments.Should().ContainSingle().Which.Should().Be(("user-1", "route-sku", true));
    }

    [Fact]
    public async Task Group_route_rejects_body_target_mismatch_without_graph_mutation()
    {
        var groups = new RecordingGroupCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), groupCommands: groups);
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync(
            "/api/users/user-1/groups/route-group",
            Json("""{"groupObjectId":"body-group"}""", "group-mismatch-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        body.Should().Contain("target_mismatch");
        groups.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task Group_route_rejects_id_not_in_authoritative_catalog_without_graph_mutation()
    {
        var groups = new RecordingGroupCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), groupCommands: groups, groupCatalog: new RecordingGroupCatalogReader { Items = [new GroupCatalogItem("known-group", "Known", null, true, [])] });
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync("/api/users/user-1/groups/unknown-group", Json("{\"groupObjectId\":\"unknown-group\"}", "unknown-group-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("group_not_found");
        groups.Added.Should().BeEmpty();
    }

    [Fact]
    public async Task License_route_rejects_id_not_in_authoritative_catalog_without_graph_mutation()
    {
        var licenses = new RecordingLicenseCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), licenseCommands: licenses, licenseCatalog: new RecordingLicenseCatalogReader { Items = [new LicenseOverviewItem("known-sku", "E3", "Microsoft 365 E3", 1, 1)] });
        using var client = AuthenticatedClient(factory);

        var response = await client.PostAsync("/api/users/user-1/licenses/unknown-sku", Json("{\"skuId\":\"unknown-sku\",\"disabledPlans\":[]}", "unknown-license-key"));
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        body.Should().Contain("license_not_found");
        licenses.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Catalog_graph_validation_errors_are_returned_as_actionable_bad_requests()
    {
        var groups = new RecordingGroupCommands();
        var licenses = new RecordingLicenseCommands();
        using var factory = CreateFactory(
            new RecordingUserCommands(),
            groupCommands: groups,
            licenseCommands: licenses,
            groupCatalog: new RecordingGroupCatalogReader { Error = new GraphOperationResult(false, "invalid_request", 400) },
            licenseCatalog: new RecordingLicenseCatalogReader { Error = new GraphOperationResult(false, "invalid_license", 400) });
        using var client = AuthenticatedClient(factory);

        var groupResponse = await client.PostAsync("/api/users/user-1/groups/route-group", Json("{\"groupObjectId\":\"route-group\"}", "invalid-group-key"));
        var licenseResponse = await client.PostAsync("/api/users/user-1/licenses/route-sku", Json("{\"skuId\":\"route-sku\",\"disabledPlans\":[]}", "invalid-license-key"));

        groupResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        licenseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await groupResponse.Content.ReadAsStringAsync()).Should().Contain("invalid_request");
        (await licenseResponse.Content.ReadAsStringAsync()).Should().Contain("invalid_license");
        groups.Added.Should().BeEmpty();
        licenses.Assignments.Should().BeEmpty();
    }

    [Fact]
    public async Task Malformed_secondary_target_is_rejected_without_graph_mutation()
    {
        var groups = new RecordingGroupCommands();
        var licenses = new RecordingLicenseCommands();
        using var factory = CreateFactory(new RecordingUserCommands(), groupCommands: groups, licenseCommands: licenses);
        using var client = AuthenticatedClient(factory);

        var groupResponse = await client.PostAsync(
            "/api/users/user-1/groups/%20",
            Json("""{"groupObjectId":" "}""", "bad-group-key"));
        var licenseResponse = await client.PostAsync(
            "/api/users/user-1/licenses/%20",
            Json("""{"skuId":" ","disabledPlans":[]}""", "bad-license-key"));

        groupResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        licenseResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        groups.Added.Should().BeEmpty();
        licenses.Assignments.Should().BeEmpty();
    }

    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test");
        return client;
    }

    private static StringContent Json(string payload, string key)
    {
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        content.Headers.Add("Idempotency-Key", key);
        return content;
    }

    private static HttpRequestMessage JsonPatch(string path, string payload, string key)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, path)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", key);
        return request;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingUserCommands commands,
        GraphAuthorizationSnapshot? snapshot = null,
        RecordingGroupCommands? groupCommands = null,
        RecordingLicenseCommands? licenseCommands = null,
        RecordingGroupCatalogReader? groupCatalog = null,
        RecordingLicenseCatalogReader? licenseCatalog = null) =>
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
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticCapabilityReader(snapshot ?? AdminSnapshot));
                services.RemoveAll<IUserDirectoryReader>();
                services.AddSingleton<IUserDirectoryReader>(new RecordingDirectoryReader());
                services.RemoveAll<IUserLifecycleCommands>();
                services.AddSingleton<IUserLifecycleCommands>(commands);
                services.RemoveAll<IGroupMembershipCommands>();
                services.AddSingleton<IGroupMembershipCommands>(groupCommands ?? new RecordingGroupCommands());
                services.RemoveAll<IGroupCatalogReader>();
                services.AddSingleton<IGroupCatalogReader>(groupCatalog ?? new RecordingGroupCatalogReader());
                services.RemoveAll<ILicenseAssignmentCommands>();
                services.AddSingleton<ILicenseAssignmentCommands>(licenseCommands ?? new RecordingLicenseCommands());
                services.RemoveAll<ILicenseOverviewReader>();
                services.AddSingleton<ILicenseOverviewReader>(licenseCatalog ?? new RecordingLicenseCatalogReader());
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService>(new MemoryIdempotencyService());
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter, NoOpAuditWriter>();
            });
        });

    private static readonly GraphAuthorizationSnapshot AdminSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All", "User.Create", "User.ReadWrite.All", "User.EnableDisableAccount.All", "Group.Read.All", "GroupMember.ReadWrite.All", "LicenseAssignment.ReadWrite.All"],
        [
            new DirectoryRoleSnapshot(EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.GroupsAdministratorTemplateId, "Groups Administrator", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.LicenseAdministratorTemplateId, "License Administrator", DirectoryRoleAssignmentState.Active, "/")
        ]);

    private static readonly GraphAuthorizationSnapshot ReaderSnapshot = GraphAuthorizationSnapshot.Available(
        "actor-1",
        ["Directory.Read.All", "User.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Contoso Workplace", "member"));
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult<UserDetails?>(new UserDetails(userObjectId, "Ada Lovelace", "ada@example.com", "ada@example.com", true, "Member", IsReadOnly: false, SourceOfAuthority: "cloud"));
    }

    private sealed class RecordingUserCommands : IUserLifecycleCommands
    {
        public int CreateCalls { get; private set; }
        public int UpdateCalls { get; private set; }

        public Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
        }

        public Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken)
        {
            UpdateCalls++;
            return Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
        }

        public Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success("corr-1", "req-1"));
    }

    private sealed class RecordingGroupCommands : IGroupMembershipCommands
    {
        public List<(string GroupId, string UserId)> Added { get; } = [];
        public List<(string GroupId, string UserId)> Removed { get; } = [];

        public Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Added.Add((groupObjectId, memberObjectId));
            return Task.FromResult(GraphOperationResult.Success());
        }

        public Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string memberObjectId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Removed.Add((groupObjectId, memberObjectId));
            return Task.FromResult(GraphOperationResult.Success());
        }
    }

    private sealed class RecordingLicenseCommands : ILicenseAssignmentCommands
    {
        public List<(string UserId, string SkuId, bool Add)> Assignments { get; } = [];

        public Task<GraphOperationResult> AssignLicenseAsync(string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken)
        {
            Assignments.Add((userObjectId, command.SkuId, true));
            return Task.FromResult(GraphOperationResult.Success());
        }

        public Task<GraphOperationResult> RemoveLicenseAsync(string userObjectId, string skuId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Assignments.Add((userObjectId, skuId, false));
            return Task.FromResult(GraphOperationResult.Success());
        }
    }

    private sealed class RecordingGroupCatalogReader : IGroupCatalogReader
    {
        public IReadOnlyList<GroupCatalogItem> Items { get; init; } = [new GroupCatalogItem("route-group", "Route group", null, true, []), new GroupCatalogItem("group-1", "Group one", null, true, [])];
        public GraphOperationResult? Error { get; init; }
        public Task<GraphReadResult<IReadOnlyList<GroupCatalogItem>>> ReadGroupsAsync(string? search, int pageSize, CancellationToken cancellationToken) => Task.FromResult(Error is { } error ? GraphReadResult<IReadOnlyList<GroupCatalogItem>>.Failed(error) : GraphReadResult<IReadOnlyList<GroupCatalogItem>>.Succeeded(Items));
        public Task<GraphReadResult<GroupCatalogItem?>> ReadGroupAsync(string groupObjectId, CancellationToken cancellationToken) => Task.FromResult(Error is { } error ? GraphReadResult<GroupCatalogItem?>.Failed(error) : GraphReadResult<GroupCatalogItem?>.Succeeded(Items.SingleOrDefault(item => item.Id == groupObjectId)));
    }

    private sealed class RecordingLicenseCatalogReader : ILicenseOverviewReader
    {
        public IReadOnlyList<LicenseOverviewItem> Items { get; init; } = [new LicenseOverviewItem("route-sku", "E3", "Microsoft 365 E3", 1, 1), new LicenseOverviewItem("sku-1", "E1", "Microsoft 365 E1", 1, 1)];
        public GraphOperationResult? Error { get; init; }
        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(WorkspaceContext context, LicenseOverviewQuery query, CancellationToken cancellationToken) => Task.FromResult(Error is { } error ? GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(error) : GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded(Items));
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
                new Claim("oid", "22222222-2222-2222-2222-222222222222"),
                new Claim("tid", "11111111-1111-1111-1111-111111111111"),
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
