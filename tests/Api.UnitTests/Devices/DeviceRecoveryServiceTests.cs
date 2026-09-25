using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Devices;

public sealed class DeviceRecoveryServiceTests
{
    [Fact]
    public void Recovery_scopes_include_separate_metadata_and_secret_permissions()
    {
        GraphScopeCatalog.CapabilityEvaluationScopes.Should().Contain([
            "BitlockerKey.ReadBasic.All", "BitlockerKey.Read.All",
            "DeviceLocalCredential.ReadBasic.All", "DeviceLocalCredential.Read.All"]);
    }

    [Fact]
    public async Task Reveal_rejects_empty_reason_before_reading_graph()
    {
        var fixture = new Fixture();
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "  ", CancellationToken.None);
        result.Status.Should().Be("reason_required");
        fixture.Detail.Calls.Should().Be(0);
        fixture.Recovery.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Reveal_validates_current_managed_device_then_reads_secret_and_audits_only_safe_context()
    {
        var fixture = new Fixture();
        fixture.Recovery.LapsSecret = GraphReadResult<LapsSecret>.Succeeded(new LapsSecret("LocalAdmin", "sensitive-password", null));
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Helpdesk incident 123", CancellationToken.None);
        result.Status.Should().Be("succeeded");
        result.Data!.Password.Should().Be("sensitive-password");
        fixture.Recovery.EntraIds.Should().ContainSingle().Which.Should().Be("aad-1");
        fixture.Audit.Events.Should().ContainSingle();
        var audit = fixture.Audit.Events.Single();
        audit.ActorObjectId.Should().Be(fixture.Context.User.ObjectId);
        audit.WorkspaceId.Should().Be(fixture.Context.Membership.WorkspaceId);
        audit.TargetId.Should().Be("device-1");
        audit.SafeMetadataJson.Should().Contain("Helpdesk incident 123");
        audit.SafeMetadataJson.Should().NotContain("sensitive-password");
    }

    [Fact]
    public async Task Reveal_fails_closed_when_audit_persistence_fails()
    {
        var fixture = new Fixture();
        fixture.Recovery.LapsSecret = GraphReadResult<LapsSecret>.Succeeded(new LapsSecret("LocalAdmin", "sensitive-password", null));
        fixture.Audit.Fail = true;
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("audit_unavailable");
        result.Data.Should().BeNull();
    }

    [Fact]
    public async Task Reveal_does_not_read_secret_when_target_has_no_Entra_id()
    {
        var fixture = new Fixture();
        fixture.Detail.Device = fixture.Detail.Device with { AzureAdDeviceId = null };
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("entra_device_missing");
        fixture.Recovery.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_role_evidence_does_not_block_a_scoped_reveal_attempt()
    {
        var fixture = new Fixture();
        fixture.Authorization.Snapshot = GraphAuthorizationSnapshot.Available("actor", ["DeviceLocalCredential.Read.All"], []);
        fixture.Recovery.LapsSecret = GraphReadResult<LapsSecret>.Failed(new GraphOperationResult(false, "not_authorized", 403, CorrelationId: "corr-1", RequestId: "req-1"));
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("graph_forbidden");
        result.GraphCorrelationId.Should().Be("corr-1");
        fixture.Recovery.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Missing_secret_scope_is_distinct_from_Graph_forbidden()
    {
        var fixture = new Fixture();
        fixture.Authorization.Snapshot = GraphAuthorizationSnapshot.Available("actor", ["DeviceLocalCredential.ReadBasic.All"], []);
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("missing_scope");
        fixture.Recovery.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Bitlocker_reveal_rejects_a_key_not_listed_for_the_validated_device()
    {
        var fixture = new Fixture();
        fixture.Recovery.BitlockerKeys = GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>.Succeeded([
            new BitlockerRecoveryMetadata("other-key", "aad-1", null, "1")]);
        var result = await fixture.Service.RevealBitlockerAsync(fixture.Context, "device-1", "unrelated-key", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("recovery_not_found");
        fixture.Recovery.BitlockerSecretCalls.Should().Be(0);
        fixture.Audit.Events.Single().Outcome.Should().Be("recovery_not_found");
    }

    [Fact]
    public async Task Scope_probe_consent_and_transient_failures_are_distinct()
    {
        var fixture = new Fixture();
        fixture.Authorization.Snapshot = GraphAuthorizationSnapshot.Available("actor", [], [], scopeProblems: new Dictionary<string, string> { ["DeviceLocalCredential.Read.All"] = "consent_required" });
        var consent = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        consent.Status.Should().Be("consent_required");

        fixture.Authorization.Snapshot = GraphAuthorizationSnapshot.Available("actor", [], [], scopeProblems: new Dictionary<string, string> { ["DeviceLocalCredential.Read.All"] = "temporarily_unavailable" });
        var temporary = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        temporary.Status.Should().Be("temporarily_unavailable");
        fixture.Recovery.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Graph_throttling_keeps_retry_and_correlation_details_without_returning_secret()
    {
        var fixture = new Fixture();
        fixture.Recovery.LapsSecret = GraphReadResult<LapsSecret>.Failed(new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(12), "corr-2", "req-2"));
        var result = await fixture.Service.RevealLapsAsync(fixture.Context, "device-1", "Incident 123", CancellationToken.None);
        result.Status.Should().Be("throttled");
        result.RetryAfterSeconds.Should().Be(12);
        result.GraphRequestId.Should().Be("req-2");
        result.Data.Should().BeNull();
        fixture.Audit.Events.Single().GraphRequestId.Should().Be("req-2");
    }

    private sealed class Fixture
    {
        public WorkspaceContext Context { get; } = new(
            new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "actor@example.com", "Actor", "Member"),
            new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Workspace", "member", ModuleKeys: ["devices"]));
        public DetailReader Detail { get; } = new();
        public RecoveryReader Recovery { get; } = new();
        public AuthorizationReader Authorization { get; } = new();
        public AuditReader Audit { get; } = new();
        public DeviceRecoveryService Service => new(Detail, Recovery, Authorization, Audit);
    }

    private sealed class DetailReader : IManagedDeviceDetailReader
    {
        public int Calls { get; private set; }
        public ManagedDeviceSummary Device { get; set; } = new("device-1", "WIN-01", "Windows", "11", "compliant", "managed", "company", null, "user-1", "aad-1", "serial", "Contoso", "Model");
        public Task<GraphReadResult<ManagedDeviceSummary?>> GetAsync(string id, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(GraphReadResult<ManagedDeviceSummary?>.Succeeded(id == Device.Id ? Device : null));
        }
    }

    private sealed class RecoveryReader : IGraphDeviceRecoveryReader
    {
        public int Calls { get; private set; }
        public List<string> EntraIds { get; } = [];
        public GraphReadResult<LapsSecret> LapsSecret { get; set; } = GraphReadResult<LapsSecret>.Succeeded(new LapsSecret("Admin", "password", null));
        public GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>> BitlockerKeys { get; set; } = GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>.Succeeded([]);
        public int BitlockerSecretCalls { get; private set; }
        public Task<GraphReadResult<IReadOnlyList<BitlockerRecoveryMetadata>>> ListBitlockerAsync(string entraId, bool secretScope, CancellationToken cancellationToken) => Task.FromResult(BitlockerKeys);
        public Task<GraphReadResult<BitlockerSecret>> GetBitlockerAsync(string keyId, CancellationToken cancellationToken) { BitlockerSecretCalls++; return Task.FromResult(GraphReadResult<BitlockerSecret>.Succeeded(new BitlockerSecret("key"))); }
        public Task<GraphReadResult<LapsMetadata>> GetLapsMetadataAsync(string entraId, bool secretScope, CancellationToken cancellationToken) => Task.FromResult(GraphReadResult<LapsMetadata>.Succeeded(new LapsMetadata(entraId, "WIN-01", null, null)));
        public Task<GraphReadResult<LapsSecret>> GetLapsSecretAsync(string entraId, CancellationToken cancellationToken)
        {
            Calls++;
            EntraIds.Add(entraId);
            return Task.FromResult(LapsSecret);
        }
    }

    private sealed class AuthorizationReader : IGraphAuthorizationSnapshotReader
    {
        public GraphAuthorizationSnapshot Snapshot { get; set; } = GraphAuthorizationSnapshot.Available("actor", ["DeviceLocalCredential.Read.All", "BitlockerKey.Read.All"], []);
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
    }

    private sealed class AuditReader : IAuditWriter
    {
        public bool Fail { get; set; }
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            if (Fail) throw new InvalidOperationException("audit down");
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
