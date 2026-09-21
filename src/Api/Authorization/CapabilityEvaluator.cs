using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class CapabilityEvaluator
{
    private static readonly IReadOnlySet<string> ReaderRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EntraRoleCatalog.GlobalAdministratorTemplateId,
        EntraRoleCatalog.GlobalReaderTemplateId,
        EntraRoleCatalog.UserAdministratorTemplateId
    };

    private static readonly IReadOnlyDictionary<string, CapabilityRequirement> Requirements = new Dictionary<string, CapabilityRequirement>(StringComparer.OrdinalIgnoreCase)
    {
        [Capability.UsersView] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            RoleTemplateIds: ReaderRoles,
            MissingReadState: CapabilityState.Hidden),
        [Capability.UsersCreate] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.UserCreateScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId]),
        [Capability.UsersUpdate] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.UserProfileWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId]),
        [Capability.UsersDisable] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.UserAccountWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId]),
        [Capability.UsersResetPassword] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.UserPasswordWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId]),
        [Capability.GroupsManageMembers] = new(
            ReadScopes: ["Directory.Read.All", "Group.Read.All"],
            WriteScopes: GraphScopeCatalog.GroupMembershipWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.GroupsAdministratorTemplateId]),
        [Capability.LicensesAssign] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.LicenseWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.LicenseAdministratorTemplateId]),
        [Capability.RolesAssign] = new(
            ReadScopes: ["Directory.Read.All"],
            WriteScopes: GraphScopeCatalog.RoleAndPimScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId]),
        [Capability.PimView] = new(
            ReadScopes: GraphScopeCatalog.AuthorizationReadScopes,
            RoleTemplateIds: ReaderRoles),
        [Capability.PimActivate] = new(
            ReadScopes: GraphScopeCatalog.AuthorizationReadScopes,
            WriteScopes: GraphScopeCatalog.RoleAndPimScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId])
    };

    public static CapabilitySnapshot Evaluate(GraphAuthorizationSnapshot snapshot, WorkspaceMembership workspaceMembership)
    {
        var decisions = Capability.All
            .Select(capability => EvaluateCapability(capability, snapshot, workspaceMembership))
            .ToArray();
        return new CapabilitySnapshot(
            workspaceMembership.WorkspaceId,
            DateTimeOffset.UtcNow,
            decisions,
            snapshot.IsAvailable ? "graph_authoritative" : CapabilityState.TemporarilyUnavailable,
            snapshot.ProblemCategory);
    }

    public static bool IsPlatformOnly(string capability) =>
        string.Equals(capability, Capability.WorkspaceSettingsManage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(capability, Capability.AuditView, StringComparison.OrdinalIgnoreCase);

    public static CapabilityDecision EvaluatePlatformCapability(string capability, WorkspaceMembership workspaceMembership)
    {
        if (!IsPlatformOnly(capability))
        {
            throw new ArgumentException($"Capability '{capability}' is not platform-only.", nameof(capability));
        }

        return IsWorkspaceManager(workspaceMembership)
            ? new CapabilityDecision(capability, CapabilityState.Allowed, "workspace_platform_role")
            : new CapabilityDecision(capability, CapabilityState.Hidden, "workspace_platform_role_required");
    }

    private static CapabilityDecision EvaluateCapability(string capability, GraphAuthorizationSnapshot snapshot, WorkspaceMembership workspaceMembership)
    {
        if (IsPlatformOnly(capability))
        {
            return EvaluatePlatformCapability(capability, workspaceMembership);
        }

        if (!snapshot.IsAvailable)
        {
            return snapshot.ConsentRequired
                ? new CapabilityDecision(capability, CapabilityState.ConsentRequired, "consent_required", NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"))
                : new CapabilityDecision(capability, CapabilityState.TemporarilyUnavailable, snapshot.ProblemCategory ?? "graph_snapshot_unavailable");
        }

        var requirement = Requirements[capability];
        if (!HasAnyScope(snapshot.GrantedScopes, requirement.ReadScopes))
        {
            return new CapabilityDecision(capability, requirement.MissingReadState, "directory_read_required");
        }

        var activeRoles = snapshot.DirectoryRoles
            .Where(role => string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Active, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var tenantWideActiveTemplates = activeRoles
            .Where(IsTenantWide)
            .Select(role => role.RoleTemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (capability == Capability.UsersView)
        {
            return tenantWideActiveTemplates.Overlaps(requirement.RoleTemplateIds)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "active_role")
                : new CapabilityDecision(capability, CapabilityState.Hidden, "directory_role_required", requirement.RoleTemplateIds.First());
        }

        if (tenantWideActiveTemplates.Overlaps(requirement.RoleTemplateIds))
        {
            return HasAllScopes(snapshot.GrantedScopes, requirement.WriteScopes)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "active_role")
                : new CapabilityDecision(capability, CapabilityState.ConsentRequired, "delegated_scope_required", requirement.RoleTemplateIds.First(), NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"));
        }

        var scopedMatchingRole = activeRoles.FirstOrDefault(role => requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase));
        if (scopedMatchingRole is not null)
        {
            return new CapabilityDecision(capability, CapabilityState.ReadOnly, "directory_role_scope_not_tenant_wide", scopedMatchingRole.RoleTemplateId);
        }

        var eligibleRoles = snapshot.DirectoryRoles
            .Where(role =>
            string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
            && requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        var eligibleRole = eligibleRoles.FirstOrDefault(IsTenantWide);
        var scopedEligibleRole = eligibleRoles.FirstOrDefault(role => !IsTenantWide(role));
        if (scopedEligibleRole is not null)
        {
            return new CapabilityDecision(capability, CapabilityState.ReadOnly, "directory_role_scope_not_tenant_wide", scopedEligibleRole.RoleTemplateId);
        }

        if (eligibleRole is not null)
        {
            return HasAllScopes(snapshot.GrantedScopes, requirement.WriteScopes)
                ? PimDecision(capability, eligibleRole)
                : new CapabilityDecision(capability, CapabilityState.ConsentRequired, "delegated_scope_required", requirement.RoleTemplateIds.First(), NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"));
        }

        return tenantWideActiveTemplates.Overlaps(ReaderRoles)
            ? new CapabilityDecision(capability, CapabilityState.ReadOnly, "role_read_only", requirement.RoleTemplateIds.First())
            : new CapabilityDecision(capability, CapabilityState.Hidden, "directory_role_required", requirement.RoleTemplateIds.First());
    }

    private static CapabilityDecision PimDecision(string capability, DirectoryRoleSnapshot role)
    {
        var pimState = role.Pim?.State ?? CapabilityState.TemporarilyUnavailable;
        var state = pimState switch
        {
            PimRequirement.ApprovalRequired => CapabilityState.PimApprovalRequired,
            PimRequirement.MfaRequired => CapabilityState.PimMfaRequired,
            PimRequirement.EligibilityExpired => CapabilityState.PimEligibilityExpired,
            PimRequirement.ActivationRequired => CapabilityState.PimActivationRequired,
            _ => CapabilityState.TemporarilyUnavailable
        };
        if (state == CapabilityState.TemporarilyUnavailable)
        {
            return new CapabilityDecision(
                capability,
                CapabilityState.TemporarilyUnavailable,
                "pim_status_unavailable",
                role.RoleTemplateId,
                new CapabilityPimState(pimState),
                new CapabilityNextStep("Retry after PIM status is available"));
        }

        return new CapabilityDecision(
            capability,
            state,
            state,
            role.RoleTemplateId,
            new CapabilityPimState(pimState, role.Pim?.ActivationUrl),
            new CapabilityNextStep(NextStepLabel(state), role.Pim?.ActivationUrl));
    }

    private static string NextStepLabel(string state) => state switch
    {
        CapabilityState.PimApprovalRequired => "Wait for PIM approval",
        CapabilityState.PimMfaRequired => "Complete MFA for PIM activation",
        CapabilityState.PimEligibilityExpired => "Request renewed PIM eligibility",
        _ => "Activate the required Entra role"
    };

    private static bool IsWorkspaceManager(WorkspaceMembership membership) =>
        membership.IsAteaOperator
        || string.Equals(membership.PlatformRole, "admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "workspace-manager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "owner", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyScope(IReadOnlyCollection<string> grantedScopes, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.Count == 0 || requiredScopes.Any(scope => grantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));

    private static bool HasAllScopes(IReadOnlyCollection<string> grantedScopes, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.All(scope => grantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));

    private static bool IsTenantWide(DirectoryRoleSnapshot role) =>
        string.IsNullOrWhiteSpace(role.DirectoryScopeId)
        || string.Equals(role.DirectoryScopeId, "/", StringComparison.Ordinal);

    private sealed record CapabilityRequirement(
        IReadOnlyCollection<string> ReadScopes,
        IReadOnlyCollection<string> RoleTemplateIds,
        IReadOnlyCollection<string>? WriteScopes = null,
        string MissingReadState = CapabilityState.Hidden)
    {
        public IReadOnlyCollection<string> WriteScopes { get; } = WriteScopes ?? [];
    }
}
