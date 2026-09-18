namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public static class GraphScopeCatalog
{
    // Task 4 only verifies the signed-in delegated connection. Feature-specific scopes are deferred to Task 5.
    public static readonly IReadOnlyList<string> V1DelegatedScopes = ["User.Read"];
}
