using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public static class FeedbackFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddFeedbackFeature(this IServiceCollection services)
    {
        services.AddScoped<IFeedbackService, FeedbackService>();
        return services;
    }
}
