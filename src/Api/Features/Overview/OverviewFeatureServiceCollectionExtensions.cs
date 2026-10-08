using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.Overview;

public static class OverviewFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddOverviewFeature(this IServiceCollection services)
    {
        services.AddSingleton<OverviewDataCache>();
        services.AddScoped<IOverviewDataReader, GraphOverviewDataReader>();
        services.AddScoped<IOverviewGraphReader, OverviewGraphReader>();
        services.AddScoped<IOverviewActivityReader, OverviewActivityReader>();
        services.AddScoped<IOverviewService, OverviewService>();
        return services;
    }
}
