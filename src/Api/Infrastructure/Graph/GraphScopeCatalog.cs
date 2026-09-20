namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphScopeCatalog
{
    public static readonly IReadOnlyList<string> V1DelegatedScopes = ["User.Read"];
    public static readonly IReadOnlyList<string> DirectoryReadScopes = ["User.Read.All", "Group.Read.All", "Directory.Read.All"];
    public static readonly IReadOnlyList<string> UserCreateScopes = ["User.Create"];
    public static readonly IReadOnlyList<string> UserProfileWriteScopes = ["User.ReadWrite.All"];
    public static readonly IReadOnlyList<string> UserAccountWriteScopes = ["User.EnableDisableAccount.All", "User.Read.All"];
    public static readonly IReadOnlyList<string> UserPasswordWriteScopes = ["User-PasswordProfile.ReadWrite.All"];
    public static readonly IReadOnlyList<string> GroupMembershipWriteScopes = ["GroupMember.ReadWrite.All"];
    public static readonly IReadOnlyList<string> LicenseWriteScopes = ["LicenseAssignment.ReadWrite.All"];
    public static readonly IReadOnlyList<string> RoleAndPimScopes = ["RoleManagement.ReadWrite.Directory"];
}
