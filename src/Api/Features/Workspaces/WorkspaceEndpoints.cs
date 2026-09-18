using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using System.Security.Claims;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform").RequireAuthorization();
        platform.MapPost("/workspaces", CreateWorkspaceAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/memberships", AddMembershipAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/invitations", CreateInvitationAsync);

        endpoints.MapGet("/api/workspaces/current", GetCurrentWorkspaceAsync).RequireAuthorization();
        endpoints.MapGet("/api/workspaces/current/connection-health", GetConnectionHealthAsync).RequireAuthorization();
        endpoints.MapPost("/api/workspaces/current/connection-health/check", CheckConnectionHealthAsync).RequireAuthorization();
        endpoints.MapPost("/api/workspaces/current/consent/start", StartConsentAsync).RequireAuthorization();
        endpoints.MapPost("/api/invitations/{nonce}/redeem", RedeemInvitationAsync).RequireAuthorization();
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

    private static async Task<IResult> CreateInvitationAsync(Guid workspaceId, InvitationRequest request, HttpContext httpContext, IPlatformAuthorization authorization, InvitationService invitations, CancellationToken cancellationToken)
    {
        if (!authorization.CanManageWorkspace(httpContext.User, workspaceId)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) || request.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest(new { error = "invalid_invitation" });
        var result = await invitations.CreateAsync(workspaceId, request.Email, request.DisplayName, request.ExpiresAt, cancellationToken);
        return Results.Ok(new { result.InvitationId, result.InvitationUrl, result.ExpiresAt });
    }

    private static async Task<IResult> RedeemInvitationAsync(string nonce, HttpContext httpContext, InvitationService invitations, CancellationToken cancellationToken)
    {
        var tenantId = ParseGuidClaim(httpContext.User, "tid");
        var objectId = ParseGuidClaim(httpContext.User, "oid");
        var email = httpContext.User.FindFirstValue("preferred_username") ?? httpContext.User.FindFirstValue("upn");
        if (tenantId == Guid.Empty || objectId == Guid.Empty || string.IsNullOrWhiteSpace(email)) return Results.Unauthorized();
        var redeemed = await invitations.RedeemAsync(nonce, tenantId, objectId, email, httpContext.User.FindFirstValue("name") ?? string.Empty, cancellationToken);
        return redeemed ? Results.Ok(new { status = ConnectionState.ConsentRequired }) : Results.BadRequest(new { error = "invitation_invalid_or_expired" });
    }

    private static async Task<IResult> GetConnectionHealthAsync(IWorkspaceContextAccessor accessor, IOnboardingService onboarding, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var state = await onboarding.GetAsync(context.Membership.WorkspaceId, cancellationToken);
        return Results.Ok(ToHealthDto(state, null, ""));
    }

    private static async Task<IResult> CheckConnectionHealthAsync(IWorkspaceContextAccessor accessor, IOnboardingService onboarding, IConnectionHealthReader reader, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var correlationId = httpContext.TraceIdentifier;
        var result = await reader.ReadAsync(context.User.TenantId, cancellationToken);
        var status = result.Status switch
        {
            ConnectionHealthStatus.Connected => ConnectionState.Connected,
            ConnectionHealthStatus.ConsentRequired => ConnectionState.ConsentRequired,
            ConnectionHealthStatus.PermissionIncomplete => ConnectionState.PermissionIncomplete,
            ConnectionHealthStatus.ConsentRevoked => ConnectionState.ConsentRevoked,
            ConnectionHealthStatus.ConnectionFailed => ConnectionState.ConnectionFailed,
            _ => ConnectionState.TemporarilyUnavailable
        };
        try
        {
            await onboarding.RecordCheckAsync(context.Membership.WorkspaceId, status, result.GrantedScopes, result.ProblemCategory, cancellationToken);
        }
        catch (InvalidOperationException)
        {
            return Results.Conflict(new { error = "connection_state_transition_invalid", correlationId });
        }
        return Results.Ok(new ConnectionHealthDto(context.Membership.WorkspaceId, status, DateTimeOffset.UtcNow, result.GrantedScopes, result.ProblemCategory, correlationId));
    }

    private static IResult StartConsentAsync(IWorkspaceContextAccessor accessor, IConfiguration configuration)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var clientId = configuration["AzureAd:ClientId"] ?? string.Empty;
        var redirectUri = configuration["Onboarding:ConsentRedirectUri"] ?? "/onboarding";
        var scopes = string.Join(' ', GraphScopeCatalog.V1DelegatedScopes);
        var url = $"https://login.microsoftonline.com/{context.User.TenantId}/oauth2/v2.0/authorize?client_id={Uri.EscapeDataString(clientId)}&response_type=code&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_mode=query&scope={Uri.EscapeDataString(scopes)}&state=connection-health";
        return Results.Ok(new ConsentStartResponse(url, GraphScopeCatalog.V1DelegatedScopes, "delegated_consent_required"));
    }

    private static ConnectionHealthDto ToHealthDto(WorkspaceOnboardingState state, string? problem, string correlationId) => new(state.WorkspaceId, state.ConnectionStatus, state.LastVerifiedAt, state.ConsentScopes, problem ?? state.FailureCategory, correlationId);
    private static Guid ParseGuidClaim(ClaimsPrincipal principal, string type) => Guid.TryParse(principal.FindFirstValue(type), out var value) ? value : Guid.Empty;

    private static WorkspaceDto ToDto(Infrastructure.Persistence.Entities.Workspace workspace) => new(workspace.Id, workspace.TenantId, workspace.DisplayName, workspace.ConnectionStatus);
}
