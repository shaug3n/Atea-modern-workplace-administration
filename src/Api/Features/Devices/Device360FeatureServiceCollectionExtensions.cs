using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.Devices;

public static class Device360FeatureServiceCollectionExtensions
{
    public static IServiceCollection AddDevice360Feature(this IServiceCollection services)
    {
        services.AddScoped<IDevice360GraphReader, GraphDevice360Reader>();
        services.AddScoped<IDevice360Service, Device360Service>();
        return services;
    }
}
