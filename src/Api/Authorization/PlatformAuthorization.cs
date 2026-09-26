using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class PlatformAuthorization
{
    public const string RequiredPlatformAdminScope = "platform.admin";

    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        services.AddHttpContextAccessor();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"))
            .EnableTokenAcquisitionToCallDownstreamApi()
            .AddInMemoryTokenCaches();
        services.Configure<LocalAdminOptions>(configuration.GetSection("AteaAdmin:LocalDevelopment"));
        var localOptions = configuration.GetSection("AteaAdmin:LocalDevelopment").Get<LocalAdminOptions>() ?? new();
        LocalAdminAuthentication.ValidateEnvironment(environment, localOptions);
        if (environment.IsDevelopment())
        {
            services.AddAuthentication().AddCookie(LocalAdminAuthentication.Scheme, options =>
            {
                options.Cookie.Name = "atea-local-admin";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Strict;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            });
        }
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidAudience = configuration["AzureAd:Audience"];
        });
        services.AddAuthorization(options =>
        {
            var fallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.FallbackPolicy = fallbackPolicy;
            var platformPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("oid")
                .AddRequirements(new PlatformScopeRequirement(RequiredPlatformAdminScope));
            if (environment.IsDevelopment())
                platformPolicy.AddAuthenticationSchemes(LocalAdminAuthentication.Scheme);
            options.AddPolicy("PlatformAdminPolicy", platformPolicy.Build());
        });
        services.AddSingleton<IAuthorizationHandler, PlatformScopeAuthorizationHandler>();
        services.AddScoped<IWorkspaceMembershipReader, EfWorkspaceMembershipReader>();
        services.AddScoped<IPlatformWorkspaceGrantReader, EfPlatformWorkspaceGrantReader>();
        var scopes = configuration.GetSection("PlatformAuthorization:AdminWorkspaceScopes").GetChildren()
            .Where(section => Guid.TryParse(section.Key, out _))
            .ToDictionary(
                section => Guid.Parse(section.Key),
                section => (IReadOnlySet<Guid>)(section.Get<string[]>() ?? [])
                    .Where(value => Guid.TryParse(value, out _))
                    .Select(Guid.Parse)
                    .ToHashSet());
        var adminObjectIds = configuration.GetSection("PlatformAuthorization:AdminObjectIds").Get<string[]>() ?? [];
        if (LocalAdminAuthentication.IsConfigured(environment, localOptions)) adminObjectIds = [.. adminObjectIds, localOptions.ObjectId];
        var homeTenantId = configuration["PlatformAuthorization:HomeTenantId"];
        services.AddScoped<IPlatformAuthorization>(serviceProvider => new AllowlistPlatformAuthorization(
            homeTenantId,
            adminObjectIds,
            scopes,
            serviceProvider.GetRequiredService<IPlatformWorkspaceGrantReader>(),
            LocalAdminAuthentication.IsAllWorkspacesAllowed(environment, localOptions)));
        services.AddScoped<WorkspaceContextResolver>(serviceProvider => new WorkspaceContextResolver(
            configuration["AzureAd:Audience"] ?? string.Empty,
            serviceProvider.GetRequiredService<IWorkspaceMembershipReader>()));
        services.AddScoped<IWorkspaceContextAccessor, WorkspaceContextAccessor>();
        return services;
    }

    public static IApplicationBuilder UsePlatformAuthorization(this IApplicationBuilder application)
    {
        application.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), apiBranch =>
        {
            apiBranch.UseAuthentication();
            apiBranch.UseWhen(context => !context.Request.Path.StartsWithSegments("/api/platform"), workspaceBranch =>
                workspaceBranch.UseMiddleware<WorkspaceContextMiddleware>());
            apiBranch.UseAuthorization();
        });
        return application;
    }

    private sealed class EfWorkspaceMembershipReader(WorkplaceDbContext db) : IWorkspaceMembershipReader
    {
        public async Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default)
        {
            var membership = await db.WorkspaceMemberships.AsNoTracking()
                .Where(x => x.TenantObjectId == objectId && x.Workspace.TenantId == tenantId)
                .Select(x => new { x.WorkspaceId, x.Workspace.DisplayName, x.PlatformRole, x.IsAteaOperator, x.ModuleGrantsJson })
                .SingleOrDefaultAsync(cancellationToken);
            if (membership is null) return null;
            IReadOnlyCollection<string> moduleKeys;
            try { moduleKeys = System.Text.Json.JsonSerializer.Deserialize<string[]>(membership.ModuleGrantsJson) ?? []; }
            catch (System.Text.Json.JsonException) { moduleKeys = []; }
            return new WorkspaceMembership(membership.WorkspaceId, membership.DisplayName, membership.PlatformRole, membership.IsAteaOperator, moduleKeys);
        }
    }
}

public sealed record PlatformScopeRequirement(string RequiredScope) : IAuthorizationRequirement;

public sealed class PlatformScopeAuthorizationHandler(IHostEnvironment environment) : AuthorizationHandler<PlatformScopeRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformScopeRequirement requirement)
    {
        var isLocalDevelopmentAdmin = environment.IsDevelopment() && context.User.Identities.Any(identity =>
            string.Equals(identity.AuthenticationType, LocalAdminAuthentication.Scheme, StringComparison.Ordinal) &&
            context.User.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true"));
        if (isLocalDevelopmentAdmin || HasScope(context.User, requirement.RequiredScope)) context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope) => principal.FindAll("scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);
}

public interface IPlatformAuthorization
{
    bool IsAuthorized(ClaimsPrincipal principal);
    Task<PlatformWorkspaceScope> GetWorkspaceScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default) =>
        Task.FromResult(GetWorkspaceScope(principal));
    Task<bool> CanManageWorkspaceAsync(ClaimsPrincipal principal, Guid workspaceId, CancellationToken cancellationToken = default) =>
        Task.FromResult(CanManageWorkspace(principal, workspaceId));

    // Kept as compatibility defaults for existing test and local adapters while all production endpoints use the async boundary.
    PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal) => new(false, new HashSet<Guid>());
    bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId) => false;
}

public sealed record PlatformWorkspaceScope(bool IsAll, IReadOnlySet<Guid> WorkspaceIds);

public sealed record PlatformOperatorIdentity(Guid TenantId, Guid ObjectId);

public static class PlatformOperatorIdentityReader
{
    public static PlatformOperatorIdentity? Read(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("tid"), out var tenantId) &&
        Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) &&
        tenantId != Guid.Empty && objectId != Guid.Empty
            ? new PlatformOperatorIdentity(tenantId, objectId)
            : null;
}

public interface IPlatformWorkspaceGrantReader
{
    Task<IReadOnlySet<Guid>> GetWorkspaceIdsAsync(Guid operatorTenantId, Guid operatorObjectId, CancellationToken cancellationToken = default);
}

public sealed class EfPlatformWorkspaceGrantReader(WorkplaceDbContext db) : IPlatformWorkspaceGrantReader
{
    public async Task<IReadOnlySet<Guid>> GetWorkspaceIdsAsync(Guid operatorTenantId, Guid operatorObjectId, CancellationToken cancellationToken = default) =>
        (await db.PlatformWorkspaceGrants.AsNoTracking()
            .Where(grant => grant.OperatorTenantId == operatorTenantId && grant.OperatorObjectId == operatorObjectId)
            .Select(grant => grant.WorkspaceId)
            .ToListAsync(cancellationToken))
        .ToHashSet();
}

public sealed class AllowlistPlatformAuthorization : IPlatformAuthorization
{
    private readonly Guid? homeTenantId;
    private readonly HashSet<Guid> allowedObjectIds;
    private readonly IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> workspaceScopes;
    private readonly IPlatformWorkspaceGrantReader grantReader;
    private readonly bool localAllWorkspacesAllowed;

    public AllowlistPlatformAuthorization(
        string? homeTenantId,
        IEnumerable<string> allowedObjectIds,
        IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> workspaceScopes,
        IPlatformWorkspaceGrantReader grantReader,
        bool localAllWorkspacesAllowed = false)
    {
        this.homeTenantId = Guid.TryParse(homeTenantId, out var parsedTenantId) && parsedTenantId != Guid.Empty ? parsedTenantId : null;
        this.allowedObjectIds = allowedObjectIds.Where(x => Guid.TryParse(x, out _)).Select(Guid.Parse).ToHashSet();
        this.workspaceScopes = workspaceScopes;
        this.grantReader = grantReader;
        this.localAllWorkspacesAllowed = localAllWorkspacesAllowed;
    }

    public bool IsAuthorized(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) || !allowedObjectIds.Contains(objectId)) return false;
        if (IsLocalPrincipal(principal)) return true;
        return Guid.TryParse(principal.FindFirstValue("tid"), out var tenantId) &&
            homeTenantId is not null && tenantId == homeTenantId && HasScope(principal, PlatformAuthorization.RequiredPlatformAdminScope);
    }

    public async Task<PlatformWorkspaceScope> GetWorkspaceScopeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        if (!IsAuthorized(principal) || !Guid.TryParse(principal.FindFirstValue("oid"), out var objectId)) return EmptyScope();
        if (IsLocalAllWorkspacesPrincipal(principal)) return new(true, new HashSet<Guid>());
        var scopes = workspaceScopes.TryGetValue(objectId, out var recoveryScopes)
            ? recoveryScopes.ToHashSet()
            : new HashSet<Guid>();
        if (Guid.TryParse(principal.FindFirstValue("tid"), out var tenantId))
            scopes.UnionWith(await grantReader.GetWorkspaceIdsAsync(tenantId, objectId, cancellationToken));
        return new(false, scopes);
    }

    public async Task<bool> CanManageWorkspaceAsync(ClaimsPrincipal principal, Guid workspaceId, CancellationToken cancellationToken = default)
    {
        var scope = await GetWorkspaceScopeAsync(principal, cancellationToken);
        return scope.IsAll || scope.WorkspaceIds.Contains(workspaceId);
    }

    private bool IsLocalAllWorkspacesPrincipal(ClaimsPrincipal principal) =>
        localAllWorkspacesAllowed &&
        IsLocalPrincipal(principal) &&
        principal.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true") &&
        principal.HasClaim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true");

    private static bool IsLocalPrincipal(ClaimsPrincipal principal) =>
        principal.Identity?.AuthenticationType == LocalAdminAuthentication.Scheme &&
        principal.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true");

    private static bool HasScope(ClaimsPrincipal principal, string requiredScope) => principal.FindAll("scp")
        .SelectMany(claim => claim.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        .Contains(requiredScope, StringComparer.OrdinalIgnoreCase);

    private static PlatformWorkspaceScope EmptyScope() => new(false, new HashSet<Guid>());
}
