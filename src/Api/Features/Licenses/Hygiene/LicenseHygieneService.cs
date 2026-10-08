using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;

public interface ILicenseHygieneService
{
    Task<LicenseHygieneResponse> RefreshAsync(WorkspaceContext context, CancellationToken cancellationToken);
}

public sealed class LicenseHygieneService(
    ILicenseOverviewReader inventoryReader,
    ILicenseHygieneUserReader userReader,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    ILogger<LicenseHygieneService>? logger = null,
    Func<DateTimeOffset>? utcNow = null) : ILicenseHygieneService
{
    private readonly ILogger<LicenseHygieneService> logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<LicenseHygieneService>.Instance;
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<LicenseHygieneResponse> RefreshAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        var authorization = CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.LicensesHygieneView];
        var access = new LicenseHygieneAccess(authorization.State, authorization.ReasonCode, authorization);
        if (authorization.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
        {
            var unavailable = new LicenseHygieneSourceStatus(
                LicenseOverviewFreshness.Unavailable,
                false,
                null,
                null);
            return new LicenseHygieneResponse(
                access,
                unavailable,
                unavailable,
                new LicenseHygieneCoverage(0, false, "capability_required", 0),
                [],
                []);
        }

        var inventory = await ReadInventoryAsync(context, cancellationToken);
        var userScan = await ScanUsersAsync(context, cancellationToken);
        LogFailure("inventory", inventory.Error);
        LogFailure("user evidence", userScan.Error);
        var catalog = inventory.Error is null
            ? inventory.Value.ToDictionary(item => item.SkuId, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, LicenseOverviewItem>(StringComparer.OrdinalIgnoreCase);
        var inventoryStatus = inventory.Error is null
            ? new LicenseHygieneSourceStatus("live", false, utcNow(), null)
            : new LicenseHygieneSourceStatus("unavailable", false, null, MapError(inventory.Error));
        var userFreshness = userScan.PagesRead > 0 ? "live" : LicenseOverviewFreshness.Unavailable;
        var userStatus = new LicenseHygieneSourceStatus(
            userFreshness,
            !userScan.Completed || userScan.Error is not null,
            userFreshness == "live" ? userScan.CompletedAt ?? userScan.StartedAt : null,
            userScan.Error is null ? null : MapError(userScan.Error));

        var rows = new List<LicenseHygieneDisabledAccountRow>();
        var missingEvidenceRecords = 0;
        foreach (var user in userScan.Users)
        {
            var assignmentsValid = TryGetValidAssignments(user, out var skuIds);
            if (user.AccountEnabled is null || !assignmentsValid)
            {
                missingEvidenceRecords++;
            }

            if (user.AccountEnabled is not false || !assignmentsValid || skuIds.Count == 0)
            {
                continue;
            }

            var assignedSkus = skuIds
                .Select(skuId => MapAssignedSku(skuId, catalog))
                .ToArray();
            rows.Add(new LicenseHygieneDisabledAccountRow(
                user.Id,
                user.DisplayName,
                user.UserPrincipalName,
                assignedSkus,
                userScan.CompletedAt ?? userScan.StartedAt,
                "Microsoft Graph"));
        }

        var capacityItems = inventory.Error is null
            ? inventory.Value.Select(item => new LicenseHygieneSkuRow(
                item.SkuId,
                item.PartNumber,
                item.DisplayName,
                item.Purchased,
                item.Assigned,
                item.Available)).ToArray()
            : [];
        var coverage = new LicenseHygieneCoverage(
            userScan.Users.Count,
            userScan.Completed,
            userScan.StopReason,
            missingEvidenceRecords);

        return new LicenseHygieneResponse(
            access,
            inventoryStatus,
            userStatus,
            coverage,
            capacityItems,
            rows);
    }

    private async Task<GraphReadResult<IReadOnlyList<LicenseOverviewItem>>> ReadInventoryAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await inventoryReader.ReadAsync(context, new LicenseOverviewQuery(), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return FailedInventory(new GraphOperationResult(false, "timeout"));
        }
        catch (GraphAdapterException exception)
        {
            return FailedInventory(exception.Result);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return FailedInventory(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return FailedInventory(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (HttpRequestException)
        {
            return FailedInventory(new GraphOperationResult(false, "temporarily_unavailable"));
        }
    }

    private async Task<LicenseHygieneUserScanResult> ScanUsersAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await userReader.ScanAsync(context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            return FailedUserScan(new GraphOperationResult(false, "timeout"));
        }
        catch (GraphAdapterException exception)
        {
            return FailedUserScan(exception.Result);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return FailedUserScan(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return FailedUserScan(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (HttpRequestException)
        {
            return FailedUserScan(new GraphOperationResult(false, "temporarily_unavailable"));
        }
    }

    private GraphReadResult<IReadOnlyList<LicenseOverviewItem>> FailedInventory(GraphOperationResult error)
    {
        return GraphReadResult<IReadOnlyList<LicenseOverviewItem>>.Failed(error);
    }

    private LicenseHygieneUserScanResult FailedUserScan(GraphOperationResult error)
    {
        var now = utcNow();
        return new LicenseHygieneUserScanResult([], 0, false, error.Category, error, now, now);
    }

    private void LogFailure(string source, GraphOperationResult? error)
    {
        if (error is null)
        {
            return;
        }

        logger.LogWarning(
            "License hygiene {Source} source failed with category {Category} and status {StatusCode}.",
            source,
            error.Category,
            error.StatusCode);
    }

    private static LicenseHygieneAssignedSku MapAssignedSku(
        string skuId,
        IReadOnlyDictionary<string, LicenseOverviewItem> catalog) =>
        catalog.TryGetValue(skuId, out var item)
            ? new LicenseHygieneAssignedSku(skuId, item.PartNumber, item.DisplayName)
            : new LicenseHygieneAssignedSku(skuId, skuId, skuId);

    private static bool TryGetValidAssignments(
        LicenseHygieneUserObservation user,
        out IReadOnlyList<string> skuIds)
    {
        skuIds = user.AssignedSkuIds ?? [];
        return !user.AssignmentEvidenceMalformed
            && user.AssignedSkuIds is not null
            && user.AssignedSkuIds.All(skuId => Guid.TryParse(skuId, out _));
    }

    private static LicenseHygieneError MapError(GraphOperationResult error) =>
        new(
            error.Category,
            SafeMessage(error.Category),
            error.StatusCode,
            error.RetryAfter is null ? null : (int)Math.Ceiling(error.RetryAfter.Value.TotalSeconds));

    private static string SafeMessage(string category) => category switch
    {
        "not_authorized" => "Microsoft Graph denied the license hygiene read.",
        "consent_required" => "Delegated Microsoft Graph consent is required to read license hygiene data.",
        "throttled" => "Microsoft Graph throttled the license hygiene read. Try again later.",
        "timeout" => "The license hygiene source did not respond within its allowed time.",
        "invalid_response" => "Microsoft Graph returned an invalid license hygiene response.",
        _ => "License hygiene data is temporarily unavailable."
    };
}
