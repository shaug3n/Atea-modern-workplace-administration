using Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;
using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.AuthenticationCampaigns;

public sealed class AuthenticationCampaignsFeatureRegistrationTests
{
    [Fact]
    public void Feature_readers_and_service_are_registered_as_scoped()
    {
        var services = new ServiceCollection();

        services.AddAuthenticationCampaignsFeature();

        services.ShouldHaveScoped<IAuthenticationCampaignsRegistrationReportReader, GraphAuthenticationCampaignsReportReader>();
        services.ShouldHaveScoped<IAuthenticationCampaignsDirectoryReader, GraphAuthenticationCampaignsDirectoryReader>();
        services.ShouldHaveScoped<IAuthenticationCampaignsService, AuthenticationCampaignsService>();
    }

    [Fact]
    public async Task Endpoint_maps_only_the_authorized_read_route_with_the_view_capability()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddScoped<IWorkspaceContextAccessor>(_ => throw new NotImplementedException());
        builder.Services.AddScoped<IAuthenticationCampaignsService>(_ => throw new NotImplementedException());
        await using var app = builder.Build();

        app.MapAuthenticationCampaignsEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .ToArray();
        var route = routes.Should().ContainSingle(endpoint =>
            endpoint.RoutePattern.RawText == "/api/authentication-campaigns/registrations").Which;

        route.Metadata.GetMetadata<IAuthorizeData>().Should().NotBeNull();
        route.Metadata.GetMetadata<RequireCapabilityAttribute>()!.Capability.Should().Be(Capability.AuthenticationCampaignsView);
        routes.Where(endpoint => endpoint.RoutePattern.RawText is not null
                && endpoint.RoutePattern.RawText.Contains("authentication-campaigns/manage", StringComparison.OrdinalIgnoreCase))
            .Should().BeEmpty();
    }
}

internal static class AuthenticationCampaignsServiceCollectionAssertions
{
    public static void ShouldHaveScoped<TService, TImplementation>(this IServiceCollection services)
        where TService : class
        where TImplementation : class, TService
    {
        var descriptor = services.SingleOrDefault(item => item.ServiceType == typeof(TService));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Equal(typeof(TImplementation), descriptor.ImplementationType);
    }
}
