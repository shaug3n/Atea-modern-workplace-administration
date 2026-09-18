using Atea.UnifiedWorkplace.Api.Authorization;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform").RequireAuthorization();
        platform.MapPost("/workspaces", CreateWorkspaceAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/memberships", AddMembershipAsync);

        endpoints.MapGet("/api/workspaces/current", GetCurrentWorkspaceAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> CreateWorkspaceAsync(CreateWorkspaceRequest request, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest(new { error = "invalid_workspace" });
        try
        {
            var result = await provisioning.CreateWorkspaceAsync(request.TenantId, request.DisplayName.Trim(), cancellationToken);
            if (result.IsConflict) return Results.Conflict(new { error = "workspace_already_exists" });
            var workspace = result.Workspace!;
            return Results.Created($"/api/platform/workspaces/{workspace.Id}", ToDto(workspace));
        }
        catch (WorkspaceProvisioningUnavailableException) { return Results.Json(new { error = "workspace_database_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> AddMembershipAsync(Guid workspaceId, AddWorkspaceMembershipRequest request, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.CanManageWorkspace(httpContext.User, workspaceId)) return Results.Json(new { error = "workspace_provisioning_scope_required" }, statusCode: StatusCodes.Status403Forbidden);
        if (request.TenantObjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.PlatformRole)) return Results.BadRequest(new { error = "invalid_membership" });
        try
        {
            var membership = await provisioning.AddMembershipAsync(workspaceId, request.TenantObjectId, request.Email.Trim(), request.PlatformRole.Trim(), request.IsAteaOperator, cancellationToken);
            return Results.Created($"/api/platform/workspaces/{workspaceId}/memberships/{membership.Id}", new { membership.Id, membership.WorkspaceId, membership.TenantObjectId, membership.Email, membership.PlatformRole, membership.IsAteaOperator });
        }
        catch (KeyNotFoundException) { return Results.NotFound(); }
        catch (WorkspaceAlreadyExistsException) { return Results.Conflict(new { error = "membership_already_exists" }); }
        catch (WorkspaceProvisioningUnavailableException) { return Results.Json(new { error = "workspace_database_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> GetCurrentWorkspaceAsync(IWorkspaceContextAccessor accessor, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var workspace = await provisioning.GetAsync(context.Membership.WorkspaceId, cancellationToken);
        return workspace is null || workspace.TenantId != context.User.TenantId ? Results.NotFound() : Results.Ok(ToDto(workspace));
    }

    private static WorkspaceDto ToDto(Infrastructure.Persistence.Entities.Workspace workspace) => new(workspace.Id, workspace.TenantId, workspace.DisplayName, workspace.ConnectionStatus);
}
