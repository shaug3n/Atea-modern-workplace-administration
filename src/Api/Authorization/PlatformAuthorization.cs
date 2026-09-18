using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class PlatformAuthorization
{
    public static IServiceCollection AddPlatformAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpContextAccessor();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(configuration.GetSection("AzureAd"));
        services.AddAuthorization();
        services.AddSingleton<IWorkspaceMembershipReader, EmptyWorkspaceMembershipReader>();
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
            apiBranch.UseMiddleware<WorkspaceContextMiddleware>();
            apiBranch.UseAuthorization();
        });
        return application;
    }

    private sealed class EmptyWorkspaceMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(null);
    }
}
