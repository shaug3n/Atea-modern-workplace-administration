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

    private static CapabilityDecision EvaluateCapability(string capability, GraphAuthorizationSnapshot snapshot, WorkspaceMembership workspaceMembership)
    {
        if (capability == Capability.WorkspaceSettingsManage)
        {
            return IsWorkspaceManager(workspaceMembership)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "workspace_platform_role")
                : new CapabilityDecision(capability, CapabilityState.Hidden, "workspace_platform_role_required");
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

        var activeTemplates = snapshot.DirectoryRoles
            .Where(role => string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Active, StringComparison.OrdinalIgnoreCase))
            .Select(role => role.RoleTemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (capability == Capability.UsersView)
        {
            return activeTemplates.Overlaps(requirement.RoleTemplateIds)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "active_role")
                : new CapabilityDecision(capability, CapabilityState.Hidden, "directory_role_required", requirement.RoleTemplateIds.First());
        }

        if (activeTemplates.Overlaps(requirement.RoleTemplateIds))
        {
            return HasAllScopes(snapshot.GrantedScopes, requirement.WriteScopes)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "active_role")
                : new CapabilityDecision(capability, CapabilityState.ConsentRequired, "delegated_scope_required", requirement.RoleTemplateIds.First(), NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"));
        }

        var eligibleRole = snapshot.DirectoryRoles.FirstOrDefault(role =>
            string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
            && requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase));
        if (eligibleRole is not null)
        {
            return HasAllScopes(snapshot.GrantedScopes, requirement.WriteScopes)
                ? PimDecision(capability, eligibleRole)
                : new CapabilityDecision(capability, CapabilityState.ConsentRequired, "delegated_scope_required", requirement.RoleTemplateIds.First(), NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"));
        }

        return activeTemplates.Overlaps(ReaderRoles)
            ? new CapabilityDecision(capability, CapabilityState.ReadOnly, "role_read_only", requirement.RoleTemplateIds.First())
            : new CapabilityDecision(capability, CapabilityState.Hidden, "directory_role_required", requirement.RoleTemplateIds.First());
    }

    private static CapabilityDecision PimDecision(string capability, DirectoryRoleSnapshot role)
    {
        var pimState = role.Pim?.State ?? PimRequirement.ActivationRequired;
        var state = pimState switch
        {
            PimRequirement.ApprovalRequired => CapabilityState.PimApprovalRequired,
            PimRequirement.MfaRequired => CapabilityState.PimMfaRequired,
            PimRequirement.EligibilityExpired => CapabilityState.PimEligibilityExpired,
            _ => CapabilityState.PimActivationRequired
        };
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
        || string.Equals(membership.PlatformRole, "owner", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyScope(IReadOnlyCollection<string> grantedScopes, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.Count == 0 || requiredScopes.Any(scope => grantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));

    private static bool HasAllScopes(IReadOnlyCollection<string> grantedScopes, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.All(scope => grantedScopes.Contains(scope, StringComparer.OrdinalIgnoreCase));

    private sealed record CapabilityRequirement(
        IReadOnlyCollection<string> ReadScopes,
        IReadOnlyCollection<string> RoleTemplateIds,
        IReadOnlyCollection<string>? WriteScopes = null,
        string MissingReadState = CapabilityState.Hidden)
    {
        public IReadOnlyCollection<string> WriteScopes { get; } = WriteScopes ?? [];
    }
}
