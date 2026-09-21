namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class Capability
{
    public const string UsersView = "users.view";
    public const string UsersCreate = "users.create";
    public const string UsersUpdate = "users.update";
    public const string UsersDisable = "users.disable";
    public const string UsersResetPassword = "users.reset_password";
    public const string GroupsManageMembers = "groups.manage_members";
    public const string LicensesAssign = "licenses.assign";
    public const string RolesAssign = "roles.assign";
    public const string PimView = "pim.view";
    public const string PimActivate = "pim.activate";
    public const string AuditView = "audit.view";
    public const string WorkspaceSettingsManage = "workspace.settings.manage";

    public static readonly IReadOnlyList<string> All =
    [
        UsersView,
        UsersCreate,
        UsersUpdate,
        UsersDisable,
        UsersResetPassword,
        GroupsManageMembers,
        LicensesAssign,
        RolesAssign,
        PimView,
        PimActivate,
        AuditView,
        WorkspaceSettingsManage
    ];
}

public static class CapabilityState
{
    public const string Allowed = "allowed";
    public const string ReadOnly = "read_only";
    public const string Hidden = "hidden";
    public const string Disabled = "disabled";
    public const string ConsentRequired = "consent_required";
    public const string PimActivationRequired = "pim_activation_required";
    public const string PimApprovalRequired = "pim_approval_required";
    public const string PimMfaRequired = "pim_mfa_required";
    public const string PimEligibilityExpired = "pim_eligibility_expired";
    public const string TemporarilyUnavailable = "temporarily_unavailable";
}

public static class PimRequirement
{
    public const string ActivationRequired = "activation_required";
    public const string ApprovalRequired = "approval_required";
    public const string MfaRequired = "mfa_required";
    public const string EligibilityExpired = "eligibility_expired";
}

public static class DirectoryRoleAssignmentState
{
    public const string Active = "active";
    public const string Eligible = "eligible";
}

public static class EntraRoleCatalog
{
    public const string Version = "2026-09-20";
    public const string GlobalAdministratorTemplateId = "62e90394-69f5-4237-9190-012177145e10";
    public const string GlobalReaderTemplateId = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    public const string UserAdministratorTemplateId = "fe930be7-5e62-47db-91af-98c3a49a38b1";
    public const string GroupsAdministratorTemplateId = "fdd7a751-b60b-444a-984c-02652fe8fa1c";
    public const string LicenseAdministratorTemplateId = "4d6ac14f-3453-41d0-bef9-a3e0c569773a";
    public const string PrivilegedRoleAdministratorTemplateId = "e8611ab8-c189-46e8-94e1-60213ab1f814";

    public static readonly IReadOnlyDictionary<string, string> DisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        [GlobalAdministratorTemplateId] = "Global Administrator",
        [GlobalReaderTemplateId] = "Global Reader",
        [UserAdministratorTemplateId] = "User Administrator",
        [GroupsAdministratorTemplateId] = "Groups Administrator",
        [LicenseAdministratorTemplateId] = "License Administrator",
        [PrivilegedRoleAdministratorTemplateId] = "Privileged Role Administrator"
    };
}
