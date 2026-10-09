using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewFeatureRegistrationTests
{
    [Fact]
    public void AddOverviewFeature_registers_source_readers_and_service_with_expected_lifetimes()
    {
        var services = new ServiceCollection();

        services.AddOverviewFeature();

        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(OverviewDataCache)
            && descriptor.ImplementationType == typeof(OverviewDataCache)
            && descriptor.Lifetime == ServiceLifetime.Singleton);
        services.Should().NotContain(descriptor => descriptor.ServiceType == typeof(IOverviewDataReader));
        services.Should().NotContain(descriptor => descriptor.ImplementationType == typeof(GraphOverviewDataReader));
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOverviewGraphReader)
            && descriptor.ImplementationType == typeof(OverviewGraphReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOverviewActivityReader)
            && descriptor.ImplementationType == typeof(OverviewActivityReader)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        services.Should().ContainSingle(descriptor =>
            descriptor.ServiceType == typeof(IOverviewService)
            && descriptor.ImplementationType == typeof(OverviewService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }
}
