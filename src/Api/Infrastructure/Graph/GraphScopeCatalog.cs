namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphScopeCatalog
{
    public static readonly IReadOnlyList<string> V1DelegatedScopes = ["User.Read"];
    public static readonly IReadOnlyList<string> DirectoryReadScopes = ["User.Read.All", "Group.Read.All", "Directory.Read.All"];
    public static readonly IReadOnlyList<string> UserLifecycleWriteScopes = ["User.ReadWrite.All", "Directory.ReadWrite.All"];
    public static readonly IReadOnlyList<string> GroupMembershipWriteScopes = ["GroupMember.ReadWrite.All"];
    public static readonly IReadOnlyList<string> LicenseWriteScopes = ["LicenseAssignment.ReadWrite.All", "Directory.ReadWrite.All"];
    public static readonly IReadOnlyList<string> RoleAndPimScopes = ["RoleManagement.ReadWrite.Directory", "Directory.Read.All"];
}
