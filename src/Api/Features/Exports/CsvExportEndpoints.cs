using System.Globalization;
using System.Text;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Features.Exports;

public static class CsvExportEndpoints
{
    public static IEndpointRouteBuilder MapCsvExportEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/users/export.csv", UsersAsync).RequireAuthorization().RequireWorkspaceModule("users");
        endpoints.MapGet("/api/devices/export.csv", DevicesAsync).RequireAuthorization().RequireWorkspaceModule("devices");
        endpoints.MapGet("/api/licenses/export.csv", LicensesAsync).RequireAuthorization().RequireWorkspaceModule("licenses");
        endpoints.MapGet("/api/licenses/{skuId}/assignees/export.csv", AssigneesAsync).RequireAuthorization().RequireWorkspaceModule("licenses");
        return endpoints;
    }

    private static Task<IResult> UsersAsync(IWorkspaceContextAccessor accessor, CsvExportService service, HttpContext http, CancellationToken cancellationToken) =>
        RespondAsync(accessor, http, "users.csv", context => service.ExportUsersAsync(context,
            new UserSearchRequest(Query(http, "search"), AccountStatus: Query(http, "accountStatus"), TenantRole: Query(http, "tenantRole"),
                License: Query(http, "license"), UserType: Query(http, "userType")), cancellationToken));

    private static Task<IResult> DevicesAsync(IWorkspaceContextAccessor accessor, CsvExportService service, HttpContext http, CancellationToken cancellationToken) =>
        RespondAsync(accessor, http, "devices.csv", context => service.ExportDevicesAsync(context,
            new DeviceSearchRequest(Query(http, "search"), ComplianceState: Query(http, "complianceState"), OperatingSystem: Query(http, "operatingSystem")), cancellationToken));

    private static Task<IResult> LicensesAsync(IWorkspaceContextAccessor accessor, CsvExportService service, HttpContext http, CancellationToken cancellationToken) =>
        RespondAsync(accessor, http, "licenses.csv", context => service.ExportLicensesAsync(context,
            new LicenseOverviewRequest(Search: Query(http, "search"), Filter: Query(http, "filter")), cancellationToken));

    private static Task<IResult> AssigneesAsync(string skuId, IWorkspaceContextAccessor accessor, CsvExportService service, HttpContext http, CancellationToken cancellationToken) =>
        RespondAsync(accessor, http, "license-assignees.csv", context => service.ExportAssigneesAsync(context, skuId, cancellationToken));

    private static async Task<IResult> RespondAsync(IWorkspaceContextAccessor accessor, HttpContext http, string fileName,
        Func<WorkspaceContext, Task<CsvExportResult>> export)
    {
        if (accessor.Current is not { } context) return Results.Forbid();
        http.Response.Headers.CacheControl = "no-store";
        CsvExportResult result;
        try { result = await export(context); }
        catch (OperationCanceledException) { throw; }
        catch { return Results.Json(new { error = "audit_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
        if (result.Error is not null)
        {
            var status = result.Error switch
            {
                "invalid_query" or "invalid_license_filter" => StatusCodes.Status400BadRequest,
                "capability_required" or "not_authorized" or "consent_required" => StatusCodes.Status403Forbidden,
                "throttled" => StatusCodes.Status429TooManyRequests,
                _ => StatusCodes.Status503ServiceUnavailable
            };
            return Results.Json(new { error = result.Error }, statusCode: status);
        }
        http.Response.Headers["X-Export-Row-Count"] = result.RowCount.ToString(CultureInfo.InvariantCulture);
        http.Response.Headers["X-Export-Max-Rows"] = CsvExportResult.MaximumRows.ToString(CultureInfo.InvariantCulture);
        http.Response.Headers["X-Export-Truncated"] = result.Truncated ? "true" : "false";
        return Results.File(Encoding.UTF8.GetBytes(result.Csv!), "text/csv; charset=utf-8", fileName);
    }

    private static string? Query(HttpContext http, string key) => http.Request.Query.TryGetValue(key, out var value) ? value.ToString() : null;
}
