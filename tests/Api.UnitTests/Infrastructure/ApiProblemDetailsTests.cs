using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure;

public sealed class ApiProblemDetailsTests
{
    public static TheoryData<string, int, string> StableMappings => new()
    {
        { ApiProblemCode.AuthenticationRequired, StatusCodes.Status401Unauthorized, "Authentication required" },
        { ApiProblemCode.AuthorizationDenied, StatusCodes.Status403Forbidden, "Authorization denied" },
        { ApiProblemCode.ConsentRequired, StatusCodes.Status403Forbidden, "Microsoft 365 consent required" },
        { ApiProblemCode.Throttled, StatusCodes.Status429TooManyRequests, "Request throttled" },
        { ApiProblemCode.ValidationFailed, StatusCodes.Status400BadRequest, "Validation failed" },
        { ApiProblemCode.Conflict, StatusCodes.Status409Conflict, "Conflict" },
        { ApiProblemCode.NotFound, StatusCodes.Status404NotFound, "Resource not found" },
        { ApiProblemCode.TransientGraphFailure, StatusCodes.Status503ServiceUnavailable, "Microsoft Graph temporarily unavailable" },
    };

    [Theory]
    [MemberData(nameof(StableMappings))]
    public void Maps_required_categories_to_stable_problem_details(string code, int status, string title)
    {
        var descriptor = ApiProblemDetails.Describe(code, "correlation-safe-123");

        descriptor.Status.Should().Be(status);
        descriptor.Title.Should().Be(title);
        descriptor.Extensions["code"].Should().Be(code);
        descriptor.Extensions["correlationId"].Should().Be("correlation-safe-123");
    }

    [Fact]
    public void Transient_graph_problem_never_uses_raw_graph_payload()
    {
        var descriptor = ApiProblemDetails.Describe(
            ApiProblemCode.TransientGraphFailure,
            "correlation-safe-123",
            detail: """{"error":{"message":"raw Graph detail with access_token secret"}}""");

        descriptor.Detail.Should().NotContain("raw Graph detail");
        descriptor.Detail.Should().NotContain("access_token");
        descriptor.Detail.Should().Be("The upstream Microsoft Graph request could not be completed. Retry later using the correlation ID.");
    }
}
