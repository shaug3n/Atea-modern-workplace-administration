using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses;

public interface ILicenseAssigneeService
{
    Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, string skuId, int pageSize, string? continuationToken, CancellationToken cancellationToken);
}

public sealed class LicenseAssigneeService(
    IUserDirectoryReader reader,
    IGraphAuthorizationSnapshotReader authorizationReader,
    UserContinuationTokenProtector tokens) : ILicenseAssigneeService
{
    public async Task<UserDirectoryResponse> SearchAsync(WorkspaceContext context, string skuId, int pageSize, string? continuationToken, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(skuId, out var parsedSku) || pageSize is < 1 or > 100)
            throw new UserSearchValidationException("A valid SKU ID and pageSize between 1 and 100 are required.");
        var query = new UserSearchQuery(PageSize: pageSize, License: parsedSku.ToString("D"));
        var decision = CapabilityEvaluator.Evaluate(await authorizationReader.ReadAsync(context, cancellationToken), context.Membership)[Capability.LicensesView];
        if (decision.State is not (CapabilityState.Allowed or CapabilityState.ReadOnly))
            return new UserDirectoryResponse([], null, DateTimeOffset.UtcNow, UserDirectoryFreshness.Unavailable, true,
                new UserDirectoryError("capability_required", "License assignees cannot be read for the current capability state.", decision.State));

        var path = string.IsNullOrWhiteSpace(continuationToken) ? null : tokens.Unprotect(continuationToken, context, query, DateTimeOffset.UtcNow);
        var result = await reader.SearchAsync(context, query with { ContinuationPath = path }, cancellationToken);
        if (result.Error is not null)
            return new UserDirectoryResponse([], null, DateTimeOffset.UtcNow, UserDirectoryFreshness.Unavailable, true,
                new UserDirectoryError(result.Error.Category, "Microsoft Graph could not load license assignees.", StatusCode: result.Error.StatusCode));
        var next = result.ContinuationLink is null ? null : tokens.Protect(result.ContinuationLink, context, query, DateTimeOffset.UtcNow);
        return new UserDirectoryResponse(result.Items, next, DateTimeOffset.UtcNow, UserDirectoryFreshness.Fresh, false);
    }
}
