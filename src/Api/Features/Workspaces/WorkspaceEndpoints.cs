using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var platform = endpoints.MapGroup("/api/platform").RequireAuthorization("PlatformAdminPolicy");
        platform.MapGet("/session", GetPlatformSessionAsync);
        platform.MapPost("/workspaces/onboard", OnboardWorkspaceAsync);
        platform.MapPost("/workspaces", CreateWorkspaceAsync);
        platform.MapGet("/workspaces", ListWorkspacesAsync);
        platform.MapGet("/workspaces/{workspaceId:guid}", GetWorkspaceDetailAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/memberships", AddMembershipAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/invitations", CreateInvitationAsync);
        platform.MapPost("/workspaces/{workspaceId:guid}/invitations/{invitationId:guid}/reissue", ReissueInvitationAsync);
        platform.MapDelete("/workspaces/{workspaceId:guid}/invitations/{invitationId:guid}", RevokeInvitationAsync);

        endpoints.MapGet("/api/workspaces/current", GetCurrentWorkspaceAsync).RequireAuthorization();
        endpoints.MapGet("/api/workspaces/current/connection-health", GetConnectionHealthAsync).RequireAuthorization();
        endpoints.MapPost("/api/workspaces/current/connection-health/check", CheckConnectionHealthAsync).RequireAuthorization();
        endpoints.MapPost("/api/workspaces/current/consent/start", StartConsentAsync).RequireAuthorization();
        endpoints.MapPost("/api/workspaces/current/consent/complete", CompleteConsentAsync).RequireAuthorization();
        endpoints.MapPost("/api/invitations/{nonce}/redeem", RedeemInvitationAsync).RequireAuthorization();
        return endpoints;
    }

    private static IResult GetPlatformSessionAsync(HttpContext httpContext, IPlatformAuthorization authorization)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        var tenantId = httpContext.User.FindFirstValue("tid");
        var objectId = httpContext.User.FindFirstValue("oid");
        if (!Guid.TryParse(tenantId, out var parsedTenantId) || !Guid.TryParse(objectId, out var parsedObjectId))
            return Results.Forbid();
        var displayName = httpContext.User.FindFirstValue("name")
            ?? httpContext.User.FindFirstValue("preferred_username")
            ?? string.Empty;
        return Results.Ok(new
        {
            authenticated = true,
            tenantId = parsedTenantId,
            objectId = parsedObjectId,
            displayName,
            userPrincipalName = httpContext.User.FindFirstValue("preferred_username")
        });
    }

    private static async Task<IResult> ListWorkspacesAsync(HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        var scope = await authorization.GetWorkspaceScopeAsync(httpContext.User, cancellationToken);
        var workspaces = await provisioning.ListAsync(scope, cancellationToken);
        return Results.Ok(workspaces.Select(ToDto));
    }

    private static async Task<IResult> GetWorkspaceDetailAsync(Guid workspaceId, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        var scope = await authorization.GetWorkspaceScopeAsync(httpContext.User, cancellationToken);
        var detail = await provisioning.GetAdminDetailAsync(workspaceId, scope, cancellationToken);
        return detail is null ? Results.NotFound() : Results.Ok(detail);
    }

    private static async Task<IResult> CreateWorkspaceAsync(CreateWorkspaceRequest request, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.DisplayName)) return Results.BadRequest(new { error = "invalid_workspace" });
        try
        {
            var operatorIdentity = PlatformOperatorIdentityReader.Read(httpContext.User);
            var result = await provisioning.CreateWorkspaceAsync(request.TenantId, request.DisplayName.Trim(), operatorIdentity, cancellationToken);
            if (result.IsConflict) return Results.Conflict(new { error = "workspace_already_exists" });
            var workspace = result.Workspace!;
            return Results.Created($"/api/platform/workspaces/{workspace.Id}", ToDto(workspace));
        }
        catch (WorkspaceProvisioningUnavailableException) { return Results.Json(new { error = "workspace_database_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> OnboardWorkspaceAsync(OnboardWorkspaceRequest request, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!authorization.IsAuthorized(httpContext.User)) return Results.Forbid();
        if (request.TenantId == Guid.Empty || string.IsNullOrWhiteSpace(request.DisplayName) || request.DisplayName.Trim().Length > 200
            || !TryNormalizeAdminInvite(request.AdminUpn, request.AdminDisplayName, out var upn, out var adminDisplayName))
            return Results.BadRequest(new { error = "invalid_workspace_onboarding" });

        try
        {
            var audit = CreatePlatformAudit(httpContext.User, Guid.Empty, "workspace.onboarded", "workspace", "{}");
            var operatorIdentity = PlatformOperatorIdentityReader.Read(httpContext.User);
            var result = await provisioning.OnboardAsync(request.TenantId, request.DisplayName.Trim(), upn, adminDisplayName, audit, operatorIdentity, cancellationToken);
            if (result.IsConflict) return Results.Conflict(new { error = "workspace_already_exists" });
            var workspace = result.Workspace!;
            return Results.Created($"/api/platform/workspaces/{workspace.Id}", new WorkspaceOnboardingResponse(ToDto(workspace), result.InvitationUrl!, result.ExpiresAt!.Value));
        }
        catch (WorkspaceProvisioningUnavailableException) { return Results.Json(new { error = "workspace_database_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable); }
    }

    private static async Task<IResult> AddMembershipAsync(Guid workspaceId, AddWorkspaceMembershipRequest request, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceProvisioningService provisioning, CancellationToken cancellationToken)
    {
        if (!await authorization.CanManageWorkspaceAsync(httpContext.User, workspaceId, cancellationToken)) return Results.Json(new { error = "workspace_provisioning_scope_required" }, statusCode: StatusCodes.Status403Forbidden);
        if (request.TenantObjectId == Guid.Empty || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.PlatformRole)) return Results.BadRequest(new { error = "invalid_membership" });
        try
        {
            var audit = CreatePlatformAudit(httpContext.User, workspaceId, "workspace.membership.added", "membership", System.Text.Json.JsonSerializer.Serialize(new { role = request.PlatformRole.Trim() }));
            var membership = await provisioning.AddMembershipAsync(workspaceId, request.TenantObjectId, request.Email.Trim(), request.PlatformRole.Trim(), request.IsAteaOperator, audit, cancellationToken);
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
        if (!await authorization.CanManageWorkspaceAsync(httpContext.User, workspaceId, cancellationToken)) return Results.Forbid();
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.DisplayName) || request.ExpiresAt <= DateTimeOffset.UtcNow) return Results.BadRequest(new { error = "invalid_invitation" });
        var audit = CreatePlatformAudit(httpContext.User, workspaceId, "workspace.invitation.created", "invitation", "{}");
        var result = await invitations.CreateForRoleAsync(workspaceId, request.Email, request.DisplayName, request.ExpiresAt, "customer_admin", request.ApprovedTenantObjectId, cancellationToken, audit);
        return Results.Ok(new { result.InvitationId, result.InvitationUrl, result.ExpiresAt });
    }

    private static async Task<IResult> ReissueInvitationAsync(Guid workspaceId, Guid invitationId, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceAccessRepository accessRepository, InvitationService invitations, CancellationToken cancellationToken)
    {
        if (!await authorization.CanManageWorkspaceAsync(httpContext.User, workspaceId, cancellationToken)) return Results.Json(new { error = "workspace_provisioning_scope_required" }, statusCode: StatusCodes.Status403Forbidden);
        var current = await accessRepository.GetInvitationAsync(workspaceId, invitationId, cancellationToken);
        if (current is null || current.RedeemedAt is not null || current.RevokedAt is not null) return Results.NotFound();
        var audit = CreatePlatformAudit(httpContext.User, workspaceId, "workspace.invitation.reissued", "invitation", System.Text.Json.JsonSerializer.Serialize(new { previousInvitationId = invitationId }));
        var result = await invitations.CreateForRoleAsync(workspaceId, current.Email, current.DisplayName, DateTimeOffset.UtcNow.AddDays(7), current.Role, current.ApprovedTenantObjectId, cancellationToken, audit);
        return Results.Ok(new WorkspaceInvitationLink(result.InvitationId, result.InvitationUrl, result.ExpiresAt));
    }

    private static async Task<IResult> RevokeInvitationAsync(Guid workspaceId, Guid invitationId, HttpContext httpContext, IPlatformAuthorization authorization, IWorkspaceAccessRepository accessRepository, CancellationToken cancellationToken)
    {
        if (!await authorization.CanManageWorkspaceAsync(httpContext.User, workspaceId, cancellationToken)) return Results.Json(new { error = "workspace_provisioning_scope_required" }, statusCode: StatusCodes.Status403Forbidden);
        var audit = CreatePlatformAudit(httpContext.User, workspaceId, "workspace.invitation.revoked", "invitation", "{}");
        return await accessRepository.RevokeInvitationAsync(workspaceId, invitationId, audit, cancellationToken) ? Results.NoContent() : Results.NotFound();
    }

    private static async Task<IResult> RedeemInvitationAsync(string nonce, HttpContext httpContext, InvitationService invitations, CancellationToken cancellationToken)
    {
        var tenantId = ParseGuidClaim(httpContext.User, "tid");
        var objectId = ParseGuidClaim(httpContext.User, "oid");
        var email = httpContext.User.FindFirstValue("preferred_username") ?? httpContext.User.FindFirstValue("upn");
        if (tenantId == Guid.Empty || objectId == Guid.Empty) return Results.Unauthorized();
        var redemption = await invitations.RedeemDetailedAsync(nonce, tenantId, objectId, email, httpContext.User.FindFirstValue("name") ?? string.Empty, cancellationToken);
        return redemption is null
            ? Results.BadRequest(new { error = "invitation_invalid_or_expired" })
            : Results.Ok(new InvitationRedemptionResponse(ConnectionState.ConsentRequired, redemption.Workspace.Id, redemption.Workspace.DisplayName, "/overview"));
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

    private static async Task<IResult> StartConsentAsync(IWorkspaceContextAccessor accessor, IOptions<OnboardingOptions> onboardingOptions, ConsentChallengeService challenges, IConsentChallengeRepository challengeRepository, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!challenges.IsConfigured) return Results.Json(new { error = "consent_configuration_unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
        var clientId = configuration["AzureAd:ClientId"] ?? string.Empty;
        var redirectUri = onboardingOptions.Value.ConsentRedirectUri;
        var challenge = challenges.Create(context.Membership.WorkspaceId, context.User.TenantId);
        await challengeRepository.CreateAsync(context.Membership.WorkspaceId, context.User.TenantId, ConsentChallengeService.HashState(challenge.Challenge), challenge.CorrelationId, challenge.ExpiresAt, cancellationToken);
        const string graphDefaultScope = "https://graph.microsoft.com/.default";
        var url = $"https://login.microsoftonline.com/{context.User.TenantId}/v2.0/adminconsent?client_id={Uri.EscapeDataString(clientId)}&scope={Uri.EscapeDataString(graphDefaultScope)}&redirect_uri={Uri.EscapeDataString(redirectUri)}&state={Uri.EscapeDataString(challenge.Challenge)}";
        return Results.Ok(new ConsentStartResponse(url, GraphScopeCatalog.CapabilityEvaluationScopes, challenge.Challenge, challenge.CorrelationId));
    }

    private static async Task<IResult> CompleteConsentAsync(ConsentCompletionRequest request, IWorkspaceContextAccessor accessor, ConsentChallengeService challenges, IConsentChallengeRepository challengeRepository, CancellationToken cancellationToken)
    {
        var context = accessor.Current;
        if (context is null) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.Tenant == Guid.Empty || request.Tenant != context.User.TenantId || string.IsNullOrWhiteSpace(request.State))
            return Results.Ok(new ConsentCompletionResponse(false, "invalid_callback", string.Empty));
        if (!await challenges.TryValidateAndConsumeAsync(request.State, context.Membership.WorkspaceId, context.User.TenantId, challengeRepository, cancellationToken))
            return Results.Ok(new ConsentCompletionResponse(false, "invalid_callback", string.Empty));
        if (!challenges.TryRead(request.State, context.Membership.WorkspaceId, context.User.TenantId, out var challenge))
            return Results.Ok(new ConsentCompletionResponse(false, "invalid_callback", string.Empty));
        var status = string.IsNullOrWhiteSpace(request.ErrorCode) ? "consent_received" : "consent_denied";
        return Results.Ok(new ConsentCompletionResponse(true, status, challenge.CorrelationId));
    }

    private static ConnectionHealthDto ToHealthDto(WorkspaceOnboardingState state, string? problem, string correlationId) => new(state.WorkspaceId, state.ConnectionStatus, state.LastVerifiedAt, state.ConsentScopes, problem ?? state.FailureCategory, correlationId);
    private static Guid ParseGuidClaim(ClaimsPrincipal principal, string type) => Guid.TryParse(principal.FindFirstValue(type), out var value) ? value : Guid.Empty;

    private static bool TryNormalizeAdminInvite(string? rawUpn, string? rawDisplayName, out string upn, out string adminDisplayName)
    {
        upn = rawUpn?.Trim() ?? string.Empty;
        adminDisplayName = string.IsNullOrWhiteSpace(rawDisplayName) ? upn : rawDisplayName.Trim();
        try
        {
            return upn.Length is > 0 and <= 320 && new System.Net.Mail.MailAddress(upn).Address.Equals(upn, StringComparison.OrdinalIgnoreCase)
                && adminDisplayName.Length is > 0 and <= 200 && !adminDisplayName.Any(char.IsControl);
        }
        catch (FormatException) { return false; }
    }

    private static AuditEvent CreatePlatformAudit(ClaimsPrincipal principal, Guid workspaceId, string action, string targetType, string metadata) => new()
    {
        Id = Guid.NewGuid(), WorkspaceId = workspaceId, TenantId = ParseGuidClaim(principal, "tid"),
        ActorTenantId = ParseGuidClaim(principal, "tid"), ActorObjectId = ParseGuidClaim(principal, "oid"),
        Action = action, TargetType = targetType, Outcome = "success", Timestamp = DateTimeOffset.UtcNow,
        SafeMetadataJson = metadata
    };

    private static WorkspaceDto ToDto(Infrastructure.Persistence.Entities.Workspace workspace) => new(workspace.Id, workspace.TenantId, workspace.DisplayName, workspace.ConnectionStatus);
}
