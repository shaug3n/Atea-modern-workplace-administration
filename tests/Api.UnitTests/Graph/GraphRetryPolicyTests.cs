using System.Net;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Graph;

public sealed class GraphRetryPolicyTests
{
    [Fact]
    public async Task Retries_throttled_requests_using_retry_after()
    {
        var handler = new SequenceHandler(
            Response((HttpStatusCode)429, retryAfter: TimeSpan.FromMilliseconds(1)),
            Response(HttpStatusCode.OK, "{\"value\":[]}"));
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, "/v1.0/users"), CancellationToken.None);

        response.Result.IsSuccess.Should().BeTrue();
        handler.Requests.Should().HaveCount(2);
        response.Attempts.Should().Be(2);
    }

    [Fact]
    public async Task Retries_transient_server_failures_with_a_bound()
    {
        var handler = new SequenceHandler(
            Response(HttpStatusCode.ServiceUnavailable),
            Response(HttpStatusCode.BadGateway),
            Response(HttpStatusCode.OK, "{\"id\":\"ok\"}"));
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, "/v1.0/me"), CancellationToken.None);

        response.Result.IsSuccess.Should().BeTrue();
        response.Attempts.Should().Be(3);
        handler.Requests.Should().HaveCount(3);
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task Does_not_retry_conflicts_or_permission_failures(HttpStatusCode statusCode)
    {
        var handler = new SequenceHandler(Response(statusCode));
        var transport = CreateTransport(handler);

        var response = await transport.SendAsync(new GraphRequest(HttpMethod.Post, "/v1.0/users/user/revokeSignInSessions"), CancellationToken.None);

        response.Result.IsSuccess.Should().BeFalse();
        handler.Requests.Should().ContainSingle();
        response.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Adds_request_correlation_without_exposing_the_token()
    {
        var handler = new SequenceHandler(Response(HttpStatusCode.OK, "{\"id\":\"ok\"}"));
        var transport = CreateTransport(handler, accessToken: "secret-token-value");

        await transport.SendAsync(new GraphRequest(HttpMethod.Get, "/v1.0/me"), CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers.Should().ContainKey("client-request-id");
        request.Headers.Should().ContainKey("Authorization");
        request.Headers["Authorization"].Should().Be("Bearer <redacted>");
    }

    private static GraphHttpTransport CreateTransport(SequenceHandler handler, string accessToken = "token") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.microsoft.com") },
            accessToken,
            ["User.Read"],
            new GraphTransportOptions(MaxRetries: 2, DelayAsync: (_, _) => Task.CompletedTask));

    private static HttpResponseMessage Response(HttpStatusCode statusCode, string body = "{}", TimeSpan? retryAfter = null)
    {
        var response = new HttpResponseMessage(statusCode) { Content = new StringContent(body) };
        if (retryAfter is not null)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
        }

        return response;
    }

    private sealed class SequenceHandler(params HttpResponseMessage[] responses) : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> responses = new(responses);
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Headers.ToDictionary(
                header => header.Key,
                header => header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ? "Bearer <redacted>" : string.Join(",", header.Value))));

            return Task.FromResult(responses.Count > 0 ? responses.Dequeue() : new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private sealed record RecordedRequest(IReadOnlyDictionary<string, string> Headers);
}
