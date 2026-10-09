using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Identity;
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
using AuditEvent = Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities.AuditEvent;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Users;

public sealed class UserSecurityCommandEndpointTests
{
    [Fact]
    public async Task Authentication_and_session_writes_reject_missing_blank_and_oversized_reasons_before_dispatch()
    {
        var authentication = new RecordingAuthenticationCommands();
        var sessions = new RecordingSessionCommands();
        var audit = new RecordingAuditWriter();
        using var factory = CreateFactory(authentication, sessions, auditWriter: audit);
        using var client = AuthenticatedClient(factory);
        var writes = new (HttpMethod Method, string Path)[]
        {
            (HttpMethod.Delete, "/api/users/user-1/authentication-methods/method-1?type=fido2AuthenticationMethod"),
            (HttpMethod.Post, "/api/users/user-1/authentication-methods/reset-mfa"),
            (HttpMethod.Post, "/api/users/user-1/authentication-methods/temporary-access-pass"),
            (HttpMethod.Post, "/api/users/user-1/revoke-sessions")
        };
        var reasons = new (string? Value, string Error)[]
        {
            (null, "reason_required"),
            ("   ", "reason_required"),
            (new string('x', 1001), "reason_too_long")
        };
        var index = 0;

        foreach (var (method, path) in writes)
        foreach (var (reason, error) in reasons)
        {
            var body = new System.Text.Json.Nodes.JsonObject();
            if (reason is not null) body["reason"] = reason;
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Idempotency-Key", $"invalid-reason-{index++}");
            var response = await client.SendAsync(request);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            (await response.Content.ReadAsStringAsync()).Should().Contain(error);
        }

        authentication.Calls.Should().Be(0);
        authentication.RemoveCalls.Should().Be(0);
        sessions.Calls.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Security_routes_reject_invalid_id_and_missing_idempotency_key()
    {
        using var factory = CreateFactory();
        using var client = AuthenticatedClient(factory);

        var invalid = await client.PostAsync("/api/users/user%2Fbad/revoke-sessions", null);
        var missing = await client.PostAsync(
            "/api/users/user-1/revoke-sessions",
            new StringContent("""{"reason":"session revocation"}""", Encoding.UTF8, "application/json"));

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
        var audit = new RecordingAuditWriter();
        using var factory = CreateFactory(authentication, sessions, auditWriter: audit);
        using var client = AuthenticatedClient(factory);

        var tap = await SendAsync(client, "/api/users/user-1/authentication-methods/temporary-access-pass", "tap-key");
        var reset = await SendAsync(client, "/api/users/user-1/authentication-methods/reset-mfa", "reset-key");
        using var removeRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/users/user-1/authentication-methods/method-1?type=fido2AuthenticationMethod")
        {
            Content = new StringContent("""{"reason":"device replaced"}""", Encoding.UTF8, "application/json")
        };
        removeRequest.Headers.Add("Idempotency-Key", "remove-key");
        var remove = await client.SendAsync(removeRequest);
        var revoke = await SendAsync(client, "/api/users/user-1/revoke-sessions", "session-key");
        var tapBody = await tap.Content.ReadAsStringAsync();
        var revokeBody = await revoke.Content.ReadAsStringAsync();

        tap.StatusCode.Should().Be(HttpStatusCode.OK);
        tapBody.Should().Contain("fixture-tap-value");
        tapBody.Should().NotContain("secret");
        authentication.TemporaryAccessPassRequests.Should().ContainSingle().Which.Should().Be(new TemporaryAccessPassRequest());
        reset.StatusCode.Should().Be(HttpStatusCode.OK);
        remove.StatusCode.Should().Be(HttpStatusCode.OK);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        revokeBody.Should().Contain("users.sessions.revoke");
        authentication.Calls.Should().Be(1);
        authentication.RemoveCalls.Should().Be(1);
        sessions.Calls.Should().Be(1);
        audit.Events.Should().HaveCount(4);
        audit.Events.Select(entry => entry.SafeMetadataJson).Should().Contain("""{"reason":"security operation"}""");
        audit.Events.Select(entry => entry.SafeMetadataJson).Should().Contain("""{"reason":"device replaced"}""");
    }

    [Theory]
    [InlineData("""{"reason":"recovery","lifetimeInMinutes":9,"isUsableOnce":true}""")]
    [InlineData("""{"reason":"recovery","lifetimeInMinutes":1441,"isUsableOnce":true}""")]
    [InlineData("""{"reason":"recovery","lifetimeInMinutes":61.5,"isUsableOnce":true}""")]
    public async Task Temporary_access_pass_rejects_invalid_lifetime_before_graph_dispatch(string body)
    {
        var authentication = new RecordingAuthenticationCommands();
        var audit = new RecordingAuditWriter();
        using var factory = CreateFactory(authentication, auditWriter: audit);
        using var client = AuthenticatedClient(factory);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/user-1/authentication-methods/temporary-access-pass")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("Idempotency-Key", "invalid-tap-lifetime");

        var response = await client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        authentication.Calls.Should().Be(0);
        audit.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Temporary_access_pass_options_reach_service_and_changed_options_conflict_for_same_key()
    {
        var authentication = new RecordingAuthenticationCommands();
        var audit = new RecordingAuditWriter();
        using var factory = CreateFactory(authentication, auditWriter: audit);
        using var client = AuthenticatedClient(factory);

        var first = await SendTapAsync(client, "same-tap-options", """{"reason":"recovery","lifetimeInMinutes":61,"isUsableOnce":false}""");
        var reused = await SendTapAsync(client, "same-tap-options", """{"reason":"recovery","lifetimeInMinutes":61,"isUsableOnce":true}""");

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reused.Content.ReadAsStringAsync()).Should().Contain("idempotency_key_reused");
        authentication.Calls.Should().Be(1);
        authentication.TemporaryAccessPassRequests.Should().ContainSingle().Which.Should().Be(new TemporaryAccessPassRequest(61, false));
        audit.Events.Should().ContainSingle();
        audit.Events.Single().SafeMetadataJson.Should().Be("""{"reason":"recovery"}""");
    }

    [Fact]
    public async Task Temporary_access_pass_policy_failure_is_explicit_safe_and_keeps_graph_ids()
    {
        var authentication = new RecordingAuthenticationCommands
        {
            TapResult = new GraphTemporaryAccessPassResult(null, null, null, null, null,
                new GraphOperationResult(false, "tenant_policy_rejected", 400, CorrelationId: "corr-policy", RequestId: "req-policy"))
        };
        using var factory = CreateFactory(authentication);
        using var client = AuthenticatedClient(factory);

        var response = await SendTapAsync(client, "policy-tap", """{"reason":"recovery","lifetimeInMinutes":480,"isUsableOnce":true}""");
        var body = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        body.Should().Contain("tenant_policy_rejected");
        body.Should().Contain("corr-policy");
        body.Should().Contain("req-policy");
        body.Should().NotContain("raw");
    }

    [Fact]
    public async Task Authentication_method_remove_returns_conflict_when_idempotency_key_is_reused_with_a_different_reason()
    {
        var authentication = new RecordingAuthenticationCommands();
        var audit = new RecordingAuditWriter();
        using var factory = CreateFactory(authentication, auditWriter: audit);
        using var client = AuthenticatedClient(factory);

        using var firstRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/users/user-1/authentication-methods/method-1?type=fido2AuthenticationMethod")
        {
            Content = new StringContent("""{"reason":"device replaced"}""", Encoding.UTF8, "application/json")
        };
        firstRequest.Headers.Add("Idempotency-Key", "same-remove-key");
        var first = await client.SendAsync(firstRequest);
        using var reusedRequest = new HttpRequestMessage(HttpMethod.Delete, "/api/users/user-1/authentication-methods/method-1?type=fido2AuthenticationMethod")
        {
            Content = new StringContent("""{"reason":"different reason"}""", Encoding.UTF8, "application/json")
        };
        reusedRequest.Headers.Add("Idempotency-Key", "same-remove-key");
        var reused = await client.SendAsync(reusedRequest);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        reused.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await reused.Content.ReadAsStringAsync()).Should().Contain("idempotency_key_reused");
        authentication.RemoveCalls.Should().Be(1);
        audit.Events.Should().ContainSingle();
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

    private static StringContent Content(string key) { var content = new StringContent("""{"reason":"session revocation"}""", Encoding.UTF8, "application/json"); content.Headers.Add("Idempotency-Key", key); return content; }
    private static Task<HttpResponseMessage> SendAsync(HttpClient client, string path, string key) { var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = new StringContent("""{"reason":"security operation"}""", Encoding.UTF8, "application/json") }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private static Task<HttpResponseMessage> SendTapAsync(HttpClient client, string key, string body) { var request = new HttpRequestMessage(HttpMethod.Post, "/api/users/user-1/authentication-methods/temporary-access-pass") { Content = new StringContent(body, Encoding.UTF8, "application/json") }; request.Headers.Add("Idempotency-Key", key); return client.SendAsync(request); }
    private static HttpClient AuthenticatedClient(WebApplicationFactory<Program> factory) { var client = factory.CreateClient(); client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test"); return client; }

    private static WebApplicationFactory<Program> CreateFactory(
        RecordingAuthenticationCommands? authentication = null,
        RecordingSessionCommands? sessions = null,
        GraphAuthorizationSnapshot? snapshot = null,
        bool includeMembership = true,
        RecordingAuditWriter? auditWriter = null) =>
        new ApiIntegrationTestFactory().WithWebHostBuilder(builder =>
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
                services.RemoveAll<IAuthenticationMethodReader>();
                services.AddSingleton<IAuthenticationMethodReader>(new EmptyAuthenticationMethodReader());
                services.RemoveAll<IUserSessionCommands>();
                services.AddSingleton<IUserSessionCommands>(sessions ?? new RecordingSessionCommands());
                services.RemoveAll<IIdempotencyService>();
                services.AddSingleton<IIdempotencyService>(new MemoryIdempotencyService());
                services.RemoveAll<IAuditWriter>();
                services.AddSingleton<IAuditWriter>(auditWriter ?? new RecordingAuditWriter());
            });
        });

    private static GraphAuthorizationSnapshot AllowedSnapshot => GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All", "UserAuthenticationMethod.Read.All", "UserAuthenticationMethod.ReadWrite.All", "User.RevokeSessions.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class RecordingAuthenticationCommands : IAuthenticationMethodCommands
    {
        public int Calls { get; private set; }
        public int RemoveCalls { get; private set; }
        public List<TemporaryAccessPassRequest> TemporaryAccessPassRequests { get; } = [];
        public GraphTemporaryAccessPassResult TapResult { get; set; } = new("fixture-tap-value", "tap-1", DateTimeOffset.Parse("2026-09-23T10:00:00Z"), 60, true);
        public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken) { RemoveCalls++; return Task.FromResult(GraphOperationResult.Success()); }
        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) => CreateTemporaryAccessPassAsync(userObjectId, idempotencyKey, new TemporaryAccessPassRequest(), cancellationToken);
        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, TemporaryAccessPassRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            TemporaryAccessPassRequests.Add(request with { Reason = null });
            return Task.FromResult(TapResult.Error is null ? TapResult with { LifetimeInMinutes = request.LifetimeInMinutes, IsUsableOnce = request.IsUsableOnce } : TapResult);
        }
    }
    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) { Events.Add(auditEvent); return Task.CompletedTask; }
    }
    private sealed class EmptyAuthenticationMethodReader : IAuthenticationMethodReader
    {
        public Task<GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>> ReadAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Succeeded([]));
    }
    private sealed class RecordingSessionCommands : IUserSessionCommands { public int Calls { get; private set; } public Task<GraphOperationResult> RevokeAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) { Calls++; return Task.FromResult(GraphOperationResult.Success()); } }
    private sealed class StaticReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader { public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot); }
    private sealed class FixtureMembershipReader(bool includeMembership) : IWorkspaceMembershipReader { public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) => Task.FromResult<WorkspaceMembership?>(includeMembership ? new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Workspace", "member", ModuleKeys: ["users", "devices", "licenses"]) : null); }
    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public new const string Scheme = "Test";
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("oid", "22222222-2222-2222-2222-222222222222"), new Claim("tid", "11111111-1111-1111-1111-111111111111"), new Claim("preferred_username", "admin@example.com"), new Claim("name", "Admin"), new Claim("aud", "api://atea-unified-workplace-api")], Scheme)), Scheme)));
    }
}
