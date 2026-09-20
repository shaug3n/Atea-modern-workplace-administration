using System.Net;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Graph;

public sealed class DelegatedGraphAdapterTests
{
    [Fact]
    public async Task Directory_search_requests_least_privilege_scopes_selected_fields_and_returns_one_page()
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
        var factory = new FakeDelegatedGraphClientFactory(transport);
        var reader = new GraphDirectoryReader(factory);

        var result = await reader.SearchAsync(
            GraphContextFixture.Workspace,
            new UserSearchQuery(Search: "engineer", PageSize: 25, AccountStatus: "enabled", TenantRole: "Global Reader", License: "ENTERPRISEPACK", UserType: "Member"),
            CancellationToken.None);

        result.Items.Should().ContainSingle(user => user.Id == "user-1");
        result.ContinuationLink.Should().Be("/v1.0/users?$skiptoken=page2");
        result.CorrelationIds.Should().Equal("corr-1");
        factory.RequestedScopes.Single().Should().Equal(GraphScopeCatalog.DirectoryReadScopes);
        transport.Requests[0].PathAndQuery.Should().Contain("$select=id,displayName,userPrincipalName,mail,accountEnabled,userType");
        transport.Requests[0].PathAndQuery.Should().Contain("$top=25");
        transport.Requests[0].PathAndQuery.Should().Contain("$search=");
        transport.Requests[0].PathAndQuery.Should().Contain("accountEnabled%20eq%20true");
        transport.Requests[0].PathAndQuery.Should().Contain("userType%20eq%20%27Member%27");
        transport.Requests[0].PathAndQuery.Should().Contain("assignedLicenses%2Fany");
        transport.Requests[0].PathAndQuery.Should().Contain("Global%20Reader");
        transport.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Directory_search_uses_server_decoded_continuation_path_without_rebuilding_filters()
    {
        var transport = new FakeGraphTransport(GraphScopeCatalog.DirectoryReadScopes);
        transport.EnqueueJson(HttpStatusCode.OK, """
            {
              "value": [
                { "id": "user-2", "displayName": "Grace Hopper", "userPrincipalName": "grace@example.com", "mail": null }
              ]
            }
            """, "corr-2", "req-2");
        var reader = new GraphDirectoryReader(new FakeDelegatedGraphClientFactory(transport));

        var result = await reader.SearchAsync(
            GraphContextFixture.Workspace,
            new UserSearchQuery(ContinuationPath: "/v1.0/users?$skiptoken=page2", Search: "ignored", PageSize: 25),
            CancellationToken.None);

        result.Items.Should().ContainSingle(user => user.Id == "user-2");
        transport.Requests.Single().PathAndQuery.Should().Be("/v1.0/users?$skiptoken=page2");
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

        var result = await reader.SearchAsync(GraphContextFixture.Workspace, new UserSearchQuery(Search: "blocked"), CancellationToken.None);

        result.Error.Should().NotBeNull();
        result.Error!.Category.Should().Be(expectedCategory);
        result.Items.Should().BeEmpty();
    }
}

internal static class GraphContextFixture
{
    public static readonly Atea.UnifiedWorkplace.Api.Authorization.WorkspaceContext Workspace = new(
        new Atea.UnifiedWorkplace.Api.Authorization.AuthenticatedUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "alex@example.com",
            "Alex Example",
            "Member"),
        new Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Contoso Workplace",
            "member"));
}
