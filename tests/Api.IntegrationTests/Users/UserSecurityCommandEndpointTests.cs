using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Users;

public sealed class UserSecurityCommandEndpointTests
{
    [Fact]
    public async Task Security_routes_reject_invalid_id_and_missing_idempotency_key()
    {
        using var factory = CreateFactory();
        using var client = AuthenticatedClient(factory);

        var invalid = await client.PostAsync("/api/users/user%2Fbad/revoke-sessions", null);
        var missing = await client.PostAsync("/api/users/user-1/revoke-sessions", null);

        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await missing.Content.ReadAsStringAsync()).Should().Contain("idempotency_key_required");
    }

    [Fact]
    public async Task Security_routes_require_membership_and_capability()
    {
        using var noMembership = CreateFactory(includeMembership: false);
        using var noMembershipClient = AuthenticatedClient(noMembership);
        var membershipResponse = await noMembershipClient.PostAsync("/api/users/user-1/revoke-sessions", Content("key-1"));
        membershipResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await membershipResponse.Content.ReadAsStringAsync()).Should().Contain("authorization_denied");

        using var denied = CreateFactory(snapshot: GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]));
        using var deniedClient = AuthenticatedClient(denied);
        var deniedResponse = await deniedClient.PostAsync("/api/users/user-1/revoke-sessions", Content("key-2"));
        deniedResponse.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Successful_security_routes_return_safe_bodies()
    {
        var authentication = new RecordingAuthenticationCommands();
        var sessions = new RecordingSessionCommands();
        using var factory = CreateFactory(authentication, sessions);
        using var client = AuthenticatedClient(factory);

        var tap = await SendAsync(client, "/api/users/user-1/authentication-methods/temporary-access-pass", "tap-key");
        var revoke = await SendAsync(client, "/api/users/user-1/revoke-sessions", "session-key");
        var tapBody = await tap.Content.ReadAsStringAsync();
        var revokeBody = await revoke.Content.ReadAsStringAsync();

        tap.StatusCode.Should().Be(HttpStatusCode.OK);
        tapBody.Should().Contain("ABC123");
        tapBody.Should().NotContain("secret");
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        revokeBody.Should().Contain("users.sessions.revoke");
        authentication.Calls.Should().Be(1);
        sessions.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Missing_delegated_consent_denies_both_mutations_without_command_calls()
    {
        var authentication = new RecordingAuthenticationCommands();
        var sessions = new RecordingSessionCommands();
        using var factory = CreateFactory(authentication, sessions, GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]));
        using var client = AuthenticatedClient(factory);

        var tap = await SendAsync(client, "/api/users/user-1/authentication-methods/temporary-access-pass", "consent-tap");
        var revoke = await SendAsync(client, "/api/users/user-1/revoke-sessions", "consent-session");

        tap.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        authentication.Calls.Should().Be(0);
        sessions.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Inactive_pim_denies_both_mutations_without_command_calls()
    {
        var authentication = new RecordingAuthenticationCommands();
        var sessions = new RecordingSessionCommands();
        var eligible = new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired));
        using var factory = CreateFactory(authentication, sessions, GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All", "UserAuthenticationMethod.Read.All", "UserAuthenticationMethod.ReadWrite.All", "User.RevokeSessions.All"], [eligible]));
        using var client = AuthenticatedClient(factory);

        var tap = await SendAsync(client, "/api/users/user-1/authentication-methods/temporary-access-pass", "pim-tap");
        var revoke = await SendAsync(client, "/api/users/user-1/revoke-sessions", "pim-session");

        tap.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        revoke.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        authentication.Calls.Should().Be(0);
        sessions.Calls.Should().Be(0);
    }

    private static StringContent Content(string key) { var content = new StringContent(string.Empty, Encoding.UTF8, "application/json"); content.Headers.Add("Idempotency-Key", key); return content; }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string key) { var request = new HttpRequestMessage(HttpMethod.Post, path); request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory) { var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test"); return client; }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingAuthenticationCommands? authentication = null,
        RecordingSessionCommands? sessions = null,
        GraphAuthorizationSnapshot? snapshot = null,
        bool includeMembership = true) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = "api://atea-unified-workplace-api", ["AzureAd:ClientId"] = "test-client-id"
            }));
            builder.ConfigureServices(services =>
            {
                services.AddAuthentication(options => { options.DefaultAuthenticateScheme = TestAuthenticationHandler.Scheme; options.DefaultChallengeScheme = TestAuthenticationHandler.Scheme; }).AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>(TestAuthenticationHandler.Scheme, _ => { });
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader>(new FixtureMembershipReader(includeMembership));
                services.RemoveAll<IGraphAuthorizationSnapshotReader>();
                services.AddSingleton<IGraphAuthorizationSnapshotReader>(new StaticReader(snapshot ?? AllowedSnapshot));
                services.RemoveAll<IAuthenticationMethodCommands>();
                services.AddSingleton<IAuthenticationMethodCommands>(authentication ?? new RecordingAuthenticationCommands());
                services.RemoveAll<IUserSessionCommands>();
                services.AddSingleton<IUserSessionCommands>(sessions ?? new RecordingSessionCommands());
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService>(new MemoryIdempotencyService());
            });
        });

    private static GraphAuthorizationSnapshot AllowedSnapshot => GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All", "UserAuthenticationMethod.Read.All", "UserAuthenticationMethod.ReadWrite.All", "User.RevokeSessions.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class RecordingAuthenticationCommands : IAuthenticationMethodCommands
    {
        public int Calls { get; private set; }
        public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken) => Task.FromResult(GraphOperationResult.Success());
        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) { Calls++; return Task.FromResult(new GraphTemporaryAccessPassResult("ABC123", "tap-1", DateTimeOffset.Parse("2026-09-23T10:00:00Z"), 60, true)); }
    }
    private sealed class RecordingSessionCommands : IUserSessionCommands { public int Calls { get; private set; } public Task<GraphOperationResult> RevokeAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) { Calls++; return Task.FromResult(GraphOperationResult.Success()); } }
    private sealed class StaticReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader { public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class FixtureMembershipReader(bool includeMembership) : IWorkspaceMembershipReader { public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(includeMembership ? new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Workspace", "member") : null); }
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "22222222-2222-2222-2222-222222222222"), new Claim("tid", "11111111-1111-1111-1111-111111111111"), new Claim("preferred_username", "admin@example.com"), new Claim("name", "Admin"), new Claim("aud", "api://atea-unified-workplace-api")], Scheme)), Scheme)));
    }
}
