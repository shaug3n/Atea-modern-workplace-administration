using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewServiceTests
{
    private static readonly DateTimeOffset InitialTime = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
    private static readonly IReadOnlyCollection<string> AllModules = ["users", "licenses", "devices"];

    [Fact]
    public async Task Authorized_user_and_license_sections_are_gated_independently_by_capability_module_and_query_scope()
    {
        var graph = new RecordingOverviewGraphReader();
        var activity = new RecordingActivityReader();
        var authorization = new StaticAuthorizationReader(AllowedSnapshot(["User.Read.All"]));
        var service = CreateService(graph, activity, authorization);

        var userOnly = await service.GetAsync(Context(), ["users", "licenses"], CancellationToken.None);

        userOnly.Users.State.Should().Be("fresh");
        userOnly.Users.Data!.TotalUsers.Should().Be(120);
        userOnly.LicenseCoverage.State.Should().Be("restricted");
        graph.UserScopes.Should().Equal("User.Read.All");
        graph.LicenseCalls.Should().Be(0);

        authorization.Snapshot = AllowedSnapshot(["Directory.Read.All"]);
        var licenseOnly = await service.GetAsync(Context(), ["licenses"], CancellationToken.None);

        licenseOnly.Users.State.Should().Be("restricted");
        licenseOnly.Users.Data.Should().BeNull();
        licenseOnly.LicenseCoverage.State.Should().Be("fresh");
        licenseOnly.LicenseCoverage.Data!.Percentage.Should().Be(75);
        graph.UserCalls.Should().Be(1);
        graph.LicenseCalls.Should().Be(1);
    }

    [Fact]
    public async Task Scoped_eligible_PIM_and_unknown_authorization_never_return_tenant_totals()
    {
        var graph = new RecordingOverviewGraphReader();
        var service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(ScopedSnapshot()));

        var scoped = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        scoped.Users.State.Should().Be("restricted");
        scoped.Users.Data.Should().BeNull();
        scoped.LicenseCoverage.Data.Should().BeNull();

        service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(EligiblePimSnapshot()));
        var eligible = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        eligible.Users.Data.Should().BeNull();
        eligible.LicenseCoverage.Data.Should().BeNull();

        service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(GraphAuthorizationSnapshot.Unavailable("snapshot_unavailable")));
        var unknown = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        unknown.Users.State.Should().Be("unavailable");
        unknown.Users.Data.Should().BeNull();
        unknown.LicenseCoverage.Data.Should().BeNull();
        graph.UserCalls.Should().Be(0);
        graph.LicenseCalls.Should().Be(0);
    }

    [Fact]
    public async Task Audit_access_remains_independent_when_the_Graph_snapshot_is_unavailable()
    {
        var activity = new RecordingActivityReader();
        var service = CreateService(
            new RecordingOverviewGraphReader(),
            activity,
            new StaticAuthorizationReader(GraphAuthorizationSnapshot.Unavailable("consent_required")));

        var result = await service.GetAsync(Context(), AllModules, CancellationToken.None);

        result.Users.Data.Should().BeNull();
        result.LicenseCoverage.Data.Should().BeNull();
        result.Activity.State.Should().Be("empty");
        result.EffectiveCapabilities.Should().ContainSingle(x => x.Capability == Capability.AuditView && x.State == CapabilityState.Allowed);
        activity.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Restricted_empty_and_failed_audit_states_are_distinct()
    {
        var activity = new RecordingActivityReader();
        var service = CreateService(new RecordingOverviewGraphReader(), activity,
            new StaticAuthorizationReader(AllowedSnapshot()));
        var restricted = await service.GetAsync(Context("member"), AllModules, CancellationToken.None);
        restricted.Activity.State.Should().Be("restricted");
        activity.Calls.Should().Be(0);

        service = CreateService(new RecordingOverviewGraphReader(), activity,
            new StaticAuthorizationReader(AllowedSnapshot()));
        activity.Items = [new OverviewActivityItem("user.updated", "success", InitialTime)];
        var nonempty = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        nonempty.Activity.State.Should().Be("fresh");
        nonempty.Activity.Data!.Items.Should().ContainSingle();

        service = CreateService(new RecordingOverviewGraphReader(), activity,
            new StaticAuthorizationReader(AllowedSnapshot()));
        activity.Throw = true;
        var failed = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        failed.Activity.State.Should().Be("unavailable");
        failed.Activity.ErrorCategory.Should().Be("source_unavailable");
        failed.Activity.Data.Should().BeNull();
    }

    [Fact]
    public async Task Returns_fresh_per_source_data_within_the_30_second_window()
    {
        var now = InitialTime;
        var graph = new RecordingOverviewGraphReader();
        var activity = new RecordingActivityReader { Items = [new OverviewActivityItem("saved", "success", now)] };
        var service = CreateService(graph, activity, new StaticAuthorizationReader(AllowedSnapshot()), () => now);

        var first = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        now = now.AddSeconds(30);
        var boundary = await service.GetAsync(Context(), AllModules, CancellationToken.None);

        first.Users.State.Should().Be("fresh");
        boundary.Users.State.Should().Be("fresh");
        boundary.Users.FetchedAt.Should().Be(InitialTime);
        graph.UserCalls.Should().Be(1);
        graph.LicenseCalls.Should().Be(1);
        activity.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Preserves_each_original_fetch_time_on_stale_fallback()
    {
        var now = InitialTime;
        var graph = new RecordingOverviewGraphReader();
        var activity = new RecordingActivityReader { Items = [new OverviewActivityItem("saved", "success", now)] };
        var service = CreateService(graph, activity, new StaticAuthorizationReader(AllowedSnapshot()), () => now);
        var initial = await service.GetAsync(Context(), AllModules, CancellationToken.None);
        now = now.AddSeconds(31);
        graph.FailUsers = true;
        graph.FailLicenses = true;
        activity.Throw = true;

        var stale = await service.GetAsync(Context(), AllModules, CancellationToken.None);

        stale.Users.State.Should().Be("stale");
        stale.Users.FetchedAt.Should().Be(initial.Users.FetchedAt);
        stale.Users.PartialData.Should().BeTrue();
        stale.LicenseCoverage.State.Should().Be("stale");
        stale.LicenseCoverage.FetchedAt.Should().Be(initial.LicenseCoverage.FetchedAt);
        stale.Activity.State.Should().Be("stale");
        stale.Activity.FetchedAt.Should().Be(initial.Activity.FetchedAt);
    }

    [Fact]
    public async Task Denied_source_does_not_reuse_cached_values_after_capability_or_module_revocation()
    {
        var authorization = new StaticAuthorizationReader(AllowedSnapshot());
        var graph = new RecordingOverviewGraphReader();
        var service = CreateService(graph, new RecordingActivityReader(), authorization);
        var context = Context();
        (await service.GetAsync(context, AllModules, CancellationToken.None)).Users.Data.Should().NotBeNull();

        var moduleRevoked = await service.GetAsync(context, ["licenses", "devices"], CancellationToken.None);
        moduleRevoked.Users.State.Should().Be("restricted");
        moduleRevoked.Users.Data.Should().BeNull();

        authorization.Snapshot = GraphAuthorizationSnapshot.Unavailable("snapshot_unavailable");
        var capabilityRevoked = await service.GetAsync(context, AllModules, CancellationToken.None);
        capabilityRevoked.Users.Data.Should().BeNull();
        capabilityRevoked.LicenseCoverage.Data.Should().BeNull();
        graph.UserCalls.Should().Be(1);
        graph.LicenseCalls.Should().Be(1);
    }

    [Fact]
    public async Task Overview_cache_isolated_by_workspace_tenant_requester_and_source()
    {
        var graph = new RecordingOverviewGraphReader();
        var activity = new RecordingActivityReader();
        var authorization = new StaticAuthorizationReader(AllowedSnapshot());
        var service = CreateService(graph, activity, authorization);
        await service.GetAsync(Context(), AllModules, CancellationToken.None);

        var otherWorkspace = Context() with
        {
            Membership = Context().Membership with { WorkspaceId = Guid.NewGuid() }
        };
        var otherTenant = Context() with
        {
            User = Context().User with { TenantId = Guid.NewGuid() }
        };
        var otherRequester = Context() with
        {
            User = Context().User with { ObjectId = Guid.NewGuid() }
        };
        await service.GetAsync(otherWorkspace, AllModules, CancellationToken.None);
        await service.GetAsync(otherTenant, AllModules, CancellationToken.None);
        await service.GetAsync(otherRequester, AllModules, CancellationToken.None);
        await service.GetAsync(Context("customer_admin"), AllModules, CancellationToken.None);

        authorization.Snapshot = AllowedSnapshot(["User.Read.All"]);
        await service.GetAsync(Context(), AllModules, CancellationToken.None);

        graph.UserCalls.Should().Be(5);
        graph.LicenseCalls.Should().Be(4);
        activity.Calls.Should().Be(5);
    }

    [Fact]
    public async Task Failure_in_one_source_does_not_hide_or_refresh_another_source()
    {
        var now = InitialTime;
        var graph = new RecordingOverviewGraphReader();
        var activity = new RecordingActivityReader { Items = [new OverviewActivityItem("saved", "success", now)] };
        var service = CreateService(graph, activity, new StaticAuthorizationReader(AllowedSnapshot()), () => now);
        await service.GetAsync(Context(), AllModules, CancellationToken.None);
        now = now.AddSeconds(31);
        graph.FailUsers = true;

        var result = await service.GetAsync(Context(), AllModules, CancellationToken.None);

        result.Users.State.Should().Be("stale");
        result.Users.Data!.TotalUsers.Should().Be(120);
        result.LicenseCoverage.State.Should().Be("fresh");
        result.LicenseCoverage.Data!.AssignedUsers.Should().Be(90);
        result.Activity.State.Should().Be("fresh");
        result.Users.Data.TotalUsers.Should().NotBe(0);
        graph.LicenseCalls.Should().Be(2);
        activity.Calls.Should().Be(2);
    }

    [Fact]
    public async Task Mismatched_assigned_user_count_is_unavailable_and_never_clamped()
    {
        var graph = new RecordingOverviewGraphReader { LicenseCounts = new OverviewLicenseCounts(2, 3) };
        var service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(AllowedSnapshot(["Directory.Read.All"])));

        var result = await service.GetAsync(Context(), ["licenses"], CancellationToken.None);

        result.LicenseCoverage.State.Should().Be("unavailable");
        result.LicenseCoverage.Data.Should().BeNull();
        result.LicenseCoverage.ErrorCategory.Should().Be("invalid_response");
    }

    [Fact]
    public async Task Failed_source_reads_are_unavailable_without_zero_shaped_data()
    {
        var graph = new RecordingOverviewGraphReader { FailUsers = true, FailLicenses = true };
        var service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(AllowedSnapshot()));

        var result = await service.GetAsync(Context(), AllModules, CancellationToken.None);

        result.Users.State.Should().Be("unavailable");
        result.Users.Data.Should().BeNull();
        result.LicenseCoverage.State.Should().Be("unavailable");
        result.LicenseCoverage.Data.Should().BeNull();
    }

    [Fact]
    public async Task Unavailable_required_query_scope_is_not_reported_as_an_empty_user_total()
    {
        var graph = new RecordingOverviewGraphReader { UserErrorCategory = "required_scope_unavailable" };
        var service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(AllowedSnapshot()));

        var result = await service.GetAsync(Context(), ["users"], CancellationToken.None);

        result.Users.State.Should().Be("unavailable");
        result.Users.Data.Should().BeNull();
        result.Users.ErrorCategory.Should().Be("required_scope_unavailable");
        graph.UserCalls.Should().Be(1);
    }

    [Fact]
    public async Task Cancellation_propagates_from_source_reads()
    {
        var graph = new RecordingOverviewGraphReader { ThrowOnCancellation = true };
        var service = CreateService(graph, new RecordingActivityReader(),
            new StaticAuthorizationReader(AllowedSnapshot()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var act = () => service.GetAsync(Context(), AllModules, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    private static OverviewService CreateService(
        RecordingOverviewGraphReader graph,
        RecordingActivityReader activity,
        StaticAuthorizationReader authorization,
        Func<DateTimeOffset>? utcNow = null,
        OverviewDataCache? cache = null) =>
        new(graph, activity, authorization, utcNow, cache: cache ?? new OverviewDataCache(),
            logger: NullLogger<OverviewService>.Instance);

    private static WorkspaceContext Context(string role = "workspace_owner") => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member"),
        new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", role));

    private static GraphAuthorizationSnapshot AllowedSnapshot(IReadOnlyCollection<string>? scopes = null) => GraphAuthorizationSnapshot.Available(
        "user-1",
        scopes ?? ["Directory.Read.All", "User.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);

    private static GraphAuthorizationSnapshot ScopedSnapshot() => GraphAuthorizationSnapshot.Available(
        "user-1",
        ["Directory.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/administrativeUnits/123")]);

    private static GraphAuthorizationSnapshot EligiblePimSnapshot() => GraphAuthorizationSnapshot.Available(
        "user-1",
        ["Directory.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired))]);

    private sealed class RecordingOverviewGraphReader : IOverviewGraphReader
    {
        public int UserCalls { get; private set; }
        public int LicenseCalls { get; private set; }
        public List<string> UserScopes { get; } = [];
        public bool FailUsers { get; set; }
        public bool FailLicenses { get; set; }
        public string? UserErrorCategory { get; set; }
        public bool ThrowOnCancellation { get; set; }
        public OverviewLicenseCounts LicenseCounts { get; set; } = new(120, 90);

        public Task<GraphReadResult<int>> ReadUserCountAsync(WorkspaceContext context, string delegatedScope, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UserCalls++;
            UserScopes.Add(delegatedScope);
            return Task.FromResult(FailUsers || UserErrorCategory is not null
                ? GraphReadResult<int>.Failed(new GraphOperationResult(false, UserErrorCategory ?? "throttled"))
                : GraphReadResult<int>.Succeeded(120));
        }

        public Task<GraphReadResult<OverviewLicenseCounts>> ReadLicenseCountsAsync(WorkspaceContext context, CancellationToken cancellationToken)
        {
            if (ThrowOnCancellation) cancellationToken.ThrowIfCancellationRequested();
            LicenseCalls++;
            return Task.FromResult(FailLicenses
                ? GraphReadResult<OverviewLicenseCounts>.Failed(new GraphOperationResult(false, "throttled"))
                : GraphReadResult<OverviewLicenseCounts>.Succeeded(LicenseCounts));
        }
    }

    private sealed class RecordingActivityReader : IOverviewActivityReader
    {
        public int Calls { get; private set; }
        public bool Throw { get; set; }
        public IReadOnlyList<OverviewActivityItem> Items { get; set; } = [];

        public Task<IReadOnlyList<OverviewActivityItem>> ReadRecentAsync(WorkspaceContext context, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            if (Throw) throw new InvalidOperationException("database details must not escape");
            return Task.FromResult(Items);
        }
    }

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public GraphAuthorizationSnapshot Snapshot { get; set; } = snapshot;
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Snapshot);
        }
    }
}
