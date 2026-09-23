using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserSecurityCommandServiceTests
{
    [Fact]
    public async Task First_tap_response_exposes_the_code_once_but_safe_idempotency_json_does_not_contain_it()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new RecordingAuditWriter();
        var service = CreateAuthenticationService(idempotency, audit, new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

        result.TemporaryAccessPass.Should().Be("fixture-tap-value");
        idempotency.Records.Single().SafeResultJson.Should().NotContain("fixture-tap-value");
        audit.Events.Single().SafeMetadataJson.Should().Be("{}");
    }

    [Fact]
    public async Task Replayed_tap_request_never_returns_the_original_code()
    {
        var idempotency = new MemoryIdempotencyService();
        var service = CreateAuthenticationService(idempotency, new RecordingAuditWriter(), new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));

        _ = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);
        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

        result.Replayed.Should().BeTrue();
        result.TemporaryAccessPass.Should().BeNull();
        result.Error.Should().Be("temporary_access_pass_already_issued");
    }

    [Fact]
    public async Task Invalid_tap_graph_result_is_not_succeeded_or_persisted_with_a_code()
    {
        var idempotency = new MemoryIdempotencyService();
        var service = CreateAuthenticationService(idempotency, new RecordingAuditWriter(), new GraphTemporaryAccessPassResult(null, "tap-1", null, 60, true));

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "invalid-tap", CancellationToken.None);

        result.Status.Should().Be("temporarily_unavailable");
        result.Error.Should().Be("invalid_response");
        result.TemporaryAccessPass.Should().BeNull();
        idempotency.Records.Single().SafeResultJson.Should().NotContain("fixture-tap-value");
    }

    [Fact]
    public async Task Tap_audit_preserves_only_graph_correlation_identifiers()
    {
        var audit = new RecordingAuditWriter();
        var service = CreateAuthenticationService(new MemoryIdempotencyService(), audit, new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true, null, "corr-1", "req-1"));

        await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "audit-tap", CancellationToken.None);

        audit.Events.Single().GraphCorrelationId.Should().Be("corr-1");
        audit.Events.Single().GraphRequestId.Should().Be("req-1");
        audit.Events.Single().SafeMetadataJson.Should().Be("{}");
        audit.Events.Single().SafeMetadataJson.Should().NotContain("fixture-tap-value");
    }

    [Fact]
    public async Task Missing_tap_consent_denies_direct_service_call_without_command_dispatch()
    {
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));
        var service = CreateAuthenticationService(new MemoryIdempotencyService(), new RecordingAuditWriter(), commands: commands, snapshot: ConsentMissingSnapshot);

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "consent-tap", CancellationToken.None);

        result.Status.Should().Be("denied");
        commands.TemporaryAccessPassCalls.Should().Be(0);
    }

    [Fact]
    public async Task Inactive_pim_denies_direct_session_service_call_without_command_dispatch()
    {
        var commands = new RecordingSessionCommands();
        var service = CreateSessionService(commands, InactivePimSnapshot);

        var result = await service.RevokeAsync(Context(), "user-1", "pim-session", CancellationToken.None);

        result.Status.Should().Be("denied");
        commands.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("user/1", "key-1")]
    [InlineData("user\\1", "key-1")]
    [InlineData("user\u0001", "key-1")]
    [InlineData("", "key-1")]
    [InlineData("user-1", "")]
    public async Task Session_service_rejects_unsafe_or_blank_values_without_command_dispatch(string userObjectId, string idempotencyKey)
    {
        var commands = new RecordingSessionCommands();
        var service = CreateSessionService(commands);

        var result = await service.RevokeAsync(Context(), userObjectId, idempotencyKey, CancellationToken.None);

        result.Status.Should().Be("invalid_target");
        commands.Calls.Should().Be(0);
    }

    private static AuthenticationMethodService CreateAuthenticationService(
        IIdempotencyService idempotency,
        IAuditWriter audit,
        GraphTemporaryAccessPassResult? tap = null,
        StubAuthenticationMethodCommands? commands = null,
        GraphAuthorizationSnapshot? snapshot = null) =>
        new(
            new StubAuthenticationMethodReader(),
            new StaticAuthorizationReader(snapshot ?? AllowedSnapshot),
            commands ?? new StubAuthenticationMethodCommands(tap ?? new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true)),
            idempotency,
            audit);

    private static UserSessionCommandService CreateSessionService(RecordingSessionCommands commands, GraphAuthorizationSnapshot? snapshot = null) =>
        new(new StaticAuthorizationReader(snapshot ?? AllowedSnapshot), commands, new MemoryIdempotencyService(), new RecordingAuditWriter());

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member", null),
        new AuthorizationWorkspaceMembership(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Workspace", "member"));

    private sealed class StubAuthenticationMethodReader : IAuthenticationMethodReader
    {
        public Task<GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>> ReadAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Succeeded([]));
    }

    private sealed class StubAuthenticationMethodCommands(GraphTemporaryAccessPassResult tap) : IAuthenticationMethodCommands
    {
        public int TemporaryAccessPassCalls { get; private set; }
        public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
            CountTapAsync();

        private Task<GraphTemporaryAccessPassResult> CountTapAsync()
        {
            TemporaryAccessPassCalls++;
            return Task.FromResult(tap);
        }
    }

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingSessionCommands : IUserSessionCommands
    {
        public int Calls { get; private set; }
        public Task<GraphOperationResult> RevokeAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(GraphOperationResult.Success());
        }
    }

    private static GraphAuthorizationSnapshot AllowedSnapshot => GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All", "UserAuthenticationMethod.Read.All", "UserAuthenticationMethod.ReadWrite.All", "User.RevokeSessions.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]);
    private static GraphAuthorizationSnapshot ConsentMissingSnapshot => GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]);
    private static GraphAuthorizationSnapshot InactivePimSnapshot => GraphAuthorizationSnapshot.Available("actor-1", ["Directory.Read.All", "User.Read.All", "User.RevokeSessions.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired))]);
}
