using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;

namespace Atea.UnifiedWorkplace.Api.Features.Exports;

public sealed record CsvExportResult(string? Csv, int RowCount, bool Truncated, string? Error = null)
{
    public const int MaximumRows = 10_000;
}

internal sealed record ExportPage<T>(IReadOnlyList<T> Items, string? Next, string? Error = null);

public sealed class CsvExportService(
    IUserQueryService users,
    IDeviceService devices,
    ILicenseOverviewService licenses,
    ILicenseAssigneeService assignees,
    IAuditWriter audit)
{
    public Task<CsvExportResult> ExportUsersAsync(WorkspaceContext context, UserSearchRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(context, "users", "directory", null,
            async token =>
            {
                var result = await users.SearchAsync(context, request with { PageSize = 100, ContinuationToken = token }, cancellationToken);
                return new ExportPage<UserSummary>(result.Items, result.ContinuationToken, result.Error?.Category ?? (result.PartialData ? "partial_data" : null));
            },
            ["Id", "Display name", "User principal name", "Email", "Account enabled", "User type"],
            user => [user.Id, user.DisplayName, user.UserPrincipalName, user.Mail, user.AccountEnabled?.ToString(), user.UserType], cancellationToken);

    public Task<CsvExportResult> ExportDevicesAsync(WorkspaceContext context, DeviceSearchRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(context, "devices", "directory", null,
            async token =>
            {
                var result = await devices.SearchAsync(context, request with { PageSize = 100, ContinuationToken = token }, cancellationToken);
                return new ExportPage<ManagedDeviceSummary>(result.Items, result.ContinuationToken, result.Error?.Category ?? (result.PartialData ? "partial_data" : null));
            },
            ["Id", "Device name", "Operating system", "OS version", "Compliance", "Management state", "Owner type", "Last sync", "User ID", "Serial number", "Manufacturer", "Model"],
            device => [device.Id, device.DeviceName, device.OperatingSystem, device.OsVersion, device.ComplianceState,
                device.ManagementState, device.ManagedDeviceOwnerType, device.LastSyncDateTime?.ToString("O"), device.UserId,
                device.SerialNumber, device.Manufacturer, device.Model], cancellationToken);

    public Task<CsvExportResult> ExportLicensesAsync(WorkspaceContext context, LicenseOverviewRequest request, CancellationToken cancellationToken) =>
        ExecuteAsync(context, "licenses", "inventory", null,
            async token =>
            {
                var page = int.Parse(token ?? "1", System.Globalization.CultureInfo.InvariantCulture);
                var result = await licenses.GetAsync(context, request with { PageSize = 100, Page = page }, cancellationToken);
                return new ExportPage<LicenseOverviewItem>(result.Items, page * 100 < result.Total ? (page + 1).ToString(System.Globalization.CultureInfo.InvariantCulture) : null,
                    result.Error?.Category ?? (result.PartialData ? "partial_data" : null));
            },
            ["SKU ID", "SKU part number", "Label", "Purchased", "Assigned", "Available"],
            item => [item.SkuId, item.PartNumber, item.DisplayName, item.Purchased.ToString(), item.Assigned.ToString(), item.Available.ToString()], cancellationToken);

    public Task<CsvExportResult> ExportAssigneesAsync(WorkspaceContext context, string skuId, CancellationToken cancellationToken) =>
        ExecuteAsync(context, "licenses", "assignees", skuId,
            async token =>
            {
                var result = await assignees.SearchAsync(context, skuId, 100, token, cancellationToken);
                return new ExportPage<UserSummary>(result.Items, result.ContinuationToken, result.Error?.Category ?? (result.PartialData ? "partial_data" : null));
            },
            ["Id", "Display name", "User principal name", "Email", "Account enabled", "User type"],
            user => [user.Id, user.DisplayName, user.UserPrincipalName, user.Mail, user.AccountEnabled?.ToString(), user.UserType], cancellationToken);

    private async Task<CsvExportResult> ExecuteAsync<T>(WorkspaceContext context, string module, string target, string? targetId,
        Func<string?, Task<ExportPage<T>>> readPage, string[] columns, Func<T, string?[]> cells, CancellationToken cancellationToken)
    {
        CsvExportResult outcome;
        try
        {
            var rows = new List<T>();
            var visited = new HashSet<string>(StringComparer.Ordinal);
            string? token = null;
            bool truncated;
            do
            {
                if (!visited.Add(token ?? "<first>")) throw new InvalidOperationException("Graph pagination repeated a page.");
                var page = await readPage(token);
                if (page.Error is not null)
                {
                    outcome = new CsvExportResult(null, 0, false, page.Error);
                    break;
                }
                var remaining = CsvExportResult.MaximumRows - rows.Count;
                rows.AddRange(page.Items.Take(remaining));
                truncated = page.Items.Count > remaining;
                token = page.Next;
                if (truncated || (rows.Count == CsvExportResult.MaximumRows && token is null))
                {
                    outcome = new CsvExportResult(BuildCsv(columns, rows.Select(cells)), rows.Count, truncated);
                    break;
                }
                if (rows.Count == CsvExportResult.MaximumRows && token is not null)
                {
                    var sentinel = await readPage(token);
                    if (sentinel.Error is not null) outcome = new CsvExportResult(null, 0, false, sentinel.Error);
                    else outcome = new CsvExportResult(BuildCsv(columns, rows.Select(cells)), rows.Count,
                        sentinel.Items.Count > 0 || sentinel.Next is not null);
                    break;
                }
                if (token is null)
                {
                    outcome = new CsvExportResult(BuildCsv(columns, rows.Select(cells)), rows.Count, false);
                    break;
                }
            } while (true);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception) when (exception is UserSearchValidationException or DeviceSearchValidationException or LicenseOverviewValidationException or ArgumentException)
        {
            outcome = new CsvExportResult(null, 0, false, "invalid_query");
        }
        catch
        {
            outcome = new CsvExportResult(null, 0, false, "temporarily_unavailable");
        }

        await audit.WriteAsync(new AuditEvent
        {
            WorkspaceId = context.Membership.WorkspaceId,
            TenantId = context.User.TenantId,
            ActorTenantId = context.User.TenantId,
            ActorObjectId = context.User.ObjectId,
            Action = $"exports.{module}",
            TargetType = target,
            TargetId = targetId ?? module,
            Outcome = outcome.Error is null ? "succeeded" : "failed",
            FailureCategory = outcome.Error,
            Timestamp = DateTimeOffset.UtcNow,
            SafeMetadataJson = JsonSerializer.Serialize(new { rows = outcome.RowCount, truncated = outcome.Truncated })
        }, cancellationToken);
        return outcome;
    }

    private static string BuildCsv(string[] columns, IEnumerable<string?[]> rows)
    {
        var csv = new StringBuilder();
        csv.AppendLine(string.Join(',', columns.Select(Cell)));
        foreach (var row in rows) csv.AppendLine(string.Join(',', row.Select(Cell)));
        return csv.ToString();
    }

    private static string Cell(string? raw)
    {
        var value = raw ?? string.Empty;
        var index = 0;
        while (index < value.Length && (char.IsWhiteSpace(value[index]) || char.IsControl(value[index])
            || value[index] is '\uFEFF' or '\u200B' or '\u200C' or '\u200D' or '\u2060')) index++;
        if (index < value.Length && value[index] is '=' or '+' or '-' or '@') value = "'" + value;
        return '"' + value.Replace("\"", "\"\"", StringComparison.Ordinal) + '"';
    }
}
