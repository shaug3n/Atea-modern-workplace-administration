using System.Net;
using System.Diagnostics;
using System.Text;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class TenantResolverTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Domain = "contoso.com";

    [Fact]
    public async Task ResolveAsync_uses_fixed_discovery_url_and_canonical_issuer()
    {
        var handler = new StubHandler(_ => JsonResponse(Metadata(TenantId)));
        var resolver = CreateResolver(handler);

        var result = await resolver.ResolveAsync(Domain);

        result.Status.Should().Be(TenantResolutionStatus.Resolved);
        result.TenantId.Should().Be(TenantId);
        handler.RequestUri!.AbsoluteUri.Should().Be(
            "https://login.microsoftonline.com/contoso.com/v2.0/.well-known/openid-configuration");
        handler.ClientName.Should().Be("EntraTenantDiscovery");
    }

    [Theory]
    [InlineData("common")]
    [InlineData("organizations")]
    [InlineData("consumers")]
    [InlineData("127.0.0.1")]
    [InlineData("https://contoso.com")]
    [InlineData("contoso.com:443")]
    [InlineData("*.contoso.com")]
    [InlineData("bücher..com")]
    public async Task ResolveAsync_rejects_invalid_domains(string domain)
    {
        var handler = new StubHandler(_ => JsonResponse(Metadata(TenantId)));
        var resolver = CreateResolver(handler);

        var result = await resolver.ResolveAsync(domain);

        result.Status.Should().Be(TenantResolutionStatus.InvalidInput);
        result.TenantId.Should().BeNull();
        handler.RequestUri.Should().BeNull();
    }

    [Theory]
    [InlineData("https://evil.example/tenant/v2.0")]
    [InlineData("https://login.microsoftonline.com/common/v2.0")]
    public async Task ResolveAsync_rejects_mismatched_metadata_authority(string issuer)
    {
        var handler = new StubHandler(_ => JsonResponse(Metadata(TenantId, issuer: issuer)));
        var resolver = CreateResolver(handler);

        var result = await resolver.ResolveAsync(Domain);

        result.Status.Should().NotBe(TenantResolutionStatus.Resolved);
        result.TenantId.Should().BeNull();
    }

    [Theory]
    [InlineData("https://evil.example/11111111-1111-1111-1111-111111111111/oauth2/v2.0/authorize")]
    [InlineData("https://login.microsoftonline.com/22222222-2222-2222-2222-222222222222/oauth2/v2.0/authorize")]
    public async Task ResolveAsync_rejects_mismatched_endpoint_host_or_tenant(string authorizationEndpoint)
    {
        var handler = new StubHandler(_ => JsonResponse(Metadata(TenantId, authorizationEndpoint: authorizationEndpoint)));
        var result = await CreateResolver(handler).ResolveAsync(Domain);

        result.Status.Should().NotBe(TenantResolutionStatus.Resolved);
        result.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_rejects_redirects()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Redirect)
        {
            Headers = { Location = new Uri("https://evil.example/metadata") }
        });
        var result = await CreateResolver(handler).ResolveAsync(Domain);

        result.Status.Should().NotBe(TenantResolutionStatus.Resolved);
        result.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_maps_timeout_and_not_found()
    {
        var timeoutResolver = CreateResolver(new StubHandler(_ => throw new TaskCanceledException("timeout")));
        var timeout = await timeoutResolver.ResolveAsync(Domain);
        timeout.Status.Should().Be(TenantResolutionStatus.Unavailable);

        var notFoundResolver = CreateResolver(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound)));
        var notFound = await notFoundResolver.ResolveAsync(Domain);
        notFound.Status.Should().Be(TenantResolutionStatus.NotFound);
    }

    [Fact]
    public async Task ResolveAsync_maps_Entra_invalid_tenant_errors_to_not_found()
    {
        var body = """{"error":"invalid_tenant","error_description":"AADSTS90002: Tenant was not found.","error_codes":[90002]}""";
        var resolver = CreateResolver(new StubHandler(_ => JsonResponse(body, HttpStatusCode.BadRequest)));

        var result = await resolver.ResolveAsync(Domain);

        result.Status.Should().Be(TenantResolutionStatus.NotFound);
        result.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_does_not_treat_other_bad_requests_as_missing_tenants()
    {
        var resolver = CreateResolver(new StubHandler(_ => JsonResponse("""{"error":"invalid_request"}""", HttpStatusCode.BadRequest)));

        var result = await resolver.ResolveAsync(Domain);

        result.Status.Should().Be(TenantResolutionStatus.Unavailable);
        result.TenantId.Should().BeNull();
    }

    [Fact]
    public async Task ResolveAsync_applies_the_five_second_deadline_to_metadata_body_reads()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DelayedReadStream(TimeSpan.FromSeconds(6)))
        });
        var resolver = CreateResolver(handler);
        var stopwatch = Stopwatch.StartNew();

        var result = await resolver.ResolveAsync(Domain);

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5.8));
        result.Status.Should().Be(TenantResolutionStatus.Unavailable);
    }

    [Fact]
    public async Task ResolveAsync_preserves_caller_cancellation_during_body_reads()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DelayedReadStream(TimeSpan.FromSeconds(6)))
        });
        var resolver = CreateResolver(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        var action = () => resolver.ResolveAsync(Domain, cancellation.Token);

        await FluentActions.Invoking(action).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ResolveAsync_caches_successful_aliases_but_not_failures()
    {
        var successHandler = new StubHandler(_ => JsonResponse(Metadata(TenantId)));
        var resolver = CreateResolver(successHandler);
        (await resolver.ResolveAsync(" Contoso.com ")).Status.Should().Be(TenantResolutionStatus.Resolved);
        (await resolver.ResolveAsync(Domain)).Status.Should().Be(TenantResolutionStatus.Resolved);
        successHandler.RequestCount.Should().Be(1);

        var failureHandler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var failureResolver = CreateResolver(failureHandler);
        await failureResolver.ResolveAsync(Domain);
        await failureResolver.ResolveAsync(Domain);
        failureHandler.RequestCount.Should().Be(2);
    }

    private static OidcTenantResolver CreateResolver(StubHandler handler) =>
        new(new StubHttpClientFactory(handler));

    private static HttpResponseMessage JsonResponse(string json, HttpStatusCode statusCode = HttpStatusCode.OK) =>
        new(statusCode) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string Metadata(Guid tenantId, string? issuer = null, string? authorizationEndpoint = null)
    {
        var tenant = tenantId.ToString();
        return $$"""
            {
              "issuer": "{{issuer ?? $"https://login.microsoftonline.com/{tenant}/v2.0"}}",
              "authorization_endpoint": "{{authorizationEndpoint ?? $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize"}}",
              "token_endpoint": "https://login.microsoftonline.com/{{tenant}}/oauth2/v2.0/token"
            }
            """;
    }

    private sealed class StubHttpClientFactory(StubHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            handler.ClientName = name;
            return new HttpClient(handler, disposeHandler: false);
        }
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string? ClientName { get; set; }
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestCount++;
            return Task.FromResult(responder(request));
        }
    }

    private sealed class DelayedReadStream(TimeSpan delay) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(delay, cancellationToken);
            return 0;
        }
    }
}
