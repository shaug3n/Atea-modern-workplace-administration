using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public static class AuthenticationCampaignsFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddAuthenticationCampaignsFeature(this IServiceCollection services)
    {
        services.AddScoped<IAuthenticationCampaignsRegistrationReportReader, GraphAuthenticationCampaignsReportReader>();
        services.AddScoped<IAuthenticationCampaignsDirectoryReader, GraphAuthenticationCampaignsDirectoryReader>();
        services.AddScoped<IAuthenticationCampaignsService, AuthenticationCampaignsService>();
        return services;
    }
}
