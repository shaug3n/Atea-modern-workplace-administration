namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphScopeCatalog
{
    public static readonly IReadOnlyList<string> V1DelegatedScopes = ["User.Read", "User.Read.All", "Group.Read.All", "Directory.Read.All", "RoleManagement.Read.Directory"];
}
