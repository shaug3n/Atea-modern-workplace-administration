using System.Net;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Graph;

public sealed class DelegatedGraphAdapterTests
{
    [Fact]
    public async Task Directory_search_requests_least_privilege_scopes_selected_fields_and_all_pages()
    {
        var transport = new FakeGraphTransport(GraphScopeCatalog.DirectoryReadScopes);
        transport.EnqueueJson(HttpStatusCode.OK, """
            {
              "@odata.nextLink": "/v1.0/users?$skiptoken=page2",
              "value": [
                { "id": "user-1", "displayName": "Ada Lovelace", "userPrincipalName": "ada@example.com", "mail": "ada@example.com" }
              ]
            }
            """, "corr-1", "req-1");
        transport.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "user-2", "displayName": "Grace Hopper", "userPrincipalName": "grace@example.com", "mail": null }
              ]
            }
            """, "corr-2", "req-2");
        var factory = new FakeDelegatedGraphClientFactory(transport);
        var reader = new GraphDirectoryReader(factory);

        var result = await reader.SearchAsync(new UserSearchQuery("engineer", 25), CancellationToken.None);

        result.Items.Should().HaveCount(2);
        result.Items.Select(user => user.Id).Should().Equal("user-1", "user-2");
        result.CorrelationIds.Should().Equal("corr-1", "corr-2");
        factory.RequestedScopes.Single().Should().Equal(GraphScopeCatalog.DirectoryReadScopes);
        transport.Requests[0].PathAndQuery.Should().Contain("$select=id,displayName,userPrincipalName,mail,accountEnabled,userType");
        transport.Requests[0].PathAndQuery.Should().Contain("$top=25");
        transport.Requests[0].PathAndQuery.Should().Contain("$search=");
        transport.Requests[1].PathAndQuery.Should().Be("/v1.0/users?$skiptoken=page2");
    }

    [Fact]
    public async Task Directory_get_maps_not_found_to_null_without_raw_graph_payload()
    {
        var transport = new FakeGraphTransport(GraphScopeCatalog.DirectoryReadScopes);
        transport.EnqueueJson(HttpStatusCode.NotFound, "{\"error\":{\"code\":\"Request_ResourceNotFound\",\"message\":\"raw graph detail\"}}", "corr", "req");
        var reader = new GraphDirectoryReader(new FakeDelegatedGraphClientFactory(transport));

        var result = await reader.GetAsync("missing-user", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task User_lifecycle_adapter_owns_disable_request_details_and_returns_mapped_result()
    {
        var transport = new FakeGraphTransport(GraphScopeCatalog.UserAccountWriteScopes);
        transport.EnqueueJson(HttpStatusCode.Conflict, "{\"error\":{\"code\":\"Directory_ConcurrencyViolation\"}}", "corr", "req");
        var lifecycle = new GraphUserLifecycle(new FakeDelegatedGraphClientFactory(transport));

        var result = await lifecycle.SetAccountEnabledAsync("user-1", false, "idempotency-key", CancellationToken.None);

        result.Category.Should().Be("conflict");
        transport.Requests.Should().ContainSingle();
        var request = transport.Requests.Single();
        request.PathAndQuery.Should().Be("/v1.0/users/user-1");
        request.Headers.Should().ContainKey("Idempotency-Key").WhoseValue.Should().Be("idempotency-key");
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, "not_authorized")]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData((HttpStatusCode)429, "throttled")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "temporarily_unavailable")]
    public async Task Directory_search_returns_safe_error_mapping(HttpStatusCode statusCode, string expectedCategory)
    {
        var transport = new FakeGraphTransport(GraphScopeCatalog.DirectoryReadScopes);
        transport.EnqueueJson(statusCode, "{\"error\":{\"message\":\"raw graph detail\"}}", "corr", "req");
        var reader = new GraphDirectoryReader(new FakeDelegatedGraphClientFactory(transport));

        var result = await reader.SearchAsync(new UserSearchQuery("blocked"), CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be(expectedCategory);
        result.Items.Should().BeEmpty();
    }
}
