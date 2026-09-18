using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

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

    private static async Task<IResult> CreateWorkspaceAsync(CreateWorkspaceRequest request, HttpContext httpContext, IPlatformAuthorization authorization, WorkplaceDbContext db, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest(new { error = "invalid_workspace" });
        if (await db.Workspaces.AnyAsync(x => x.TenantId == request.TenantId, cancellationToken)) return Results.Conflict(new { error = "workspace_already_exists" });
        var workspaceId = Guid.NewGuid();
        var workspace = await new WorkspaceRepository(db, workspaceId).AddAsync(workspaceId, request.TenantId, request.DisplayName.Trim(), cancellationToken);
        return Results.Created($"/api/platform/workspaces/{workspace.Id}", ToDto(workspace));
    }

    private static async Task<IResult> AddMembershipAsync(Guid workspaceId, AddWorkspaceMembershipRequest request, HttpContext httpContext, IPlatformAuthorization authorization, WorkplaceDbContext db, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        if (request.TenantObjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.PlatformRole)) return Results.BadRequest(new { error = "invalid_membership" });
        if (!await db.Workspaces.AnyAsync(x => x.Id == workspaceId, cancellationToken)) return Results.NotFound();
        try
        {
            var membership = await new WorkspaceRepository(db, workspaceId).AddMembershipAsync(workspaceId, request.TenantObjectId, request.Email.Trim(), request.PlatformRole.Trim(), request.IsAteaOperator, cancellationToken);
            return Results.Created($"/api/platform/workspaces/{workspaceId}/memberships/{membership.Id}", new { membership.Id, membership.WorkspaceId, membership.TenantObjectId, membership.Email, membership.PlatformRole, membership.IsAteaOperator });
        }
        catch (DbUpdateException) { return Results.Conflict(new { error = "membership_already_exists" }); }
    }

    private static async Task<IResult> GetCurrentWorkspaceAsync(IWorkspaceContextAccessor accessor, WorkplaceDbContext db, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var workspace = await db.Workspaces.AsNoTracking().SingleOrDefaultAsync(x => x.Id == context.Membership.WorkspaceId && x.TenantId == context.User.TenantId, cancellationToken);
        return workspace is null ? Results.NotFound() : Results.Ok(ToDto(workspace));
    }

    private static WorkspaceDto ToDto(Infrastructure.Persistence.Entities.Workspace workspace) => new(workspace.Id, workspace.TenantId, workspace.DisplayName, workspace.ConnectionStatus);
}
