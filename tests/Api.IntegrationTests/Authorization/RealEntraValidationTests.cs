using System.Net;
using System.Net.Http.Headers;
using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Authorization;

public sealed class RealEntraValidationTests
{
    private const string RunVariable = "ATEA_REAL_ENTRA_RUN";
    private const string AuthorityVariable = "ATEA_REAL_ENTRA_AUTHORITY";
    private const string TenantVariable = "ATEA_REAL_ENTRA_TENANT_ID";
    private const string ClientVariable = "ATEA_REAL_ENTRA_CLIENT_ID";
    private const string AudienceVariable = "ATEA_REAL_ENTRA_AUDIENCE";
    private const string TokenVariable = "ATEA_REAL_ENTRA_ACCESS_TOKEN";

    [RealEntraFact]
    public async Task RealEntraTokenIsAcceptedAndInvalidTokenIsRejected()
    {
        var configuration = RealEntraConfiguration.Read();
        if (configuration is null)
        {
            return;
        }

        using var factory = CreateFactory(configuration);
        using var validClient = factory.CreateClient();
        validClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", configuration.AccessToken);

        var validResponse = await validClient.GetAsync("/api/ping");

        validResponse.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        validResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        using var invalidClient = factory.CreateClient();
        invalidClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid.real.entra.token");

        var invalidResponse = await invalidClient.GetAsync("/api/ping");

        invalidResponse.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await invalidResponse.Content.ReadAsStringAsync()).Should().Be("{\"error\":\"authentication_required\"}");
    }

    private sealed class RealEntraFactAttribute : FactAttribute
    {
        public RealEntraFactAttribute()
        {
            if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "true", StringComparison.OrdinalIgnoreCase))
            {
                Skip = $"Skipped: set {RunVariable}=true to enable real Entra validation; it is network-dependent by design.";
                return;
            }

            if (new[] { AuthorityVariable, TenantVariable, ClientVariable, AudienceVariable, TokenVariable }
                .Any(variable => string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))))
            {
                Skip = $"Skipped: {RunVariable}=true is set, but {AuthorityVariable}, {TenantVariable}, {ClientVariable}, {AudienceVariable}, and {TokenVariable} are also required.";
            }
        }
    }

    private static WebApplicationFactory<Program> CreateFactory(RealEntraConfiguration configuration) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, appConfiguration) => appConfiguration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = configuration.Authority,
                ["AzureAd:TenantId"] = configuration.TenantId,
                ["AzureAd:ClientId"] = configuration.ClientId,
                ["AzureAd:Audience"] = configuration.Audience
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader, FixtureMembershipReader>();
            });
        });

    private sealed record RealEntraConfiguration(string Authority, string TenantId, string ClientId, string Audience, string AccessToken)
    {
        public static RealEntraConfiguration? Read()
        {
            var values = new[]
            {
                Environment.GetEnvironmentVariable(AuthorityVariable),
                Environment.GetEnvironmentVariable(TenantVariable),
                Environment.GetEnvironmentVariable(ClientVariable),
                Environment.GetEnvironmentVariable(AudienceVariable),
                Environment.GetEnvironmentVariable(TokenVariable)
            };
            return values.Any(string.IsNullOrWhiteSpace)
                ? null
                : new RealEntraConfiguration(values[0]!, values[1]!, values[2]!, values[3]!, values[4]!);
        }
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "real-entra-test-workspace"));
    }
}
