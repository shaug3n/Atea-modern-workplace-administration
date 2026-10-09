using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.PlatformAbout;

public static class PlatformAboutFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddPlatformAboutFeature(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(_ => new PlatformBuildMetadata(
                ReadBuildValue(configuration["AteaBuild:ProductVersion"]),
                ReadBuildValue(configuration["AteaBuild:Commit"]),
                ReadBuildValue(configuration["AteaBuild:Branch"])));
        return services;
    }

    private static string ReadBuildValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unavailable" : value;
}
