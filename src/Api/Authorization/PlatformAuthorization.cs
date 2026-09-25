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
    private const string DefaultRequiredScope = "platform.admin";

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
            var requiredScope = configuration["PlatformAuthorization:RequiredScope"];
            if (string.IsNullOrWhiteSpace(requiredScope)) requiredScope = DefaultRequiredScope;
            var fallbackPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .Build();
            options.FallbackPolicy = fallbackPolicy;
            var platformPolicy = new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser()
                .RequireClaim("oid")
                .AddRequirements(new PlatformScopeRequirement(requiredScope));
            if (environment.IsDevelopment())
                platformPolicy.AddAuthenticationSchemes(LocalAdminAuthentication.Scheme);
            options.AddPolicy("PlatformAdminPolicy", platformPolicy.Build());
        });
        services.AddSingleton<IAuthorizationHandler, PlatformScopeAuthorizationHandler>();
        services.AddScoped<IWorkspaceMembershipReader, EfWorkspaceMembershipReader>();
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
        services.AddSingleton<IPlatformAuthorization>(_ => new AllowlistPlatformAuthorization(adminObjectIds, scopes, LocalAdminAuthentication.IsAllWorkspacesAllowed(environment, localOptions)));
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
    PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal);
    bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId);
}

public sealed record PlatformWorkspaceScope(bool IsAll, IReadOnlySet<Guid> WorkspaceIds);

public sealed class AllowlistPlatformAuthorization(
    IEnumerable<string> allowedObjectIds,
    IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> workspaceScopes,
    bool localAllWorkspacesAllowed = false) : IPlatformAuthorization
{
    private readonly HashSet<Guid> allowedObjectIds = allowedObjectIds
        .Where(x => Guid.TryParse(x, out _))
        .Select(Guid.Parse)
        .ToHashSet();
    private readonly IReadOnlyDictionary<Guid, IReadOnlySet<Guid>> workspaceScopes = workspaceScopes;
    private readonly bool localAllWorkspacesAllowed = localAllWorkspacesAllowed;

    public bool IsAuthorized(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) && allowedObjectIds.Contains(objectId);

    public PlatformWorkspaceScope GetWorkspaceScope(ClaimsPrincipal principal)
    {
        if (!Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) || !allowedObjectIds.Contains(objectId)) return new(false, new HashSet<Guid>());
        if (IsLocalAllWorkspacesPrincipal(principal)) return new(true, new HashSet<Guid>());
        return new(false, workspaceScopes.TryGetValue(objectId, out var scopes) ? scopes : new HashSet<Guid>());
    }

    public bool CanManageWorkspace(ClaimsPrincipal principal, Guid workspaceId) =>
        Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) &&
        allowedObjectIds.Contains(objectId) &&
        (IsLocalAllWorkspacesPrincipal(principal) ||
         (workspaceScopes.TryGetValue(objectId, out var scopes) && scopes.Contains(workspaceId)));

    private bool IsLocalAllWorkspacesPrincipal(ClaimsPrincipal principal) =>
        localAllWorkspacesAllowed &&
        principal.Identity?.AuthenticationType == LocalAdminAuthentication.Scheme &&
        principal.HasClaim(LocalAdminAuthentication.LocalAdminClaim, "true") &&
        principal.HasClaim(LocalAdminAuthentication.AllowAllWorkspacesClaim, "true");
}
