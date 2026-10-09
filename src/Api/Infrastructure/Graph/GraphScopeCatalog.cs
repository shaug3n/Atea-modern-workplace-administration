namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphScopeCatalog
{
    public static readonly IReadOnlyList<string> V1DelegatedScopes = ["User.Read"];
    public static readonly IReadOnlyList<string> DirectoryReadScopes = ["User.Read.All", "Group.Read.All", "Directory.Read.All"];
    public static readonly IReadOnlyList<string> UserCreateScopes = ["User.Create"];
    public static readonly IReadOnlyList<string> UserProfileWriteScopes = ["User.ReadWrite.All"];
    public static readonly IReadOnlyList<string> UserAccountWriteScopes = ["User.EnableDisableAccount.All", "User.Read.All"];
    public static readonly IReadOnlyList<string> UserPasswordWriteScopes = ["User-PasswordProfile.ReadWrite.All"];
    public static readonly IReadOnlyList<string> UserSessionWriteScopes = ["User.RevokeSessions.All"];
    public static readonly IReadOnlyList<string> GroupMembershipWriteScopes = ["GroupMember.ReadWrite.All"];
    public static readonly IReadOnlyList<string> LicenseWriteScopes = ["LicenseAssignment.ReadWrite.All"];
    public static readonly IReadOnlyList<string> RoleAndPimScopes = ["RoleManagement.ReadWrite.Directory"];
    public static readonly IReadOnlyList<string> DeviceReadScopes = ["DeviceManagementManagedDevices.Read.All"];
    public static readonly IReadOnlyList<string> DeviceConfigurationReadScopes = ["DeviceManagementConfiguration.Read.All"];
    public static readonly IReadOnlyList<string> DeviceWriteScopes = ["DeviceManagementManagedDevices.ReadWrite.All"];
    public static readonly IReadOnlyList<string> DevicePrivilegedOperationScopes = ["DeviceManagementManagedDevices.PrivilegedOperations.All"];
    public static readonly IReadOnlyList<string> BitlockerMetadataScopes = ["BitlockerKey.ReadBasic.All"];
    public static readonly IReadOnlyList<string> BitlockerSecretScopes = ["BitlockerKey.Read.All"];
    public static readonly IReadOnlyList<string> LapsMetadataScopes = ["DeviceLocalCredential.ReadBasic.All"];
    public static readonly IReadOnlyList<string> LapsSecretScopes = ["DeviceLocalCredential.Read.All"];
    public static readonly IReadOnlyList<string> AuthenticationMethodReadScopes = ["UserAuthenticationMethod.Read.All"];
    public static readonly IReadOnlyList<string> AuthenticationMethodWriteScopes = ["UserAuthenticationMethod.ReadWrite.All"];
    public static readonly IReadOnlyList<string> AuthenticationCampaignReportScopes = ["AuditLog.Read.All"];
    public static readonly IReadOnlyList<string> DirectoryProfileReadScopes = ["User.Read.All"];
    public static readonly IReadOnlyList<string> ExchangeMailboxSettingsReadScopes = ["MailboxSettings.Read"];
    public static readonly IReadOnlyList<string> AuthorizationReadScopes = ["Directory.Read.All", "RoleManagement.Read.Directory"];

    // These are the known Graph scopes used by capability evaluation. The
    // authorization reader probes them individually so an optional unconsented
    // feature does not make the entire authorization snapshot unavailable.
    public static readonly IReadOnlyList<string> CapabilityEvaluationScopes =
    [
        "User.Read",
        "User.Read.All",
        "Group.Read.All",
        "Directory.Read.All",
        "User.Create",
        "User.ReadWrite.All",
        "User.EnableDisableAccount.All",
        "User-PasswordProfile.ReadWrite.All",
        "User.RevokeSessions.All",
        "GroupMember.ReadWrite.All",
        "LicenseAssignment.ReadWrite.All",
        "RoleManagement.Read.Directory",
        "RoleManagement.ReadWrite.Directory",
        "DeviceManagementManagedDevices.Read.All",
        "DeviceManagementConfiguration.Read.All",
        "DeviceManagementManagedDevices.ReadWrite.All",
        "DeviceManagementManagedDevices.PrivilegedOperations.All",
        "BitlockerKey.ReadBasic.All",
        "BitlockerKey.Read.All",
        "DeviceLocalCredential.ReadBasic.All",
        "DeviceLocalCredential.Read.All",
        "UserAuthenticationMethod.Read.All",
        "UserAuthenticationMethod.ReadWrite.All",
        "AuditLog.Read.All",
        "MailboxSettings.Read"
    ];
}
