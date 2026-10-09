using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Identity.Client;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class DelegatedScopeAvailabilityReaderTests
{
    [Fact]
    public async Task Classifies_only_positive_missing_consent_as_missing()
    {
        var factory = new ProbeFactory((scope, _) => scope switch
        {
            "User.Read.All" => Task.FromException<GraphClientLease>(new MsalUiRequiredException("consent_required", "Consent is required.")),
            "Group.Read.All" => Task.FromException<GraphClientLease>(new MsalUiRequiredException("invalid_grant", "Conditional Access requires MFA.")),
            "UnknownToken" => Task.FromException<GraphClientLease>(new MsalUiRequiredException("invalid_grant", "The access token is unavailable because consent interaction is pending.")),
            _ => Task.FromResult(NewLease())
        });
        var reader = new DelegatedScopeAvailabilityReader(factory);

        var results = (await reader.ReadAsync(["User.Read.All", "Group.Read.All", "User.Read", "UnknownToken"]))
            .ToDictionary(result => result.Scope);

        results["User.Read.All"].Status.Should().Be(ScopeAvailability.MissingConsent);
        results["Group.Read.All"].Status.Should().Be(ScopeAvailability.Unknown);
        results["User.Read"].Status.Should().Be(ScopeAvailability.Available);
        results["UnknownToken"].Status.Should().Be(ScopeAvailability.Unknown);
        results["Group.Read.All"].ProblemCategory.Should().Be("temporarily_unavailable");
    }

    [Fact]
    public async Task Never_runs_more_than_four_scope_probes_concurrently()
    {
        var factory = new ProbeFactory(async (_, cancellationToken) =>
        {
            await Task.Delay(20, cancellationToken);
            return NewLease();
        });
        var reader = new DelegatedScopeAvailabilityReader(factory);

        var results = await reader.ReadAsync(Enumerable.Range(0, 12).Select(index => $"Scope.{index}").ToArray());

        results.Should().HaveCount(12);
        factory.MaximumActiveCalls.Should().BeInRange(1, 4);
        factory.ActiveCalls.Should().Be(0);
        factory.DisposedLeases.Should().Be(12);
    }

    [Fact]
    public async Task Propagates_caller_cancellation_and_disposes_acquired_leases()
    {
        var factory = new ProbeFactory(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return NewLease();
        });
        var reader = new DelegatedScopeAvailabilityReader(factory);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var action = () => reader.ReadAsync(["User.Read"], cancellation.Token);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(action);
        factory.ActiveCalls.Should().Be(0);
    }

    [Fact]
    public async Task Unknown_provider_and_temporary_errors_are_unknown_and_no_role_reads_are_made()
    {
        var transport = new RecordingTransport();
        var factory = new ProbeFactory((_, _) =>
            Task.FromResult(new GraphClientLease(transport, ["User.Read"])));
        var reader = new DelegatedScopeAvailabilityReader(factory);

        var results = await reader.ReadAsync(["RoleManagement.Read.Directory", "User.Read"]);

        results.Should().OnlyContain(result => result.Status == ScopeAvailability.Available);
        transport.SendCount.Should().Be(0);
    }

    [Fact]
    public async Task Cancellation_and_unclassified_scope_faults_are_not_claimed_missing()
    {
        var factory = new ProbeFactory((scope, _) => scope switch
        {
            "MfaScope" => Task.FromException<GraphClientLease>(new MsalUiRequiredException("interaction_required", "MFA required.")),
            "FaultScope" => Task.FromException<GraphClientLease>(new HttpRequestException("temporary failure")),
            _ => Task.FromResult(NewLease())
        });
        var reader = new DelegatedScopeAvailabilityReader(factory);

        var results = (await reader.ReadAsync(["MfaScope", "FaultScope"])).ToDictionary(result => result.Scope);

        results["MfaScope"].Status.Should().Be(ScopeAvailability.Unknown);
        results["FaultScope"].Status.Should().Be(ScopeAvailability.Unknown);
        results.Values.Should().OnlyContain(result => result.Status != ScopeAvailability.MissingConsent);
    }

    private static GraphClientLease NewLease(IAsyncDisposable? owner = null) =>
        new(new RecordingTransport(), [], owner);

    private sealed class ProbeFactory(Func<string, CancellationToken, Task<GraphClientLease>> create) : IDelegatedGraphClientFactory
    {
        private int active;
        private int maximumActive;
        private int disposed;

        public int ActiveCalls => Volatile.Read(ref active);
        public int MaximumActiveCalls => Volatile.Read(ref maximumActive);
        public int DisposedLeases => Volatile.Read(ref disposed);

        public async Task<GraphClientLease> CreateForCurrentUserAsync(
            IReadOnlyCollection<string> scopes,
            CancellationToken cancellationToken)
        {
            var current = Interlocked.Increment(ref active);
            UpdateMaximum(current);
            try
            {
                var lease = await create(scopes.Single(), cancellationToken);
                return lease with { OwnedResource = new LeaseTracker(this) };
            }
            catch
            {
                Interlocked.Decrement(ref active);
                throw;
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var observed = Volatile.Read(ref maximumActive);
                if (value <= observed || Interlocked.CompareExchange(ref maximumActive, value, observed) == observed)
                    return;
            }
        }

        private sealed class LeaseTracker(ProbeFactory owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync()
            {
                Interlocked.Increment(ref owner.disposed);
                Interlocked.Decrement(ref owner.active);
                return ValueTask.CompletedTask;
            }
        }
    }

    private sealed class RecordingTransport : IGraphTransport
    {
        public int SendCount { get; private set; }
        public IReadOnlyCollection<string> Scopes => [];

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            SendCount++;
            throw new InvalidOperationException("Scope availability probing must not perform Graph role reads.");
        }
    }
}
