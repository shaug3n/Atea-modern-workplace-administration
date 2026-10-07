using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class ConnectionVerificationServiceTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid WorkspaceId = Guid.NewGuid();

    [Fact]
    public async Task Definite_missing_and_unknown_scopes_remain_distinct()
    {
        var scopeReader = new FixtureScopeAvailabilityReader(scope => scope switch
        {
            "User.Read.All" => ScopeAvailability.MissingConsent,
            "Group.Read.All" => ScopeAvailability.Unknown,
            _ => ScopeAvailability.Available
        });
        var onboarding = new RecordingOnboardingService();
        var service = CreateService(ConnectionHealthStatus.Connected, scopeReader, onboarding);

        var result = await service.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.Status.Should().Be(ConnectionState.PermissionIncomplete);
        result.PermissionCoverage!.MissingScopes.Should().Equal("User.Read.All");
        result.PermissionCoverage.UnknownScopes.Should().Equal("Group.Read.All");
        result.PermissionCoverage.AvailableScopes.Should().Contain("User.Read");
        onboarding.RecordedStatus.Should().Be(ConnectionState.PermissionIncomplete);
        onboarding.RecordedScopes.Should().BeEquivalentTo(result.PermissionCoverage.AvailableScopes);
    }

    [Fact]
    public async Task All_catalog_scopes_available_yields_connected()
    {
        var scopeReader = new FixtureScopeAvailabilityReader(_ => ScopeAvailability.Available);
        var service = CreateService(ConnectionHealthStatus.Connected, scopeReader, new RecordingOnboardingService());

        var result = await service.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.Status.Should().Be(ConnectionState.Connected);
        result.PermissionCoverage!.AvailableScopes.Should().BeEquivalentTo(GraphScopeCatalog.CapabilityEvaluationScopes);
        result.PermissionCoverage.MissingScopes.Should().BeEmpty();
        result.PermissionCoverage.UnknownScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_only_scope_probes_yield_temporarily_unavailable()
    {
        var scopeReader = new FixtureScopeAvailabilityReader(_ => ScopeAvailability.Unknown);
        var service = CreateService(ConnectionHealthStatus.Connected, scopeReader, new RecordingOnboardingService());

        var result = await service.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.Status.Should().Be(ConnectionState.TemporarilyUnavailable);
        result.PermissionCoverage!.AvailableScopes.Should().BeEmpty();
        result.PermissionCoverage.MissingScopes.Should().BeEmpty();
        result.PermissionCoverage.UnknownScopes.Should().BeEquivalentTo(GraphScopeCatalog.CapabilityEvaluationScopes);
    }

    [Theory]
    [InlineData(ConnectionHealthStatus.ConsentRequired, ConnectionState.ConsentRequired)]
    [InlineData(ConnectionHealthStatus.PermissionIncomplete, ConnectionState.PermissionIncomplete)]
    [InlineData(ConnectionHealthStatus.ConsentRevoked, ConnectionState.ConsentRevoked)]
    [InlineData(ConnectionHealthStatus.ConnectionFailed, ConnectionState.ConnectionFailed)]
    [InlineData(ConnectionHealthStatus.TemporarilyUnavailable, ConnectionState.TemporarilyUnavailable)]
    public async Task Baseline_failure_preserves_its_category_without_scope_probes(
        ConnectionHealthStatus baselineStatus,
        string expectedStatus)
    {
        var scopeReader = new FixtureScopeAvailabilityReader(_ => ScopeAvailability.MissingConsent);
        var onboarding = new RecordingOnboardingService();
        var service = CreateService(baselineStatus, scopeReader, onboarding, "baseline_problem");

        var result = await service.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.Status.Should().Be(expectedStatus);
        result.Health.Problem.Should().Be("baseline_problem");
        result.PermissionCoverage.Should().BeNull();
        scopeReader.ReadCalls.Should().Be(0);
        onboarding.RecordedStatus.Should().Be(expectedStatus);
        onboarding.RecordedFailureCategory.Should().Be("baseline_problem");
        result.Health.LastVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Baseline_only_mode_does_not_probe_or_claim_permission_coverage()
    {
        var scopeReader = new FixtureScopeAvailabilityReader(_ => ScopeAvailability.MissingConsent);
        var onboarding = new RecordingOnboardingService();
        var service = CreateService(ConnectionHealthStatus.Connected, scopeReader, onboarding);

        var result = await service.VerifyAsync(Context(), includePermissionCoverage: false);

        result.Health.Status.Should().Be(ConnectionState.Connected);
        result.PermissionCoverage.Should().BeNull();
        scopeReader.ReadCalls.Should().Be(0);
        onboarding.RecordedScopes.Should().Contain("User.Read");
    }

    [Fact]
    public async Task Comprehensive_probe_timeout_returns_unknown_coverage_without_cancelling_the_caller()
    {
        var onboarding = new RecordingOnboardingService();
        var reader = new ConnectionVerificationService(
            new FixtureConnectionHealthReader(new ConnectionHealthReadResult(ConnectionHealthStatus.Connected, ["User.Read"])),
            new BlockingScopeAvailabilityReader(),
            onboarding,
            TimeSpan.FromMilliseconds(25));

        var result = await reader.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.Status.Should().Be(ConnectionState.TemporarilyUnavailable);
        result.PermissionCoverage!.UnknownScopes.Should().BeEquivalentTo(GraphScopeCatalog.CapabilityEvaluationScopes);
        onboarding.RecordedStatus.Should().Be(ConnectionState.TemporarilyUnavailable);
        onboarding.RecordedFailureCategory.Should().Be("temporarily_unavailable");
    }

    [Fact]
    public async Task Last_verified_time_is_persisted_only_when_an_observed_check_is_recorded()
    {
        var repository = new RecordingOnboardingRepository();
        var onboarding = new OnboardingService(repository);
        var verifier = new ConnectionVerificationService(
            new FixtureConnectionHealthReader(new ConnectionHealthReadResult(ConnectionHealthStatus.Connected, ["User.Read"])),
            new FixtureScopeAvailabilityReader(_ => ScopeAvailability.Available),
            onboarding);

        repository.Snapshot!.LastVerifiedAt.Should().BeNull();
        var result = await verifier.VerifyAsync(Context(), includePermissionCoverage: true);

        result.Health.LastVerifiedAt.Should().NotBeNull();
        repository.Snapshot!.LastVerifiedAt.Should().NotBeNull();
        repository.UpdateCalls.Should().Be(1);
    }

    private static ConnectionVerificationService CreateService(
        ConnectionHealthStatus baselineStatus,
        FixtureScopeAvailabilityReader scopeReader,
        RecordingOnboardingService onboarding,
        string? problemCategory = null) =>
        new(
            new FixtureConnectionHealthReader(new ConnectionHealthReadResult(baselineStatus, ["User.Read"], problemCategory)),
            scopeReader,
            onboarding);

    private static WorkspaceContext Context() =>
        new(
            new AuthenticatedUser(TenantId, Guid.NewGuid(), "admin@example.com", "Admin", "Member"),
            new WorkspaceMembership(WorkspaceId, "Customer workspace"));

    private sealed class FixtureConnectionHealthReader(ConnectionHealthReadResult result) : IConnectionHealthReader
    {
        public Task<ConnectionHealthReadResult> ReadAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
            Task.FromResult(result);
    }

    private sealed class FixtureScopeAvailabilityReader(Func<string, ScopeAvailability> classify) : IDelegatedScopeAvailabilityReader
    {
        public int ReadCalls { get; private set; }

        public Task<IReadOnlyCollection<DelegatedScopeResult>> ReadAsync(
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            IReadOnlyCollection<DelegatedScopeResult> results = scopes
                .Select(scope =>
                {
                    var status = classify(scope);
                    return new DelegatedScopeResult(scope, status, status switch
                    {
                        ScopeAvailability.MissingConsent => "consent_required",
                        ScopeAvailability.Unknown => "temporarily_unavailable",
                        _ => null
                    });
                })
                .ToArray();
            return Task.FromResult(results);
        }
    }

    private sealed class BlockingScopeAvailabilityReader : IDelegatedScopeAvailabilityReader
    {
        public async Task<IReadOnlyCollection<DelegatedScopeResult>> ReadAsync(
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return [];
        }
    }

    private sealed class RecordingOnboardingService : IOnboardingService
    {
        public string? RecordedStatus { get; private set; }
        public string? RecordedFailureCategory { get; private set; }
        public IReadOnlyCollection<string> RecordedScopes { get; private set; } = [];

        public Task<WorkspaceOnboardingState> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new WorkspaceOnboardingState(workspaceId, ConnectionState.ConsentRequired, null, [], null));

        public Task<WorkspaceOnboardingState> TransitionAsync(Guid workspaceId, string targetState, string? failureCategory = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkspaceOnboardingState> RecordCheckAsync(
            Guid workspaceId,
            string status,
            IReadOnlyCollection<string> scopes,
            string? failureCategory = null,
            CancellationToken cancellationToken = default)
        {
            RecordedStatus = status;
            RecordedFailureCategory = failureCategory;
            RecordedScopes = scopes;
            return Task.FromResult(new WorkspaceOnboardingState(
                workspaceId,
                status,
                DateTimeOffset.UtcNow,
                scopes,
                failureCategory));
        }
    }

    private sealed class RecordingOnboardingRepository : IOnboardingRepository
    {
        public int UpdateCalls { get; private set; }
        public ConnectionSnapshot? Snapshot { get; private set; } =
            new(WorkspaceId, ConnectionState.ConsentRequired, null, [], null);

        public Task<ConnectionSnapshot?> GetConnectionAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Snapshot);

        public Task<ConnectionSnapshot> UpdateConnectionAsync(
            Guid workspaceId,
            string status,
            IReadOnlyCollection<string> consentScopes,
            DateTimeOffset? lastVerifiedAt,
            string? failureCategory,
            CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            Snapshot = new ConnectionSnapshot(workspaceId, status, lastVerifiedAt, consentScopes, failureCategory);
            return Task.FromResult(Snapshot);
        }
    }
}
