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
    public async Task Tap_reason_is_audited_and_part_of_idempotency_fingerprint_without_persisting_the_secret()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new RecordingAuditWriter();
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));
        var service = CreateAuthenticationService(idempotency, audit, commands: commands);

        var first = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-tap-key", CancellationToken.None, "  onboarding  ");
        var replay = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-tap-key", CancellationToken.None, "onboarding");
        var reused = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-tap-key", CancellationToken.None, "different reason");

        first.TemporaryAccessPass.Should().Be("fixture-tap-value");
        replay.Replayed.Should().BeTrue();
        replay.TemporaryAccessPass.Should().BeNull();
        reused.Status.Should().Be("idempotency_key_reused");
        commands.TemporaryAccessPassCalls.Should().Be(1);
        audit.Events.Should().ContainSingle();
        audit.Events.Single().SafeMetadataJson.Should().Be("""{"reason":"onboarding"}""");
        idempotency.Records.Single().SafeResultJson.Should().NotContain("fixture-tap-value");
    }

    [Fact]
    public async Task Tap_options_and_normalized_reason_are_forwarded_and_conflicting_options_reuse_is_rejected()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new RecordingAuditWriter();
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 61, false));
        var service = CreateAuthenticationService(idempotency, audit, commands: commands);
        var request = new TemporaryAccessPassRequest(61, false, "  account recovery  ");

        var first = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-options-key", request, CancellationToken.None);
        var replay = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-options-key", new TemporaryAccessPassRequest(61, false, "account recovery"), CancellationToken.None);
        var reused = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "same-options-key", new TemporaryAccessPassRequest(61, true, "account recovery"), CancellationToken.None);

        first.Status.Should().Be("succeeded");
        first.LifetimeInMinutes.Should().Be(61);
        first.IsUsableOnce.Should().BeFalse();
        replay.Replayed.Should().BeTrue();
        replay.TemporaryAccessPass.Should().BeNull();
        reused.Status.Should().Be("idempotency_key_reused");
        commands.TemporaryAccessPassCalls.Should().Be(1);
        commands.TemporaryAccessPassRequests.Should().ContainSingle().Which.Should().Be(new TemporaryAccessPassRequest(61, false));
        audit.Events.Should().ContainSingle();
        audit.Events.Single().SafeMetadataJson.Should().Be("""{"reason":"account recovery"}""");
        idempotency.Records.Single().SafeResultJson.Should().NotContain("fixture-tap-value");
    }

    [Fact]
    public async Task Request_aware_service_overload_preserves_legacy_implementations_and_rejects_unsupported_options()
    {
        var legacyService = new LegacyAuthenticationMethodService();
        IAuthenticationMethodService service = legacyService;

        var delegated = await service.CreateTemporaryAccessPassAsync(
            Context(),
            "user-1",
            "legacy-default",
            new TemporaryAccessPassRequest(60, true, "legacy reason"),
            CancellationToken.None);
        var unsupported = await service.CreateTemporaryAccessPassAsync(
            Context(),
            "user-1",
            "legacy-options",
            new TemporaryAccessPassRequest(61, false, "other reason"),
            CancellationToken.None);

        delegated.Status.Should().Be("succeeded");
        legacyService.TapCalls.Should().Be(1);
        legacyService.LastReason.Should().Be("legacy reason");
        unsupported.Status.Should().Be("unsupported_options");
        unsupported.Error.Should().Be("unsupported_options");
        legacyService.TapCalls.Should().Be(1);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(1441)]
    public async Task Tap_service_rejects_out_of_range_options_without_command_dispatch(int lifetimeInMinutes)
    {
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, lifetimeInMinutes, true));
        var service = CreateAuthenticationService(new MemoryIdempotencyService(), new RecordingAuditWriter(), commands: commands);

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "invalid-options", new TemporaryAccessPassRequest(lifetimeInMinutes), CancellationToken.None);

        result.Status.Should().Be("invalid_target");
        result.Error.Should().Be("invalid_request");
        commands.TemporaryAccessPassCalls.Should().Be(0);
    }

    [Fact]
    public async Task Tap_tenant_policy_rejection_is_actionable_and_keeps_graph_correlation_ids()
    {
        var policyError = new GraphOperationResult(false, "tenant_policy_rejected", 400, CorrelationId: "corr-policy", RequestId: "req-policy");
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult(null, null, null, null, null, policyError));
        var audit = new RecordingAuditWriter();
        var service = CreateAuthenticationService(new MemoryIdempotencyService(), audit, commands: commands);

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "policy-options", new TemporaryAccessPassRequest(480, Reason: "recovery access"), CancellationToken.None);

        result.Status.Should().Be("policy_rejected");
        result.Error.Should().Be("tenant_policy_rejected");
        result.GraphCorrelationId.Should().Be("corr-policy");
        result.GraphRequestId.Should().Be("req-policy");
        audit.Events.Single().FailureCategory.Should().Be("tenant_policy_rejected");
    }

    [Fact]
    public async Task Authentication_method_remove_and_reset_reject_changed_reasons_for_reused_keys()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new RecordingAuditWriter();
        var commands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));
        var service = CreateAuthenticationService(idempotency, audit, commands: commands);

        var remove = await service.RemoveAsync(Context(), "user-1", "method-1", "fido2AuthenticationMethod", "same-method-key", CancellationToken.None, "security cleanup");
        var removeReplay = await service.RemoveAsync(Context(), "user-1", "method-1", "fido2AuthenticationMethod", "same-method-key", CancellationToken.None, "security cleanup");
        var removeReused = await service.RemoveAsync(Context(), "user-1", "method-1", "fido2AuthenticationMethod", "same-method-key", CancellationToken.None, "different reason");
        var reset = await service.ResetMfaAsync(Context(), "user-1", "same-reset-key", CancellationToken.None, "lost device");
        var resetReused = await service.ResetMfaAsync(Context(), "user-1", "same-reset-key", CancellationToken.None, "different reason");

        remove.Status.Should().Be("succeeded");
        removeReplay.Replayed.Should().BeTrue();
        removeReused.Status.Should().Be("idempotency_key_reused");
        reset.Status.Should().Be("succeeded");
        resetReused.Status.Should().Be("idempotency_key_reused");
        commands.RemoveCalls.Should().Be(1);
        audit.Events.Should().HaveCount(2);
        audit.Events[0].SafeMetadataJson.Should().Be("""{"reason":"security cleanup"}""");
        audit.Events[1].SafeMetadataJson.Should().Be("""{"reason":"lost device"}""");
    }

    [Fact]
    public async Task Session_reason_is_audited_and_changed_reason_conflicts_without_a_second_graph_call()
    {
        var commands = new RecordingSessionCommands();
        var audit = new RecordingAuditWriter();
        var service = CreateSessionService(commands, audit: audit);

        var first = await service.RevokeAsync(Context(), "user-1", "same-session-key", CancellationToken.None, "  offboarding  ");
        var reused = await service.RevokeAsync(Context(), "user-1", "same-session-key", CancellationToken.None, "different reason");

        first.Status.Should().Be("succeeded");
        reused.Status.Should().Be("idempotency_key_reused");
        commands.Calls.Should().Be(1);
        audit.Events.Should().ContainSingle();
        audit.Events.Single().SafeMetadataJson.Should().Be("""{"reason":"offboarding"}""");
    }

    [Fact]
    public async Task Audit_failure_warning_survives_same_key_replays_without_repeating_mutations_or_persisting_tap_secrets()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new FailingAuditWriter();
        var authenticationCommands = new StubAuthenticationMethodCommands(new GraphTemporaryAccessPassResult("fixture-tap-value", "tap-1", null, 60, true));
        var authentication = CreateAuthenticationService(idempotency, audit, commands: authenticationCommands);
        var sessionCommands = new RecordingSessionCommands();
        var sessions = new UserSessionCommandService(new StaticAuthorizationReader(AllowedSnapshot), sessionCommands, idempotency, audit);

        var remove = await authentication.RemoveAsync(Context(), "user-1", "method-1", "fido2AuthenticationMethod", "audit-remove", CancellationToken.None);
        var removeReplay = await authentication.RemoveAsync(Context(), "user-1", "method-1", "fido2AuthenticationMethod", "audit-remove", CancellationToken.None);
        var reset = await authentication.ResetMfaAsync(Context(), "user-1", "audit-reset", CancellationToken.None);
        var resetReplay = await authentication.ResetMfaAsync(Context(), "user-1", "audit-reset", CancellationToken.None);
        var tap = await authentication.CreateTemporaryAccessPassAsync(Context(), "user-1", "audit-tap", CancellationToken.None);
        var tapReplay = await authentication.CreateTemporaryAccessPassAsync(Context(), "user-1", "audit-tap", CancellationToken.None);
        var revoke = await sessions.RevokeAsync(Context(), "user-1", "audit-session", CancellationToken.None);
        var revokeReplay = await sessions.RevokeAsync(Context(), "user-1", "audit-session", CancellationToken.None);

        new string?[] { remove.AuditWarning, removeReplay.AuditWarning, reset.AuditWarning, resetReplay.AuditWarning, tap.AuditWarning, tapReplay.AuditWarning, revoke.AuditWarning, revokeReplay.AuditWarning }
            .Should().OnlyContain(warning => warning == "audit_persistence_failed");
        removeReplay.Replayed.Should().BeTrue();
        resetReplay.Replayed.Should().BeTrue();
        tapReplay.Replayed.Should().BeTrue();
        tapReplay.TemporaryAccessPass.Should().BeNull();
        tapReplay.Error.Should().Be("temporary_access_pass_already_issued");
        revokeReplay.Replayed.Should().BeTrue();
        authenticationCommands.RemoveCalls.Should().Be(1);
        authenticationCommands.TemporaryAccessPassCalls.Should().Be(1);
        sessionCommands.Calls.Should().Be(1);
        idempotency.Records.Should().HaveCount(4);
        idempotency.Records.Should().OnlyContain(record => record.SafeResultJson.Contains("audit_persistence_failed", StringComparison.Ordinal));
        var tapRecord = idempotency.Records[2];
        tapRecord.SafeResultJson.Should().NotContain("fixture-tap-value");
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

    private static UserSessionCommandService CreateSessionService(RecordingSessionCommands commands, GraphAuthorizationSnapshot? snapshot = null, RecordingAuditWriter? audit = null) =>
        new(new StaticAuthorizationReader(snapshot ?? AllowedSnapshot), commands, new MemoryIdempotencyService(), audit ?? new RecordingAuditWriter());

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
        public int RemoveCalls { get; private set; }
        public List<TemporaryAccessPassRequest> TemporaryAccessPassRequests { get; } = [];
        public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken)
        {
            RemoveCalls++;
            return Task.FromResult(GraphOperationResult.Success());
        }

        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
            CountTapAsync();

        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, TemporaryAccessPassRequest request, CancellationToken cancellationToken)
        {
            TemporaryAccessPassRequests.Add(request with { Reason = null });
            return CountTapAsync();
        }

        private Task<GraphTemporaryAccessPassResult> CountTapAsync()
        {
            TemporaryAccessPassCalls++;
            return Task.FromResult(tap);
        }
    }

    private sealed class LegacyAuthenticationMethodService : IAuthenticationMethodService
    {
        public int TapCalls { get; private set; }
        public string? LastReason { get; private set; }

        public Task<AuthenticationMethodsResponse> GetAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<AuthenticationMethodCommandResult> RemoveAsync(WorkspaceContext context, string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken, string? reason = null) =>
            throw new NotSupportedException();

        public Task<AuthenticationMethodCommandResult> ResetMfaAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null) =>
            throw new NotSupportedException();

        public Task<TemporaryAccessPassCommandResult> CreateTemporaryAccessPassAsync(WorkspaceContext context, string userObjectId, string idempotencyKey, CancellationToken cancellationToken, string? reason = null)
        {
            TapCalls++;
            LastReason = reason;
            return Task.FromResult(new TemporaryAccessPassCommandResult("succeeded", Capability.AuthenticationMethodsManage, "legacy-tap"));
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

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Audit persistence failed.");
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
