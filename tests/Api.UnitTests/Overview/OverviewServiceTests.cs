using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewServiceTests
{
    [Fact]
    public async Task Returns_live_then_cached_data_without_using_cache_for_authorization()
    {
        var now = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var reader = new RecordingOverviewReader
        {
            Result = GraphReadResult<OverviewData>.Succeeded(new OverviewData(120, 90, 30))
        };
        var service = new OverviewService(reader, new StaticAuthorizationReader(AllowedSnapshot()), () => now);
        var context = Context();

        var live = await service.GetAsync(context, CancellationToken.None);
        now = now.AddSeconds(5);
        var cached = await service.GetAsync(context, CancellationToken.None);

        live.Freshness.Should().Be("live");
        cached.Freshness.Should().Be("cached");
        cached.TotalUsers.Should().Be(120);
        reader.Calls.Should().Be(1);
    }

    [Fact]
    public async Task Returns_stale_cached_data_when_refresh_fails_after_cache_lifetime()
    {
        var now = DateTimeOffset.Parse("2026-09-21T10:00:00Z");
        var reader = new RecordingOverviewReader
        {
            Result = GraphReadResult<OverviewData>.Succeeded(new OverviewData(12, 8, 4))
        };
        var service = new OverviewService(reader, new StaticAuthorizationReader(AllowedSnapshot()), () => now, TimeSpan.FromSeconds(10));
        var context = Context();

        await service.GetAsync(context, CancellationToken.None);
        now = now.AddSeconds(11);
        reader.Result = GraphReadResult<OverviewData>.Failed(new GraphOperationResult(false, "throttled", 429));

        var stale = await service.GetAsync(context, CancellationToken.None);

        stale.Freshness.Should().Be("stale");
        stale.TotalUsers.Should().Be(12);
        stale.ErrorCategory.Should().Be("throttled");
    }

    [Fact]
    public async Task Returns_unavailable_and_skips_data_reader_when_permission_is_missing()
    {
        var reader = new RecordingOverviewReader();
        var service = new OverviewService(reader, new StaticAuthorizationReader(GraphAuthorizationSnapshot.Unavailable("consent_required", true)));

        var result = await service.GetAsync(Context(), CancellationToken.None);

        result.Freshness.Should().Be("unavailable");
        result.PermissionHealth.State.Should().Be("consent_required");
        reader.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Includes_permission_health_and_pim_attention_summary()
    {
        var snapshot = GraphAuthorizationSnapshot.Available(
            "user-1",
            ["Directory.Read.All", "User.Read.All"],
            [
                new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/"),
                new DirectoryRoleSnapshot(EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired))]);
        var service = new OverviewService(
            new RecordingOverviewReader { Result = GraphReadResult<OverviewData>.Succeeded(new OverviewData(4, 3, 1)) },
            new StaticAuthorizationReader(snapshot));

        var result = await service.GetAsync(Context(), CancellationToken.None);

        result.PermissionHealth.AllowedCount.Should().BeGreaterThan(0);
        result.PimAttention.RequiresAttention.Should().BeTrue();
        result.LicenseCoverage.Percentage.Should().Be(75);
    }

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member"),
        new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Example", "admin"));

    private static GraphAuthorizationSnapshot AllowedSnapshot() => GraphAuthorizationSnapshot.Available(
        "user-1",
        ["Directory.Read.All", "User.Read.All"],
        [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/")]);

    private sealed class RecordingOverviewReader : IOverviewDataReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<OverviewData> Result { get; set; } = GraphReadResult<OverviewData>.Succeeded(new OverviewData(0, 0, 0));

        public Task<GraphReadResult<OverviewData>> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class StaticAuthorizationReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }
}
