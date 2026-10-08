using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.Licenses.Hygiene;

public static class LicenseHygieneFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddLicenseHygieneFeature(this IServiceCollection services)
    {
        services.AddScoped<ILicenseHygieneUserReader, GraphLicenseHygieneUserReader>();
        services.AddScoped<ILicenseHygieneService, LicenseHygieneService>();
        return services;
    }
}
