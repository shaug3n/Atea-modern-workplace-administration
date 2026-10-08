using Microsoft.Extensions.DependencyInjection;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;

namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public static class FeedbackFeatureServiceCollectionExtensions
{
    public static IServiceCollection AddFeedbackFeature(this IServiceCollection services)
    {
        services.AddScoped<IFeedbackService, FeedbackService>();
        services.AddScoped<IFeedbackRetentionService, FeedbackRetentionService>();
        services.AddHostedService<FeedbackRetentionWorker>();
        return services;
    }
}
