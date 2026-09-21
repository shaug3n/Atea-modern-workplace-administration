using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using PlatformWorkspaceScope = Atea.UnifiedWorkplace.Api.Authorization.PlatformWorkspaceScope;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public interface IWorkspaceProvisioningService
{
    Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default);
    Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default);
    Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default);
    Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default);
    Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default);
}

public sealed record WorkspaceProvisioningResult(Workspace? Workspace, bool IsConflict)
{
    public static WorkspaceProvisioningResult Created(Workspace workspace) => new(workspace, false);
    public static WorkspaceProvisioningResult Conflict() => new(null, true);
}

public sealed class WorkspaceUniqueConstraintException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class WorkspaceProvisioningDatabaseException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class WorkspaceProvisioningUnavailableException(string message, Exception? innerException = null) : Exception(message, innerException);
public sealed class WorkspaceAlreadyExistsException(string message, Exception? innerException = null) : Exception(message, innerException);

public sealed class WorkspaceProvisioningService(IWorkspaceProvisioningRepository repository) : IWorkspaceProvisioningService
{
    public Task<IReadOnlyList<Workspace>> ListAsync(PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => repository.ListAsync(workspaceScope, cancellationToken);

    public Task<WorkspaceAdminDetailDto?> GetAdminDetailAsync(Guid workspaceId, PlatformWorkspaceScope workspaceScope, CancellationToken cancellationToken = default) => repository.GetAdminDetailAsync(workspaceId, workspaceScope, cancellationToken);

    public async Task<WorkspaceProvisioningResult> CreateWorkspaceAsync(Guid tenantId, string displayName, CancellationToken cancellationToken = default)
    {
        if (await repository.FindByTenantIdAsync(tenantId, cancellationToken) is not null) return WorkspaceProvisioningResult.Conflict();
        try
        {
            return WorkspaceProvisioningResult.Created(await repository.CreateAsync(tenantId, displayName, cancellationToken));
        }
        catch (WorkspaceUniqueConstraintException)
        {
            return WorkspaceProvisioningResult.Conflict();
        }
        catch (WorkspaceProvisioningDatabaseException exception)
        {
            throw new WorkspaceProvisioningUnavailableException("Workspace persistence is temporarily unavailable.", exception);
        }
    }

    public async Task<WorkspaceMembership> AddMembershipAsync(Guid workspaceId, Guid tenantObjectId, string email, string platformRole, bool isAteaOperator, CancellationToken cancellationToken = default)
    {
        try
        {
            return await repository.AddMembershipAsync(workspaceId, tenantObjectId, email, platformRole, isAteaOperator, cancellationToken);
        }
        catch (WorkspaceUniqueConstraintException exception)
        {
            throw new WorkspaceAlreadyExistsException("Membership already exists.", exception);
        }
        catch (WorkspaceProvisioningDatabaseException exception)
        {
            throw new WorkspaceProvisioningUnavailableException("Workspace persistence is temporarily unavailable.", exception);
        }
    }

    public Task<Workspace?> GetAsync(Guid workspaceId, CancellationToken cancellationToken = default) => repository.GetAsync(workspaceId, cancellationToken);
}
