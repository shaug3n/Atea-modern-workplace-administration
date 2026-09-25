using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Exports;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Exports;

public sealed class CsvExportServiceTests
{
    private static readonly WorkspaceContext Context = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "reader@example.com", "Reader", "Member"),
        new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(Guid.NewGuid(), "Workspace", "member", ModuleKeys: ["users", "devices", "licenses"]));

    [Fact]
    public async Task Users_export_follows_all_pages_sanitizes_formula_cells_and_audits_request()
    {
        var users = new Users([new UserSummary("user-1", "=HYPERLINK(\"evil\")", "+admin@example.com", null)], "next");
        var audit = new Audit();
        var service = new CsvExportService(users, new Devices(), new Licenses(), new Assignees(), audit);

        var result = await service.ExportUsersAsync(Context, new UserSearchRequest(Search: "Ada"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Csv.Should().Contain("\"'=HYPERLINK(\"\"evil\"\")\"");
        result.Csv.Should().Contain("'+admin@example.com");
        result.RowCount.Should().Be(2);
        users.Tokens.Should().Equal((string?)null, "next");
        users.Searches.Should().OnlyContain(search => search == "Ada");
        audit.Events.Should().ContainSingle().Which.Action.Should().Be("exports.users");
    }

    [Fact]
    public async Task Graph_error_on_later_page_returns_no_CSV_and_audits_failure()
    {
        var users = new Users([new UserSummary("user-1", "Ada", null, null)], "next") { FailSecond = true };
        var audit = new Audit();
        var result = await new CsvExportService(users, new Devices(), new Licenses(), new Assignees(), audit)
            .ExportUsersAsync(Context, new UserSearchRequest(), CancellationToken.None);

        result.Error.Should().Be("throttled");
        result.Csv.Should().BeNull();
        audit.Events.Should().ContainSingle().Which.Outcome.Should().Be("failed");
    }

    [Fact]
    public async Task Csv_formula_guard_covers_invisible_prefixes_before_a_formula()
    {
        var users = new Users([new UserSummary("user-1", "\uFEFF=1+1", "\t@SUM(1)", null)], null);
        var result = await new CsvExportService(users, new Devices(), new Licenses(), new Assignees(), new Audit())
            .ExportUsersAsync(Context, new UserSearchRequest(), CancellationToken.None);

        result.Csv.Should().Contain("\"'\uFEFF=1+1\"");
        result.Csv.Should().Contain("\"'\t@SUM(1)\"");
    }

    [Fact]
    public async Task Users_export_stops_at_ten_thousand_and_reports_truncation_only_when_another_row_exists()
    {
        var audit = new Audit();
        var users = new ManyUsers();
        var result = await new CsvExportService(users, new Devices(), new Licenses(), new Assignees(), audit)
            .ExportUsersAsync(Context, new UserSearchRequest(AccountStatus: "enabled"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.RowCount.Should().Be(10_000);
        result.Truncated.Should().BeTrue();
        users.Calls.Should().Be(101);
        users.Statuses.Should().OnlyContain(status => status == "enabled");
        result.Csv!.Split('\n').Length.Should().Be(10_002);
        audit.Events.Should().ContainSingle().Which.SafeMetadataJson.Should().Contain("\"truncated\":true");
    }

    [Fact]
    public async Task License_inventory_export_uses_purchased_and_current_search_filter()
    {
        var licenses = new Licenses("E3");
        var result = await new CsvExportService(new Users([], null), new Devices(), licenses, new Assignees(), new Audit())
            .ExportLicensesAsync(Context, new LicenseOverviewRequest(Search: "E3"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Csv.Should().Contain("\"10\",\"7\",\"3\"");
        licenses.Search.Should().Be("E3");
    }

    [Fact]
    public async Task Device_export_contains_only_allowlisted_inventory_fields()
    {
        var result = await new CsvExportService(new Users([], null), new Devices(true), new Licenses(), new Assignees(), new Audit())
            .ExportDevicesAsync(Context, new DeviceSearchRequest(Search: "WIN", ComplianceState: "compliant"), CancellationToken.None);

        result.Error.Should().BeNull();
        result.Csv.Should().Contain("\"WIN-01\"");
        result.Csv.Should().NotContain("BitLocker").And.NotContain("LAPS").And.NotContain("recoveryKey").And.NotContain("password");
    }

    private sealed class Users(IReadOnlyList<UserSummary> first, string? next) : IUserQueryService
    {
        public List<string?> Tokens { get; } = [];
        public List<string?> Searches { get; } = [];
        public bool FailSecond { get; init; }
        public Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken)
        {
            Tokens.Add(request.ContinuationToken);
            Searches.Add(request.Search);
            var second = Tokens.Count > 1;
            return Task.FromResult(new UserDirectoryResponse(second ? [new UserSummary("user-2", "Grace", "grace@example.com", null)] : first, second ? null : next, DateTimeOffset.UtcNow, "fresh", second && FailSecond,
                second && FailSecond ? new UserDirectoryError("throttled", "Graph failed") : null));
        }
    }
    private sealed class ManyUsers : IUserQueryService
    {
        public int Calls { get; private set; }
        public List<string?> Statuses { get; } = [];
        public Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            Statuses.Add(request.AccountStatus);
            var count = Calls <= 100 ? 100 : 1;
            return Task.FromResult(new UserDirectoryResponse(Enumerable.Range(1, count).Select(index => new UserSummary($"user-{Calls}-{index}", "Ada", null, null)).ToArray(), Calls <= 100 ? $"page-{Calls + 1}" : null, DateTimeOffset.UtcNow, "fresh", false));
        }
    }
    private sealed class Devices(bool hasItem = false) : IDeviceService
    {
        public Task<DeviceDirectoryResponse> SearchAsync(WorkspaceContext context, DeviceSearchRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new DeviceDirectoryResponse(hasItem ? [new ManagedDeviceSummary("device-1", "WIN-01", "Windows", "11", "compliant", null, null, null, null, null, null, null, null)] : [], hasItem ? 1 : 0,
                DateTimeOffset.UtcNow, "live", false, new DeviceAccess("allowed")));
    }
    private sealed class Licenses(string? expectedSearch = null) : ILicenseOverviewService
    {
        public string? Search { get; private set; }
        public Task<LicenseOverviewResponse> GetAsync(WorkspaceContext context, LicenseOverviewRequest request, CancellationToken cancellationToken)
        {
            Search = request.Search;
            if (expectedSearch is null) throw new NotImplementedException();
            return Task.FromResult(new LicenseOverviewResponse([new LicenseOverviewItem("sku-1", "E3", "E3", 7, 3, 10)], 1, 1, 100,
                DateTimeOffset.UtcNow, "live", false, new LicenseOverviewAccess("allowed")));
        }
    }
    private sealed class Assignees : ILicenseAssigneeService
    {
        public Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, string skuId, int pageSize, string? continuationToken, CancellationToken cancellationToken) => throw new NotImplementedException();
    }
    private sealed class Audit : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) { Events.Add(auditEvent); return Task.CompletedTask; }
    }
}
