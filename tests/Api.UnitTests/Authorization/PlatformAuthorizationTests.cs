using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class PlatformAuthorizationTests
{
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherWorkspaceId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void Global_allowlist_authorizes_workspace_creation()
    {
        var authorization = new AllowlistPlatformAuthorization([OperatorId.ToString()], new Dictionary<Guid, IReadOnlySet<Guid>>());

        authorization.IsAuthorized(Principal()).Should().BeTrue();
    }

    [Fact]
    public void Workspace_scope_authorizes_only_the_configured_target()
    {
        var authorization = new AllowlistPlatformAuthorization([OperatorId.ToString()], new Dictionary<Guid, IReadOnlySet<Guid>>
        {
            [OperatorId] = new HashSet<Guid> { WorkspaceId }
        });

        authorization.CanManageWorkspace(Principal(), WorkspaceId).Should().BeTrue();
        authorization.CanManageWorkspace(Principal(), OtherWorkspaceId).Should().BeFalse();
    }

    [Fact]
    public void Missing_workspace_scope_is_not_authorized()
    {
        var authorization = new AllowlistPlatformAuthorization([OperatorId.ToString()], new Dictionary<Guid, IReadOnlySet<Guid>>());

        authorization.CanManageWorkspace(Principal(), WorkspaceId).Should().BeFalse();
    }

    [Fact]
    public void Bearer_like_principal_cannot_use_the_local_all_workspaces_claim()
    {
        var authorization = new AllowlistPlatformAuthorization([OperatorId.ToString()], new Dictionary<Guid, IReadOnlySet<Guid>>(), true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", OperatorId.ToString()), new Claim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true")],
            "Bearer"));

        authorization.GetWorkspaceScope(principal).IsAll.Should().BeFalse();
        authorization.CanManageWorkspace(principal, OtherWorkspaceId).Should().BeFalse();
    }

    [Fact]
    public void Configured_local_admin_principal_can_use_the_local_all_workspaces_gate()
    {
        var authorization = new AllowlistPlatformAuthorization([OperatorId.ToString()], new Dictionary<Guid, IReadOnlySet<Guid>>(), true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("oid", OperatorId.ToString()), new Claim(LocalAdminAuthentication.LocalAdminClaim, "true"), new Claim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true")],
            LocalAdminAuthentication.Scheme));

        authorization.GetWorkspaceScope(principal).IsAll.Should().BeTrue();
        authorization.CanManageWorkspace(principal, OtherWorkspaceId).Should().BeTrue();
    }

    private static ClaimsPrincipal Principal() => new(new ClaimsIdentity([new Claim("oid", OperatorId.ToString())], "Test"));
}
