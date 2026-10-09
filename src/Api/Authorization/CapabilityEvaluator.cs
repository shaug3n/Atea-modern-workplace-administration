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

    private static readonly IReadOnlySet<string> DeviceReaderRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EntraRoleCatalog.GlobalAdministratorTemplateId,
        EntraRoleCatalog.GlobalReaderTemplateId,
        EntraRoleCatalog.IntuneAdministratorTemplateId,
        EntraRoleCatalog.CloudDeviceAdministratorTemplateId
    };

    private static readonly IReadOnlySet<string> AuthenticationMethodReaderRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EntraRoleCatalog.GlobalAdministratorTemplateId,
        EntraRoleCatalog.GlobalReaderTemplateId,
        EntraRoleCatalog.AuthenticationAdministratorTemplateId,
        EntraRoleCatalog.PrivilegedAuthenticationAdministratorTemplateId
    };

    private static readonly IReadOnlySet<string> AuthenticationCampaignReaderRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        EntraRoleCatalog.ReportsReaderTemplateId,
        EntraRoleCatalog.SecurityReaderTemplateId,
        EntraRoleCatalog.SecurityAdministratorTemplateId,
        EntraRoleCatalog.GlobalReaderTemplateId
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
        [Capability.UsersRevokeSessions] = new(
            ReadScopes: ["Directory.Read.All", "User.Read.All"],
            WriteScopes: GraphScopeCatalog.UserSessionWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.UserAdministratorTemplateId],
            MissingReadState: CapabilityState.ConsentRequired),
        [Capability.GroupsManageMembers] = new(
            ReadScopes: ["Directory.Read.All", "Group.Read.All"],
            WriteScopes: GraphScopeCatalog.GroupMembershipWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.GroupsAdministratorTemplateId]),
        [Capability.LicensesView] = new(
            ReadScopes: ["Directory.Read.All"],
            RoleTemplateIds: [.. ReaderRoles, EntraRoleCatalog.LicenseAdministratorTemplateId],
            MissingReadState: CapabilityState.Hidden),
        [Capability.LicensesHygieneView] = new(
            ReadScopes: ["Directory.Read.All"],
            RoleTemplateIds: [.. ReaderRoles, EntraRoleCatalog.LicenseAdministratorTemplateId],
            MissingReadState: CapabilityState.Hidden),
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
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId]),
        [Capability.DevicesView] = new(
            ReadScopes: GraphScopeCatalog.DeviceReadScopes,
            RoleTemplateIds: DeviceReaderRoles,
            MissingReadState: CapabilityState.ConsentRequired,
            ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
        [Capability.DevicesManage] = new(
            ReadScopes: GraphScopeCatalog.DeviceReadScopes,
            WriteScopes: GraphScopeCatalog.DeviceWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.IntuneAdministratorTemplateId, EntraRoleCatalog.CloudDeviceAdministratorTemplateId],
            MissingReadState: CapabilityState.ConsentRequired,
            ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
        [Capability.DevicesPrivilegedManage] = new(
            ReadScopes: GraphScopeCatalog.DeviceReadScopes,
            WriteScopes: GraphScopeCatalog.DevicePrivilegedOperationScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.IntuneAdministratorTemplateId, EntraRoleCatalog.CloudDeviceAdministratorTemplateId],
            MissingReadState: CapabilityState.ConsentRequired,
            ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
        [Capability.AuthenticationMethodsView] = new(
            ReadScopes: GraphScopeCatalog.AuthenticationMethodReadScopes,
            RoleTemplateIds: AuthenticationMethodReaderRoles,
            MissingReadState: CapabilityState.ConsentRequired,
            ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
        [Capability.AuthenticationMethodsManage] = new(
            ReadScopes: GraphScopeCatalog.AuthenticationMethodReadScopes,
            WriteScopes: GraphScopeCatalog.AuthenticationMethodWriteScopes,
            RoleTemplateIds: [EntraRoleCatalog.GlobalAdministratorTemplateId, EntraRoleCatalog.AuthenticationAdministratorTemplateId, EntraRoleCatalog.PrivilegedAuthenticationAdministratorTemplateId],
            MissingReadState: CapabilityState.ConsentRequired,
            ReadOnlyRoleTemplateIds: [EntraRoleCatalog.GlobalReaderTemplateId]),
        [Capability.AuthenticationCampaignsView] = new(
            ReadScopes: GraphScopeCatalog.AuthenticationCampaignReportScopes,
            RoleTemplateIds: AuthenticationCampaignReaderRoles,
            MissingReadState: CapabilityState.ConsentRequired)
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
        || string.Equals(capability, Capability.WorkspaceMembersManage, StringComparison.OrdinalIgnoreCase)
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

        if (IsRecoveryCapability(capability))
        {
            return EvaluateRecoveryCapability(capability, snapshot);
        }

        var requirement = Requirements[capability];
        if (capability == Capability.PimActivate)
        {
            // PIM self-activation is intentionally based on the user's own
            // tenant-wide eligibility, not on already-active Global Admin or
            // Privileged Role Administrator access. An eligible Global Reader
            // must be able to activate that role before the read-only modules
            // can evaluate their tenant access.
            var pimEligibleRole = snapshot.DirectoryRoles.FirstOrDefault(role =>
                string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
                && IsTenantWide(role)
                && role.Pim is not null);
            if (pimEligibleRole is not null)
            {
                if (!HasAllScopes(snapshot, requirement.WriteScopes))
                {
                    return new CapabilityDecision(
                        capability,
                        CapabilityState.ConsentRequired,
                        "delegated_scope_required",
                        pimEligibleRole.RoleTemplateId,
                        NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"),
                        MissingScopes: MissingScopes(snapshot, requirement.WriteScopes));
                }

                return PimDecision(capability, pimEligibleRole);
            }
        }

        if (!HasAnyScope(snapshot, requirement.ReadScopes))
        {
            var missingReadScopes = MissingScopes(snapshot, requirement.ReadScopes.Concat(requirement.WriteScopes));
            if (HasTransientScopeProblem(snapshot, missingReadScopes))
            {
                return ScopeProbeUnavailableDecision(capability, requirement, missingReadScopes);
            }

            return requirement.MissingReadState == CapabilityState.ConsentRequired
                ? new CapabilityDecision(
                    capability,
                    CapabilityState.ConsentRequired,
                    "delegated_scope_required",
                    NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"),
                    MissingScopes: missingReadScopes)
                : new CapabilityDecision(capability, requirement.MissingReadState, "directory_read_required");
        }

        var activeRoles = snapshot.DirectoryRoles
            .Where(role => string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Active, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var tenantWideActiveTemplates = activeRoles
            .Where(IsTenantWide)
            .Select(role => role.RoleTemplateId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (capability == Capability.AuthenticationCampaignsView)
        {
            if (tenantWideActiveTemplates.Overlaps(requirement.RoleTemplateIds))
            {
                return new CapabilityDecision(capability, CapabilityState.Allowed, "active_role");
            }

            var scopedActiveRole = activeRoles.FirstOrDefault(role =>
                requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase)
                && !IsTenantWide(role));
            if (scopedActiveRole is not null)
            {
                return new CapabilityDecision(
                    capability,
                    CapabilityState.Hidden,
                    "directory_role_scope_not_tenant_wide",
                    scopedActiveRole.RoleTemplateId);
            }

            var campaignEligibleRoles = snapshot.DirectoryRoles
                .Where(role =>
                    string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
                    && requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase))
                .ToArray();
            var tenantWideEligibleRole = campaignEligibleRoles.FirstOrDefault(IsTenantWide);
            if (tenantWideEligibleRole is not null)
            {
                return PimDecision(capability, tenantWideEligibleRole);
            }

            var campaignScopedEligibleRole = campaignEligibleRoles.FirstOrDefault(role => !IsTenantWide(role));
            return campaignScopedEligibleRole is not null
                ? new CapabilityDecision(
                    capability,
                    CapabilityState.Hidden,
                    "directory_role_scope_not_tenant_wide",
                    campaignScopedEligibleRole.RoleTemplateId)
                : new CapabilityDecision(
                    capability,
                    CapabilityState.Hidden,
                    "directory_role_required",
                    requirement.RoleTemplateIds.First());
        }

        if (capability == Capability.UsersView)
        {
            if (tenantWideActiveTemplates.Overlaps(requirement.RoleTemplateIds))
            {
                return new CapabilityDecision(capability, CapabilityState.Allowed, "active_role");
            }

            var usersEligibleRole = snapshot.DirectoryRoles.FirstOrDefault(role =>
                string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
                && requirement.RoleTemplateIds.Contains(role.RoleTemplateId, StringComparer.OrdinalIgnoreCase)
                && IsTenantWide(role));
            return usersEligibleRole is not null
                ? PimDecision(capability, usersEligibleRole)
                : new CapabilityDecision(capability, CapabilityState.Hidden, "directory_role_required", requirement.RoleTemplateIds.First());
        }

        if (tenantWideActiveTemplates.Overlaps(requirement.RoleTemplateIds))
        {
            return HasAllScopes(snapshot, requirement.WriteScopes)
                ? new CapabilityDecision(capability, CapabilityState.Allowed, "active_role")
                : MissingScopeDecision(snapshot, capability, requirement, MissingScopes(snapshot, requirement.WriteScopes));
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
            return HasAllScopes(snapshot, requirement.WriteScopes)
                ? PimDecision(capability, eligibleRole)
                : MissingScopeDecision(snapshot, capability, requirement, MissingScopes(snapshot, requirement.WriteScopes));
        }

        return tenantWideActiveTemplates.Overlaps(requirement.ReadOnlyRoleTemplateIds)
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
            new CapabilityNextStep(NextStepLabel(state), "/identity"));
    }

    private static bool IsRecoveryCapability(string capability) => capability is
        Capability.DevicesBitlockerMetadata or Capability.DevicesBitlockerReveal or
        Capability.DevicesLapsMetadata or Capability.DevicesLapsReveal;

    private static CapabilityDecision EvaluateRecoveryCapability(string capability, GraphAuthorizationSnapshot snapshot)
    {
        var scopes = capability switch
        {
            Capability.DevicesBitlockerMetadata => new[] { "BitlockerKey.ReadBasic.All", "BitlockerKey.Read.All" },
            Capability.DevicesBitlockerReveal => ["BitlockerKey.Read.All"],
            Capability.DevicesLapsMetadata => ["DeviceLocalCredential.ReadBasic.All", "DeviceLocalCredential.Read.All"],
            _ => ["DeviceLocalCredential.Read.All"]
        };
        if (!HasAnyScope(snapshot, scopes))
        {
            var missing = MissingScopes(snapshot, scopes);
            return HasTransientScopeProblem(snapshot, missing)
                ? new CapabilityDecision(capability, CapabilityState.TemporarilyUnavailable, "scope_probe_unavailable",
                    NextStep: new CapabilityNextStep("Retry authorization checks"), MissingScopes: missing)
                : new CapabilityDecision(capability, CapabilityState.ConsentRequired, "delegated_scope_required",
                    NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"), MissingScopes: missing);
        }

        // Role snapshots cannot prove device ownership, custom roles, or administrative-unit scope.
        // A granted delegated scope permits an attempt; Graph decides access to the target.
        var eligible = snapshot.DirectoryRoles.FirstOrDefault(role =>
            string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Eligible, StringComparison.OrdinalIgnoreCase)
            && EntraRoleCatalog.SupportsRecoveryOperation(capability, role.RoleTemplateId));
        return new CapabilityDecision(capability, CapabilityState.Allowed, "graph_authoritative",
            RequiredRoleTemplateId: eligible?.RoleTemplateId,
            Pim: eligible?.Pim is { } pim ? new CapabilityPimState(pim.State, pim.ActivationUrl) : null,
            NextStep: eligible is null ? null : new CapabilityNextStep(
                $"Activate eligible {EntraRoleCatalog.DisplayNames[eligible.RoleTemplateId]} role in PIM if Graph denies access", "/identity"));
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
        || string.Equals(membership.PlatformRole, "customer_admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "customeradmin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "workspace-manager", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(membership.PlatformRole, "workspace_owner", StringComparison.OrdinalIgnoreCase);

    private static bool HasAnyScope(GraphAuthorizationSnapshot snapshot, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.Count == 0 || requiredScopes.Any(scope => IsScopeAvailable(snapshot, scope));

    private static bool HasAllScopes(GraphAuthorizationSnapshot snapshot, IReadOnlyCollection<string> requiredScopes) =>
        requiredScopes.All(scope => IsScopeAvailable(snapshot, scope));

    private static CapabilityDecision MissingScopeDecision(
        GraphAuthorizationSnapshot snapshot,
        string capability,
        CapabilityRequirement requirement,
        IReadOnlyCollection<string> missingScopes)
    {
        return HasTransientScopeProblem(snapshot, missingScopes)
            ? ScopeProbeUnavailableDecision(capability, requirement, missingScopes)
            : new CapabilityDecision(
                capability,
                CapabilityState.ConsentRequired,
                "delegated_scope_required",
                requirement.RoleTemplateIds.First(),
                NextStep: new CapabilityNextStep("Grant delegated consent", "/api/workspaces/current/consent/start"),
                MissingScopes: missingScopes);
    }

    private static CapabilityDecision ScopeProbeUnavailableDecision(
        string capability,
        CapabilityRequirement requirement,
        IReadOnlyCollection<string> missingScopes) =>
        new(
            capability,
            CapabilityState.TemporarilyUnavailable,
            "scope_probe_unavailable",
            requirement.RoleTemplateIds.First(),
            NextStep: new CapabilityNextStep("Retry authorization checks"),
            MissingScopes: missingScopes);

    private static bool HasTransientScopeProblem(GraphAuthorizationSnapshot snapshot, IEnumerable<string> scopes) =>
        snapshot.ScopeProblems is not null
        && scopes.Any(scope => snapshot.ScopeProblems.TryGetValue(scope, out var problem)
            && !string.Equals(problem, "consent_required", StringComparison.OrdinalIgnoreCase));

    private static IReadOnlyCollection<string> MissingScopes(GraphAuthorizationSnapshot snapshot, IEnumerable<string> requiredScopes) =>
        requiredScopes.Where(scope => !IsScopeAvailable(snapshot, scope)).ToArray();

    private static bool IsScopeAvailable(GraphAuthorizationSnapshot snapshot, string requiredScope)
    {
        // A populated availability map comes from the OBO scope probes. It is
        // authoritative, including for scopes that have not been consented. The
        // fallback keeps hand-built/unit-test snapshots compatible.
        if (snapshot.ScopeAvailability is not null)
        {
            return snapshot.ScopeAvailability.TryGetValue(requiredScope, out var available) && available;
        }

        return snapshot.GrantedScopes.Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsTenantWide(DirectoryRoleSnapshot role) =>
        string.IsNullOrWhiteSpace(role.DirectoryScopeId)
        || string.Equals(role.DirectoryScopeId, "/", StringComparison.Ordinal);

    private sealed record CapabilityRequirement(
        IReadOnlyCollection<string> ReadScopes,
        IReadOnlyCollection<string> RoleTemplateIds,
        IReadOnlyCollection<string>? WriteScopes = null,
        string MissingReadState = CapabilityState.Hidden,
        IReadOnlyCollection<string>? ReadOnlyRoleTemplateIds = null)
    {
        public IReadOnlyCollection<string> WriteScopes { get; } = WriteScopes ?? [];
        public IReadOnlyCollection<string> ReadOnlyRoleTemplateIds { get; } = ReadOnlyRoleTemplateIds ?? ReaderRoles;
    }
}
