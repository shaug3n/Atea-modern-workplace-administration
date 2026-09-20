using System.Net;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class GraphErrorMapperTests
{
    public static TheoryData<HttpStatusCode, string> StatusMappings => new()
    {
        { HttpStatusCode.Unauthorized, "unauthenticated" },
        { HttpStatusCode.Forbidden, "not_authorized" },
        { HttpStatusCode.NotFound, "not_found" },
        { HttpStatusCode.Conflict, "conflict" },
        { HttpStatusCode.InternalServerError, "temporarily_unavailable" },
        { HttpStatusCode.BadGateway, "temporarily_unavailable" },
        { HttpStatusCode.ServiceUnavailable, "temporarily_unavailable" },
        { HttpStatusCode.GatewayTimeout, "temporarily_unavailable" }
    };

    [Theory]
    [MemberData(nameof(StatusMappings))]
    public void Maps_graph_status_codes_to_application_categories(HttpStatusCode statusCode, string expectedCategory)
    {
        using var response = new HttpResponseMessage(statusCode);

        var result = GraphErrorMapper.FromResponse(response);

        result.IsSuccess.Should().BeFalse();
        result.Category.Should().Be(expectedCategory);
        result.StatusCode.Should().Be((int)statusCode);
    }

    [Fact]
    public void Maps_retry_after_throttling_without_losing_retry_delay()
    {
        using var response = new HttpResponseMessage((HttpStatusCode)429);
        response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(17));

        var result = GraphErrorMapper.FromResponse(response);

        result.Category.Should().Be("throttled");
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(17));
    }

    [Fact]
    public void Maps_network_failures_to_temporarily_unavailable()
    {
        var result = GraphErrorMapper.FromException(new HttpRequestException("connection reset"));

        result.IsSuccess.Should().BeFalse();
        result.Category.Should().Be("temporarily_unavailable");
        result.StatusCode.Should().BeNull();
    }

    [Theory]
    [InlineData("Bearer error=\"insufficient_claims\", claims=\"consent_required\"", "consent_required")]
    [InlineData("Bearer error=\"interaction_required\", claims=\"mfa_required\"", "mfa_required")]
    [InlineData("Bearer error=\"insufficient_claims\", claims=\"{\\\"access_token\\\":{\\\"polids\\\":[\\\"ca-policy\\\"]}}\"", "mfa_required")]
    public void Maps_claim_challenges_to_actionable_categories(string authenticateHeader, string expectedCategory)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        response.Headers.TryAddWithoutValidation("WWW-Authenticate", authenticateHeader);

        var result = GraphErrorMapper.FromResponse(response);

        result.Category.Should().Be(expectedCategory);
    }
}
