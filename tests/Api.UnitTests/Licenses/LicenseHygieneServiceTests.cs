using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Licenses;

public sealed class LicenseHygieneServiceTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkspaceContext Context = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com", "Reader", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Workspace", "member", ModuleKeys: ["license-hygiene"]));

    [Fact]
    public async Task Disabled_multi_sku_user_produces_one_review_row()
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded(
            [
                new("11111111-1111-1111-1111-111111111111", "ENTERPRISEPACK", "Office 365 E3", 7, 3, 10)
            ])
        };
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", "Disabled User", "disabled@example.com", false,
            [
                "11111111-1111-1111-1111-111111111111",
                "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"
            ])
        ]));

        var response = await Service(inventory, users, AllowedSnapshot()).RefreshAsync(Context, CancellationToken.None);

        response.DisabledAccounts.Should().ContainSingle();
        response.DisabledAccounts[0].Id.Should().Be("user-1");
        response.DisabledAccounts[0].AssignedLicenses.Should().HaveCount(2);
        response.DisabledAccounts[0].AssignedLicenses[0].DisplayName.Should().Be("Office 365 E3");
        response.DisabledAccounts[0].AssignedLicenses[1].SkuId.Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        response.DisabledAccounts[0].AssignedLicenses[1].PartNumber.Should().Be("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        response.Coverage.RecordsAssessed.Should().Be(1);
    }

    [Fact]
    public async Task Missing_evidence_is_counted_without_inventing_findings()
    {
        var users = new RecordingUserReader(Completed(
        [
            new("missing-assignments", "Missing assignments", null, false, null),
            new("missing-status", "Missing status", null, null, []),
            new("malformed-assignments", "Malformed assignments", null, false, null, AssignmentEvidenceMalformed: true),
            new("valid-empty", "Valid empty", null, true, [])
        ]));

        var response = await Service(new RecordingInventoryReader(), users, AllowedSnapshot())
            .RefreshAsync(Context, CancellationToken.None);

        response.Coverage.RecordsAssessed.Should().Be(4);
        response.Coverage.MissingEvidenceRecords.Should().Be(3);
        response.DisabledAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Unknown_assignment_sku_is_preserved()
    {
        var unknownId = "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb";
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", null, "disabled@example.com", false, [unknownId])
        ]));

        var response = await Service(new RecordingInventoryReader(), users, AllowedSnapshot())
            .RefreshAsync(Context, CancellationToken.None);

        response.DisabledAccounts.Should().ContainSingle();
        response.DisabledAccounts[0].AssignedLicenses.Should().ContainSingle()
            .Which.Should().Be(new LicenseHygieneAssignedSku(unknownId, unknownId, unknownId));
    }

    [Fact]
    public async Task Completed_empty_differs_from_unavailable()
    {
        var completed = await Service(new RecordingInventoryReader(), new RecordingUserReader(Completed([])), AllowedSnapshot())
            .RefreshAsync(Context, CancellationToken.None);
        var failed = await Service(
            new RecordingInventoryReader(),
            new RecordingUserReader(FailedScan("not_authorized", 403)),
            AllowedSnapshot()).RefreshAsync(Context, CancellationToken.None);

        completed.UserEvidence.Freshness.Should().Be("live");
        completed.Coverage.Completed.Should().BeTrue();
        completed.DisabledAccounts.Should().BeEmpty();
        failed.UserEvidence.Freshness.Should().Be("unavailable");
        failed.Coverage.Completed.Should().BeFalse();
        failed.UserEvidence.Error!.Category.Should().Be("not_authorized");
        failed.UserEvidence.Error.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Inventory_and_user_source_failures_are_independent()
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(
                new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(13)))
        };
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", "Disabled", null, false, ["cccccccc-cccc-cccc-cccc-cccccccccccc"])
        ]));

        var response = await Service(inventory, users, AllowedSnapshot()).RefreshAsync(Context, CancellationToken.None);

        response.Inventory.Freshness.Should().Be("unavailable");
        response.Inventory.Error!.Category.Should().Be("throttled");
        response.Inventory.Error.StatusCode.Should().Be(429);
        response.Inventory.Error.RetryAfterSeconds.Should().Be(13);
        response.UserEvidence.Freshness.Should().Be("live");
        response.DisabledAccounts.Should().ContainSingle();
        response.CapacityItems.Should().BeEmpty();
    }

    [Fact]
    public async Task User_source_failure_preserves_verified_inventory()
    {
        var inventory = new RecordingInventoryReader
        {
            Result = GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded(
            [
                new("11111111-1111-1111-1111-111111111111", "ENTERPRISEPACK", "Office 365 E3", 7, 3, 10)
            ])
        };
        var users = new RecordingUserReader(FailedScan("throttled", 429));

        var response = await Service(inventory, users, AllowedSnapshot()).RefreshAsync(Context, CancellationToken.None);

        response.Inventory.Freshness.Should().Be("live");
        response.CapacityItems.Should().ContainSingle().Which.Available.Should().Be(3);
        response.UserEvidence.Freshness.Should().Be("unavailable");
        response.UserEvidence.Error!.Category.Should().Be("throttled");
        response.UserEvidence.Error.StatusCode.Should().Be(429);
        response.DisabledAccounts.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_assignment_sku_is_missing_evidence_and_never_a_finding()
    {
        var users = new RecordingUserReader(Completed(
        [
            new("user-1", "Disabled", null, false, ["not-a-sku-guid"])
        ]));

        var response = await Service(new RecordingInventoryReader(), users, AllowedSnapshot())
            .RefreshAsync(Context, CancellationToken.None);

        response.DisabledAccounts.Should().BeEmpty();
        response.Coverage.MissingEvidenceRecords.Should().Be(1);
    }

    [Fact]
    public async Task Later_user_page_failure_preserves_verified_observations()
    {
        var users = new RecordingUserReader(new LicenseHygieneUserScanResult(
        [
            new("user-1", "Verified", null, false, ["dddddddd-dddd-dddd-dddd-dddddddddddd"])
        ],
        PagesRead: 1,
        Completed: false,
        StopReason: "throttled",
        Error: new GraphOperationResult(false, "throttled", 429, TimeSpan.FromSeconds(9)),
        StartedAt: ObservedAt,
        CompletedAt: ObservedAt.AddSeconds(2)));

        var response = await Service(new RecordingInventoryReader(), users, AllowedSnapshot())
            .RefreshAsync(Context, CancellationToken.None);

        response.UserEvidence.Freshness.Should().Be("live");
        response.UserEvidence.PartialData.Should().BeTrue();
        response.UserEvidence.Error!.Category.Should().Be("throttled");
        response.UserEvidence.Error.RetryAfterSeconds.Should().Be(9);
        response.DisabledAccounts.Should().ContainSingle().Which.Id.Should().Be("user-1");
        response.Coverage.Completed.Should().BeFalse();
        response.Coverage.StopReason.Should().Be("throttled");
    }

    [Fact]
    public async Task Denied_or_unavailable_capability_skips_data_reads()
    {
        foreach (var snapshot in new[]
        {
            GraphAuthorizationSnapshot.Unavailable("consent_required", true),
            GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"], [])
        })
        {
            var inventory = new RecordingInventoryReader();
            var users = new RecordingUserReader(Completed([]));

            var response = await Service(inventory, users, snapshot).RefreshAsync(Context, CancellationToken.None);

            inventory.Calls.Should().Be(0);
            users.Calls.Should().Be(0);
            response.Access.State.Should().NotBe(CapabilityState.Allowed);
            response.CapacityItems.Should().BeEmpty();
            response.DisabledAccounts.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task Caller_cancellation_propagates()
    {
        var source = new CancellationTokenSource();
        source.Cancel();
        var users = new RecordingUserReader(Completed([])) { Exception = new OperationCanceledException(source.Token) };

        var act = () => Service(new RecordingInventoryReader(), users, AllowedSnapshot())
            .RefreshAsync(Context, source.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task Read_only_capability_is_authorized_before_either_data_source()
    {
        var readOnlySnapshot = GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"],
        [
            new DirectoryRoleSnapshot(
                EntraRoleCatalog.LicenseAdministratorTemplateId,
                "License Administrator",
                DirectoryRoleAssignmentState.Active,
                "/administrativeUnits/unit-1")
        ]);
        var inventory = new RecordingInventoryReader();
        var users = new RecordingUserReader(Completed([]));

        var response = await Service(inventory, users, readOnlySnapshot).RefreshAsync(Context, CancellationToken.None);

        response.Access.State.Should().Be(CapabilityState.ReadOnly);
        inventory.Calls.Should().Be(1);
        users.Calls.Should().Be(1);
    }

    private static LicenseHygieneService Service(
        RecordingInventoryReader inventory,
        RecordingUserReader users,
        GraphAuthorizationSnapshot snapshot) =>
        new(inventory, users, new SnapshotReader(snapshot), utcNow: () => ObservedAt);

    private static GraphAuthorizationSnapshot AllowedSnapshot() =>
        GraphAuthorizationSnapshot.Available("reader", ["Directory.Read.All"],
        [
            new DirectoryRoleSnapshot(
                EntraRoleCatalog.GlobalReaderTemplateId,
                "Global Reader",
                DirectoryRoleAssignmentState.Active,
                "/")
        ]);

    private static LicenseHygieneUserScanResult Completed(IReadOnlyList<LicenseHygieneUserObservation> users) =>
        new(users, 1, true, "completed", null, ObservedAt, ObservedAt.AddSeconds(1));

    private static LicenseHygieneUserScanResult FailedScan(string category, int statusCode) =>
        new([], 0, false, category, new GraphOperationResult(false, category, statusCode), ObservedAt, ObservedAt);

    private sealed class RecordingInventoryReader : ILicenseOverviewReader
    {
        public int Calls { get; private set; }
        public GraphReadResult<IReadOnlyList<LicenseOverviewItem>> Result { get; init; } =
            GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Succeeded([]);

        public Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadAsync(
            WorkspaceContext context,
            LicenseOverviewQuery query,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingUserReader(LicenseHygieneUserScanResult result) : ILicenseHygieneUserReader
    {
        public int Calls { get; private set; }
        public Exception? Exception { get; init; }

        public Task<LicenseHygieneUserScanResult> ScanAsync(WorkspaceContext context, CancellationToken cancellationToken)
        {
            Calls++;
            return Exception is null ? Task.FromResult(result) : Task.FromException<LicenseHygieneUserScanResult>(Exception);
        }
    }

    private sealed class SnapshotReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }
}
