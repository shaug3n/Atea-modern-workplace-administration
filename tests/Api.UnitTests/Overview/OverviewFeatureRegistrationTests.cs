using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewFeatureRegistrationTests
{
    [Fact]
    public void AddOverviewFeature_preserves_existing_service_lifetimes()
    {
        var services = new ServiceCollection();

        services.AddOverviewFeature();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(OverviewDataCache)
            && descriptor.ImplementationType == typeof(OverviewDataCache)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOverviewDataReader)
            && descriptor.ImplementationType == typeof(GraphOverviewDataReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOverviewService)
            && descriptor.ImplementationType == typeof(OverviewService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
