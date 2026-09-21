using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Authorization;

public sealed class JwtValidationTests
{
    private static readonly RSA SigningRsa = RSA.Create(2048);
    private const string Issuer = "https://issuer.test/organizations/v2.0";
    private const string Audience = "api://atea-unified-workplace-api";

    [Fact]
    public async Task ValidProductionBearerTokenCanReadSession()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken());

        var response = await client.GetAsync("/api/session");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
    }

    [Theory]
    [InlineData("audience")]
    [InlineData("issuer")]
    [InlineData("expired")]
    [InlineData("signature")]
    public async Task InvalidProductionBearerTokenReturnsStructured401(string failure)
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", CreateToken(failure));

        var response = await client.GetAsync("/api/session");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        document.RootElement.GetProperty("code").GetString().Should().Be("authentication_required");
    }

    private static WebApplicationFactory<Program> CreateFactory() =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Audience"] = Audience,
                ["AzureAd:ClientId"] = "test-client-id"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IWorkspaceMembershipReader>();
                services.AddSingleton<IWorkspaceMembershipReader, FixtureMembershipReader>();
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null!;
                    options.MetadataAddress = null!;
                    options.ConfigurationManager = null!;
                    options.MapInboundClaims = false;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuer = Issuer,
                        IssuerValidator = (issuer, _, _) => string.Equals(issuer, Issuer, StringComparison.Ordinal)
                            ? issuer
                            : throw new SecurityTokenInvalidIssuerException(),
                        ValidateAudience = true,
                        ValidAudience = Audience,
                        ValidateLifetime = true,
                        RequireExpirationTime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new RsaSecurityKey(SigningRsa)
                    };
                });
            });
        });

    private static string CreateToken(string? failure = null)
    {
        var signingKey = SigningRsa;
        var tokenIssuer = Issuer;
        var tokenAudience = Audience;
        var expires = DateTime.UtcNow.AddMinutes(5);
        if (failure == "issuer") tokenIssuer = "https://wrong-issuer.test";
        if (failure == "audience") tokenAudience = "api://wrong-audience";
        if (failure == "expired") expires = DateTime.UtcNow.AddMinutes(-5);
        if (failure == "signature") signingKey = RSA.Create(2048);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = tokenIssuer,
            Audience = tokenAudience,
            Expires = expires,
            NotBefore = failure == "expired" ? DateTime.UtcNow.AddMinutes(-10) : DateTime.UtcNow.AddMinutes(-1),
            Subject = new ClaimsIdentity(new[]
            {
                new Claim("tid", "11111111-1111-1111-1111-111111111111"),
                new Claim("oid", "22222222-2222-2222-2222-222222222222"),
                new Claim("preferred_username", "alex@example.com"),
                new Claim("name", "Alex Example"),
                new Claim("userType", "Member"),
                new Claim("scp", "access_as_user")
            }),
            SigningCredentials = new SigningCredentials(new RsaSecurityKey(signingKey), SecurityAlgorithms.RsaSha256)
        };
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityTokenHandler().CreateToken(descriptor));
    }

    private sealed class FixtureMembershipReader : IWorkspaceMembershipReader
    {
        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult<WorkspaceMembership?>(new WorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "customer-workspace"));
    }
}
