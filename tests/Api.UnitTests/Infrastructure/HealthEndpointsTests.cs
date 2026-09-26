using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using FluentAssertions;
using Microsoft.AspNetCore.Http;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure;

public sealed class HealthEndpointsTests
{
    [Fact]
    public async Task Database_failure_returns_generic_not_ready_result()
    {
        var result = await HealthEndpoints.CheckDatabaseReadinessAsync(_ => Task.FromException<bool>(new InvalidOperationException("db-secret.internal")), CancellationToken.None);
        var (status, body) = ReadResult(result);

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.Should().Contain("database").And.NotContain("db-secret.internal");
    }

    [Fact]
    public async Task Slow_database_probe_is_bounded_and_does_not_require_graph()
    {
        var result = await HealthEndpoints.CheckDatabaseReadinessAsync(
            async cancellationToken => { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); return true; },
            CancellationToken.None);
        var (status, body) = ReadResult(result);

        status.Should().Be(StatusCodes.Status503ServiceUnavailable);
        body.Should().Contain("unavailable");
    }

    [Fact]
    public async Task Healthy_database_returns_ready()
    {
        var result = await HealthEndpoints.CheckDatabaseReadinessAsync(_ => Task.FromResult(true), CancellationToken.None);
        var (status, body) = ReadResult(result);

        status.Should().Be(StatusCodes.Status200OK);
        body.Should().Contain("ready");
    }

    private static (int Status, string Body) ReadResult(IResult result)
    {
        var status = (result as IStatusCodeHttpResult)?.StatusCode ?? StatusCodes.Status200OK;
        var body = result is IValueHttpResult value ? JsonSerializer.Serialize(value.Value) : string.Empty;
        return (status, body);
    }
}
