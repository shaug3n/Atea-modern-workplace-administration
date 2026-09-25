using System.Net.Mail;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record WorkspaceInvitationLink(Guid Id, string InvitationUrl, DateTimeOffset ExpiresAt);

public static class WorkspaceAccessEndpoints
{
    private static readonly TimeSpan InvitationLifetime = TimeSpan.FromDays(7);
    private static readonly HashSet<string> AllowedRoles = new(StringComparer.OrdinalIgnoreCase) { "member", "customer_admin" };

    public static IEndpointRouteBuilder MapWorkspaceAccessEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var access = endpoints.MapGroup("/api/workspaces/current/access").RequireAuthorization();
        access.MapGet("", ListAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapPost("/invitations", CreateInvitationAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapPost("/invitations/{invitationId:guid}/reissue", ReissueInvitationAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapDelete("/invitations/{invitationId:guid}", RevokeInvitationAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapPatch("/memberships/{membershipId:guid}", ChangeRoleAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapPatch("/memberships/{membershipId:guid}/modules", ChangeModulesAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapPost("/ownership/transfer", TransferOwnershipAsync).RequireCapability(Capability.WorkspaceMembersManage);
        access.MapDelete("/memberships/{membershipId:guid}", RemoveMembershipAsync).RequireCapability(Capability.WorkspaceMembersManage);
        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var (memberships, invitations) = await repository.ListAsync(context.Membership.WorkspaceId, cancellationToken);
        return Results.Ok(new WorkspaceAccessResponse(
            memberships.Where(x => !x.IsAteaOperator).Select(x => new WorkspaceAccessMembershipDto(x.Id, x.TenantObjectId, x.Email, NormalizeLegacyRole(x.PlatformRole), x.CreatedAt, ParseModuleKeys(x.ModuleGrantsJson))).ToArray(),
            invitations.Select(x => new WorkspaceAccessInvitationDto(x.Id, x.Email, x.DisplayName, x.Role, x.ExpiresAt, x.RedeemedAt, x.RevokedAt, ParseModuleKeys(x.ModuleKeysJson))).ToArray()));
    }

    private static async Task<IResult> CreateInvitationAsync(
        WorkspaceAccessInvitationRequest request,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository accessRepository,
        IWorkspaceSettingsService settings,
        InvitationService invitations,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!TryNormalizeInvitation(request.Email, request.DisplayName, request.Role, out var email, out var displayName, out var role))
            return Results.BadRequest(new { error = "invalid_invitation" });

        var (memberships, _) = await accessRepository.ListAsync(context.Membership.WorkspaceId, cancellationToken);
        if (memberships.Any(x => string.Equals(x.Email, email, StringComparison.OrdinalIgnoreCase)))
            return Results.Conflict(new { error = "user_already_has_workspace_access" });

        var owner = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole);
        if (!owner && role != "member") return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.ModuleKeys is null) return Results.BadRequest(new { error = "module_selection_required" });
        var enabled = (await settings.GetConfigurationAsync(context, cancellationToken)).EnabledModules;
        var actorModules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, enabled, context.Membership.ModuleKeys);
        if (!TryNormalizeGrants(request.ModuleKeys, enabled, out var moduleKeys)
            || (!owner && moduleKeys.Except(actorModules, StringComparer.OrdinalIgnoreCase).Any()))
            return Results.BadRequest(new { error = "invalid_module_selection" });

        var result = await invitations.CreateForRoleAsync(context.Membership.WorkspaceId, email, displayName, DateTimeOffset.UtcNow.Add(InvitationLifetime), role, cancellationToken: cancellationToken,
            auditEvent: CreateAuditEvent(context, "workspace.invitation.created", "invitation", Guid.Empty, role), moduleKeys: moduleKeys);
        return Results.Created($"/api/workspaces/current/access/invitations/{result.InvitationId}", new WorkspaceInvitationLink(result.InvitationId, result.InvitationUrl, result.ExpiresAt));
    }

    private static async Task<IResult> ReissueInvitationAsync(
        Guid invitationId,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository accessRepository,
        IWorkspaceSettingsService settings,
        InvitationService invitations,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var current = await accessRepository.GetInvitationAsync(context.Membership.WorkspaceId, invitationId, cancellationToken);
        if (current is null || current.RedeemedAt is not null || current.RevokedAt is not null)
            return Results.NotFound();

        var owner = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole);
        if (!owner && !current.Role.Equals("member", StringComparison.OrdinalIgnoreCase)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var grants = ParseModuleKeys(current.ModuleKeysJson);
        var enabled = (await settings.GetConfigurationAsync(context, cancellationToken)).EnabledModules;
        var actorModules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, enabled, context.Membership.ModuleKeys);
        if (!owner && grants.Except(actorModules, StringComparer.OrdinalIgnoreCase).Any()) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var result = await invitations.CreateForRoleAsync(context.Membership.WorkspaceId, current.Email, current.DisplayName, DateTimeOffset.UtcNow.Add(InvitationLifetime), NormalizeLegacyRole(current.Role), cancellationToken: cancellationToken,
            auditEvent: CreateAuditEvent(context, "workspace.invitation.reissued", "invitation", Guid.Empty, NormalizeLegacyRole(current.Role)), moduleKeys: grants);
        return Results.Ok(new WorkspaceInvitationLink(result.InvitationId, result.InvitationUrl, result.ExpiresAt));
    }

    private static async Task<IResult> RevokeInvitationAsync(
        Guid invitationId,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var invitation = await repository.GetInvitationAsync(context.Membership.WorkspaceId, invitationId, cancellationToken);
        if (invitation is null) return Results.NotFound();
        if (!WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole)
            && !invitation.Role.Equals("member", StringComparison.OrdinalIgnoreCase))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!await repository.RevokeInvitationAsync(context.Membership.WorkspaceId, invitationId, CreateAuditEvent(context, "workspace.invitation.revoked", "invitation", invitationId, null), cancellationToken)) return Results.NotFound();
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeRoleAsync(
        Guid membershipId,
        WorkspaceAccessRoleRequest request,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (string.IsNullOrWhiteSpace(request.Role)) return Results.BadRequest(new { error = "invalid_workspace_role" });
        var role = request.Role.Trim().ToLowerInvariant();
        if (!AllowedRoles.Contains(role)) return Results.BadRequest(new { error = "invalid_workspace_role" });
        if (!WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var result = await repository.ChangeRoleAsync(context.Membership.WorkspaceId, membershipId, role, CreateAuditEvent(context, "workspace.membership.role_changed", "membership", membershipId, role), cancellationToken);
        if (result == WorkspaceAccessMutationResult.NotFound) return Results.NotFound();
        if (result == WorkspaceAccessMutationResult.FinalAdministrator) return Results.Conflict(new { error = "last_customer_admin_required" });
        return Results.NoContent();
    }

    private static async Task<IResult> ChangeModulesAsync(
        Guid membershipId,
        WorkspaceMembershipModulesRequest request,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        IWorkspaceSettingsService settings,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.ModuleKeys is null) return Results.BadRequest(new { error = "module_selection_required" });
        var enabled = (await settings.GetConfigurationAsync(context, cancellationToken)).EnabledModules;
        if (!TryNormalizeGrants(request.ModuleKeys, enabled, out var moduleKeys)) return Results.BadRequest(new { error = "invalid_module_selection" });
        var owner = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole);
        if (!owner)
        {
            var actorModules = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, enabled, context.Membership.ModuleKeys);
            var target = await repository.GetMembershipAsync(context.Membership.WorkspaceId, membershipId, cancellationToken);
            if (target is null || WorkspaceModuleCatalog.IsCustomerAdministrator(target.PlatformRole)) return Results.StatusCode(StatusCodes.Status403Forbidden);
            if (moduleKeys.Except(actorModules, StringComparer.OrdinalIgnoreCase).Any()) return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var result = await repository.SetModuleGrantsAsync(context.Membership.WorkspaceId, membershipId, moduleKeys,
            CreateAuditEvent(context, "workspace.membership.modules_changed", "membership", membershipId, null), cancellationToken);
        return result == WorkspaceAccessMutationResult.NotFound ? Results.NotFound() : Results.NoContent();
    }

    private static async Task<IResult> TransferOwnershipAsync(
        WorkspaceOwnershipTransferRequest request,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        IWorkspaceSettingsService settings,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context || !WorkspaceModuleCatalog.IsOwner(accessor.Current.Membership.PlatformRole))
            return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.NewOwnerMembershipId == Guid.Empty) return Results.BadRequest(new { error = "invalid_owner" });
        var enabled = (await settings.GetConfigurationAsync(context, cancellationToken)).EnabledModules;
        var audit = CreateAuditEvent(context, "workspace.ownership.transferred", "membership", request.NewOwnerMembershipId, "workspace_owner");
        var result = await repository.TransferOwnershipAsync(context.Membership.WorkspaceId, context.User.ObjectId, request.NewOwnerMembershipId, enabled, audit, cancellationToken);
        return result == WorkspaceAccessMutationResult.NotFound ? Results.NotFound() : Results.NoContent();
    }

    private static bool TryNormalizeGrants(IReadOnlyCollection<string> requested, IReadOnlyCollection<string> enabled, out IReadOnlyCollection<string> normalized)
    {
        normalized = WorkspaceModuleCatalog.Normalize(requested);
        return requested.All(WorkspaceModuleCatalog.IsKnown)
            && normalized.Count == requested.Distinct(StringComparer.OrdinalIgnoreCase).Count()
            && normalized.Except(enabled, StringComparer.OrdinalIgnoreCase).Any() == false;
    }

    private static IReadOnlyCollection<string> ParseModuleKeys(string json)
    {
        try { return WorkspaceModuleCatalog.Normalize(JsonSerializer.Deserialize<string[]>(json)); }
        catch (JsonException) { return []; }
    }

    private static async Task<IResult> RemoveMembershipAsync(
        Guid membershipId,
        IWorkspaceContextAccessor accessor,
        IWorkspaceAccessRepository repository,
        CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole))
        {
            var target = await repository.GetMembershipAsync(context.Membership.WorkspaceId, membershipId, cancellationToken);
            if (target is null) return Results.NotFound();
            if (WorkspaceModuleCatalog.IsCustomerAdministrator(target.PlatformRole)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        }
        var result = await repository.RemoveMembershipAsync(context.Membership.WorkspaceId, membershipId, CreateAuditEvent(context, "workspace.membership.removed", "membership", membershipId, null), cancellationToken);
        if (result == WorkspaceAccessMutationResult.NotFound) return Results.NotFound();
        if (result == WorkspaceAccessMutationResult.FinalAdministrator) return Results.Conflict(new { error = "last_customer_admin_required" });
        return Results.NoContent();
    }

    private static AuditEvent CreateAuditEvent(WorkspaceContext context, string action, string targetType, Guid targetId, string? role)
    {
        return new AuditEvent
        {
            WorkspaceId = context.Membership.WorkspaceId,
            TenantId = context.User.TenantId,
            ActorTenantId = context.User.TenantId,
            ActorObjectId = context.User.ObjectId,
            Action = action,
            TargetType = targetType,
            TargetId = targetId.ToString("D"),
            Outcome = "success",
            Timestamp = DateTimeOffset.UtcNow,
            SafeMetadataJson = JsonSerializer.Serialize(new { resource = targetType, role })
        };
    }

    private static bool TryNormalizeInvitation(string? rawEmail, string? rawDisplayName, string? rawRole, out string email, out string displayName, out string role)
    {
        email = rawEmail?.Trim() ?? string.Empty;
        displayName = rawDisplayName?.Trim() ?? string.Empty;
        role = rawRole?.Trim().ToLowerInvariant() ?? string.Empty;
        try
        {
            return email.Length is > 0 and <= 320
                && new MailAddress(email).Address.Equals(email, StringComparison.OrdinalIgnoreCase)
                && displayName.Length is > 0 and <= 200
                && !displayName.Any(char.IsControl)
                && AllowedRoles.Contains(role);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string NormalizeLegacyRole(string role) => role.Equals("customeradmin", StringComparison.OrdinalIgnoreCase) ? "customer_admin" : role;
}
