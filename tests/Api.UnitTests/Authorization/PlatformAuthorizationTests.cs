using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class PlatformAuthorizationTests
{
    private static readonly Guid OperatorTenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid CustomerTenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid OperatorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherOperatorId = Guid.Parse("44444444-4444-4444-4444-444444444444");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherWorkspaceId = Guid.Parse("66666666-6666-6666-6666-666666666666");

    [Fact]
    public void Configured_test_tenant_operator_with_platform_scope_is_authorized()
    {
        var authorization = CreateAuthorization();

        authorization.IsAuthorized(Principal(OperatorTenantId, OperatorId, "openid platform.admin")).Should().BeTrue();
    }

    [Fact]
    public void Operator_with_right_object_id_but_wrong_tenant_is_rejected()
    {
        var authorization = CreateAuthorization();

        authorization.IsAuthorized(Principal(CustomerTenantId, OperatorId, "platform.admin")).Should().BeFalse();
    }

    [Fact]
    public void Operator_without_platform_scope_is_rejected()
    {
        var authorization = CreateAuthorization();

        authorization.IsAuthorized(Principal(OperatorTenantId, OperatorId, "openid profile")).Should().BeFalse();
    }

    [Fact]
    public void Hostile_required_scope_configuration_cannot_change_route_policy_or_hosted_authorization()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["AzureAd:Audience"] = "api://test-api",
            ["PlatformAuthorization:HomeTenantId"] = OperatorTenantId.ToString(),
            ["PlatformAuthorization:AdminObjectIds:0"] = OperatorId.ToString(),
            ["PlatformAuthorization:RequiredScope"] = "attacker.control"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPlatformAuthorization(configuration, new TestHostEnvironment());
        services.AddSingleton<IPlatformWorkspaceGrantReader>(new InMemoryPlatformWorkspaceGrantReader(new Dictionary<(Guid, Guid), IReadOnlySet<Guid>>()));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var policy = scope.ServiceProvider.GetRequiredService<IOptions<AuthorizationOptions>>().Value.GetPolicy("PlatformAdminPolicy");
        policy!.Requirements.OfType<PlatformScopeRequirement>().Single().RequiredScope.Should().Be("platform.admin");
        var authorization = scope.ServiceProvider.GetRequiredService<IPlatformAuthorization>();
        authorization.IsAuthorized(Principal(OperatorTenantId, OperatorId, "attacker.control")).Should().BeFalse();
        authorization.IsAuthorized(Principal(OperatorTenantId, OperatorId, "platform.admin")).Should().BeTrue();
    }

    [Fact]
    public void Tenant_pinned_customer_token_is_rejected_when_object_id_is_not_allowlisted()
    {
        var authorization = CreateAuthorization();

        authorization.IsAuthorized(Principal(OperatorTenantId, OtherOperatorId, "platform.admin")).Should().BeFalse();
    }

    [Fact]
    public async Task Workspace_scope_combines_explicit_recovery_scope_and_database_grants()
    {
        var authorization = CreateAuthorization(
            recoveryScopes: new Dictionary<Guid, IReadOnlySet<Guid>>
            {
                [OperatorId] = new HashSet<Guid> { WorkspaceId }
            },
            databaseGrants: new Dictionary<(Guid TenantId, Guid ObjectId), IReadOnlySet<Guid>>
            {
                [(OperatorTenantId, OperatorId)] = new HashSet<Guid> { OtherWorkspaceId }
            });

        var scope = await authorization.GetWorkspaceScopeAsync(Principal(OperatorTenantId, OperatorId, "platform.admin"));

        scope.IsAll.Should().BeFalse();
        scope.WorkspaceIds.Should().BeEquivalentTo([WorkspaceId, OtherWorkspaceId]);
        (await authorization.CanManageWorkspaceAsync(Principal(OperatorTenantId, OperatorId, "platform.admin"), OtherWorkspaceId)).Should().BeTrue();
    }

    [Fact]
    public async Task Existing_workspace_without_explicit_scope_is_not_visible()
    {
        var authorization = CreateAuthorization();

        var scope = await authorization.GetWorkspaceScopeAsync(Principal(OperatorTenantId, OperatorId, "platform.admin"));

        scope.IsAll.Should().BeFalse();
        scope.WorkspaceIds.Should().BeEmpty();
        (await authorization.CanManageWorkspaceAsync(Principal(OperatorTenantId, OperatorId, "platform.admin"), WorkspaceId)).Should().BeFalse();
    }

    [Fact]
    public async Task Bearer_principal_cannot_use_the_local_all_workspaces_claim()
    {
        var authorization = CreateAuthorization(localAllWorkspacesAllowed: true);
        var principal = Principal(OperatorTenantId, OperatorId, "platform.admin", new Claim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true"));

        (await authorization.GetWorkspaceScopeAsync(principal)).IsAll.Should().BeFalse();
        (await authorization.CanManageWorkspaceAsync(principal, OtherWorkspaceId)).Should().BeFalse();
    }

    [Fact]
    public async Task Configured_local_development_cookie_keeps_its_separate_all_workspace_behavior()
    {
        var authorization = CreateAuthorization(localAllWorkspacesAllowed: true);
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("oid", OperatorId.ToString()),
                new Claim(LocalAdminAuthentication.LocalAdminClaim, "true"),
                new Claim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true")
            ],
            LocalAdminAuthentication.Scheme));

        authorization.IsAuthorized(principal).Should().BeTrue();
        (await authorization.GetWorkspaceScopeAsync(principal)).IsAll.Should().BeTrue();
        (await authorization.CanManageWorkspaceAsync(principal, OtherWorkspaceId)).Should().BeTrue();
    }

    [Fact]
    public async Task Scoped_local_development_cookie_keeps_explicit_recovery_workspace_scope_without_tenant_claim()
    {
        var authorization = CreateAuthorization(
            recoveryScopes: new Dictionary<Guid, IReadOnlySet<Guid>>
            {
                [OperatorId] = new HashSet<Guid> { WorkspaceId }
            });
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("oid", OperatorId.ToString()),
                new Claim(LocalAdminAuthentication.LocalAdminClaim, "true")
            ],
            LocalAdminAuthentication.Scheme));

        authorization.IsAuthorized(principal).Should().BeTrue();
        var scope = await authorization.GetWorkspaceScopeAsync(principal);
        scope.IsAll.Should().BeFalse();
        scope.WorkspaceIds.Should().ContainSingle().Which.Should().Be(WorkspaceId);
        (await authorization.CanManageWorkspaceAsync(principal, WorkspaceId)).Should().BeTrue();
        (await authorization.CanManageWorkspaceAsync(principal, OtherWorkspaceId)).Should().BeFalse();
    }

    private static AllowlistPlatformAuthorization CreateAuthorization(
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>>? recoveryScopes = null,
        IReadOnlyDictionary<(Guid TenantId, Guid ObjectId), IReadOnlySet<Guid>>? databaseGrants = null,
        bool localAllWorkspacesAllowed = false) =>
        new(
            OperatorTenantId.ToString(),
            [OperatorId.ToString()],
            recoveryScopes ?? new Dictionary<Guid, IReadOnlySet<Guid>>(),
            new InMemoryPlatformWorkspaceGrantReader(databaseGrants ?? new Dictionary<(Guid, Guid), IReadOnlySet<Guid>>()),
            localAllWorkspacesAllowed);

    private static ClaimsPrincipal Principal(Guid tenantId, Guid objectId, string scopes, params Claim[] additionalClaims) =>
        new(new ClaimsIdentity(
            [new Claim("tid", tenantId.ToString()), new Claim("oid", objectId.ToString()), new Claim("scp", scopes), .. additionalClaims],
            "Bearer"));

    private sealed class InMemoryPlatformWorkspaceGrantReader(IReadOnlyDictionary<(Guid TenantId, Guid ObjectId), IReadOnlySet<Guid>> grants) : IPlatformWorkspaceGrantReader
    {
        public Task<IReadOnlySet<Guid>> GetWorkspaceIdsAsync(Guid operatorTenantId, Guid operatorObjectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(grants.TryGetValue((operatorTenantId, operatorObjectId), out var workspaceIds)
                ? workspaceIds
                : (IReadOnlySet<Guid>)new HashSet<Guid>());
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Api.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
