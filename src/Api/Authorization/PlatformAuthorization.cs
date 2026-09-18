using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Identity.Web;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class PlatformAuthorization
{
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters.ValidateIssuer = true;
            options.TokenValidationParameters.ValidAudience = configuration["AzureAd:Audience"];
        });
        services.AddAuthorization(options =>
        {
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });
        services.AddScoped<IWorkspaceMembershipReader, EfWorkspaceMembershipReader>();
        services.AddSingleton<IPlatformAuthorization>(_ => new AllowlistPlatformAuthorization(configuration.GetSection("PlatformAuthorization:AdminObjectIds").Get<string[]>() ?? []));
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
                .Select(x => new { x.WorkspaceId, x.Workspace.DisplayName })
                .SingleOrDefaultAsync(cancellationToken);
            return membership is null ? null : new WorkspaceMembership(membership.WorkspaceId, membership.DisplayName);
        }
    }
}

public interface IPlatformAuthorization
{
    bool IsAuthorized(ClaimsPrincipal principal);
}

public sealed class AllowlistPlatformAuthorization(IEnumerable<string> allowedObjectIds) : IPlatformAuthorization
{
    private readonly HashSet<Guid> allowedObjectIds = allowedObjectIds
        .Where(x => Guid.TryParse(x, out _))
        .Select(Guid.Parse)
        .ToHashSet();

    public bool IsAuthorized(ClaimsPrincipal principal) =>
        Guid.TryParse(principal.FindFirstValue("oid"), out var objectId) && allowedObjectIds.Contains(objectId);
}
