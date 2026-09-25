using System.Net;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Exchange;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Exchange;

public sealed class ExchangeServiceTests
{
    [Fact]
    public async Task Lists_directory_mail_enabled_identities_with_a_bounded_inventory_contract()
    {
        var transport = new RecordingTransport();
        transport.Enqueue(
            GraphOperationResult.Success("corr-1", "req-1"),
            """
            {
              "@odata.nextLink": "https://graph.microsoft.com/v1.0/users?$skiptoken=page2",
              "value": [
                { "id": "user-1", "displayName": "Ada Lovelace", "mail": "ada@example.com", "userPrincipalName": "ada@example.com" },
                { "id": "user-2", "displayName": "No Mailbox", "mail": null, "userPrincipalName": "nomail@example.com" }
              ]
            }
            """);

        var result = await new ExchangeService(new RecordingFactory(transport)).ListAsync(
            Workspace,
            new ExchangeMailboxListQuery("ada", 25),
            CancellationToken.None);

        result.Error.Should().BeNull();
        result.Response.Items.Should().ContainSingle(item =>
            item.UserId == "user-1" && item.DisplayName == "Ada Lovelace" && item.Address == "ada@example.com");
        result.Response.InventorySource.Should().Be("directory");
        result.Response.IsCompleteExchangeInventory.Should().BeFalse();
        result.Response.Limitation.Should().Contain("directory-backed");
        result.Response.ContinuationToken.Should().Be("/v1.0/users?$skiptoken=page2");
        transport.Requests.Should().ContainSingle();
        transport.Requests[0].Method.Should().Be(HttpMethod.Get);
        transport.Requests[0].PathAndQuery.Should().Contain("$filter=mail%20ne%20null");
        transport.Requests[0].PathAndQuery.Should().Contain("$search=");
        transport.Requests[0].Headers.Should().ContainKey("ConsistencyLevel").WhoseValue.Should().Be("eventual");
        transport.RequestedScopes.Should().ContainSingle().Which.Should().Equal(GraphScopeCatalog.DirectoryReadScopes);
    }

    [Fact]
    public async Task Verifies_one_mailbox_with_mailbox_settings_read_without_exposing_mail_content()
    {
        var transport = new RecordingTransport();
        transport.Enqueue(
            GraphOperationResult.Success("corr-2", "req-2"),
            """
            {
              "userPurpose": "shared",
              "timeZone": "Europe/Oslo",
              "language": { "locale": "nb-NO", "displayName": "Norwegian" },
              "dateFormat": "dd.MM.yyyy",
              "timeFormat": "HH:mm",
              "automaticRepliesSetting": {
                "status": "alwaysEnabled",
                "internalReplyMessage": "do not expose this message"
              }
            }
            """);

        var result = await new ExchangeService(new RecordingFactory(transport)).VerifyAsync(
            Workspace,
            "user-1",
            CancellationToken.None);

        result.VerificationStatus.Should().Be("verified");
        result.MailboxType.Should().Be("shared");
        result.Settings!.TimeZone.Should().Be("Europe/Oslo");
        result.Settings.Language.Should().Be("nb-NO");
        result.Settings.AutomaticRepliesStatus.Should().Be("alwaysEnabled");
        JsonSerializer.Serialize(result).Should().NotContain("do not expose this message");
        transport.Requests.Should().ContainSingle(request =>
            request.Method == HttpMethod.Get && request.PathAndQuery == "/v1.0/users/user-1/mailboxSettings");
        transport.RequestedScopes.Should().ContainSingle().Which.Should().Equal(["MailboxSettings.Read"]);
    }

    [Fact]
    public async Task Rejects_an_absolute_continuation_token_before_sending_a_delegated_request()
    {
        var transport = new RecordingTransport();

        var result = await new ExchangeService(new RecordingFactory(transport)).ListAsync(
            Workspace,
            new ExchangeMailboxListQuery(ContinuationToken: "https://attacker.example/collect-token"),
            CancellationToken.None);

        result.Error!.Category.Should().Be("invalid_continuation");
        result.Error.StatusCode.Should().Be(400);
        transport.Requests.Should().BeEmpty();
        transport.RequestedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Both_exchange_routes_require_the_exchange_workspace_module()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Services.AddSingleton<IWorkspaceContextAccessor>(new FixedWorkspaceContextAccessor(Workspace));
        builder.Services.AddSingleton<IWorkspaceSettingsService>(new FixedWorkspaceSettingsService(new WorkspaceConfiguration([], [], new Dictionary<string, string>(), string.Empty, "light")));
        builder.Services.AddSingleton<IExchangeService, NoOpExchangeService>();
        await using var app = builder.Build();
        app.MapExchangeEndpoints();

        var routes = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith("/api/exchange/mailboxes", StringComparison.Ordinal) == true)
            .ToArray();

        routes.Should().HaveCount(2);
        foreach (var route in routes)
        {
            var context = new DefaultHttpContext { RequestServices = app.Services };
            context.Request.Method = HttpMethods.Get;
            context.Request.Path = route.RoutePattern.RawText!.Contains("{userObjectId}", StringComparison.Ordinal)
                ? "/api/exchange/mailboxes/user-1/overview"
                : "/api/exchange/mailboxes";
            if (context.Request.Path.Value?.EndsWith("/overview", StringComparison.Ordinal) == true)
            {
                context.Request.RouteValues["userObjectId"] = "user-1";
            }

            var requestDelegate = route.RequestDelegate ?? throw new InvalidOperationException("Exchange endpoint has no request delegate.");
            await requestDelegate(context);

            context.Response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }
    }

    [Theory]
    [InlineData("not_found", "mailbox_missing", 404)]
    [InlineData("not_authorized", "forbidden", 403)]
    [InlineData("temporarily_unavailable", "unavailable", 503)]
    public async Task Verification_returns_a_safe_row_level_result_for_target_failures(
        string category,
        string expectedStatus,
        int expectedHttpStatus)
    {
        var transport = new RecordingTransport();
        transport.Enqueue(new GraphOperationResult(false, category, expectedHttpStatus), "{\"error\":{\"message\":\"raw mailbox detail\"}}");

        var result = await new ExchangeService(new RecordingFactory(transport)).VerifyAsync(
            Workspace,
            "missing-or-protected-user",
            CancellationToken.None);

        result.VerificationStatus.Should().Be(expectedStatus);
        result.HttpStatusCode.Should().Be(expectedHttpStatus);
        result.Error!.Category.Should().Be(expectedStatus);
        JsonSerializer.Serialize(result).Should().NotContain("raw mailbox detail");
    }

    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace", "member"));

    private sealed class RecordingFactory(RecordingTransport transport) : IDelegatedGraphClientFactory
    {
        public Task<GraphClientLease> CreateForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
        {
            transport.RequestedScopes.Add(scopes);
            return Task.FromResult(new GraphClientLease(transport, scopes));
        }
    }

    private sealed class RecordingTransport : IGraphTransport
    {
        private readonly Queue<GraphTransportResponse> responses = new();

        public IReadOnlyCollection<string> Scopes => [];
        public List<GraphRequest> Requests { get; } = [];
        public List<IReadOnlyCollection<string>> RequestedScopes { get; } = [];

        public void Enqueue(GraphOperationResult result, string content) => responses.Enqueue(
            new GraphTransportResponse(result, content, 1, new Dictionary<string, IReadOnlyCollection<string>>()));

        public Task<GraphTransportResponse> SendAsync(GraphRequest request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(responses.Dequeue());
        }
    }

    private sealed class FixedWorkspaceContextAccessor(WorkspaceContext context) : IWorkspaceContextAccessor
    {
        public WorkspaceContext? Current => context;
    }

    private sealed class FixedWorkspaceSettingsService(WorkspaceConfiguration configuration) : IWorkspaceSettingsService
    {
        public Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken) => Task.FromResult(configuration);
        public Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class NoOpExchangeService : IExchangeService
    {
        public Task<ExchangeMailboxListResult> ListAsync(WorkspaceContext context, ExchangeMailboxListQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<ExchangeMailboxOverviewResponse> VerifyAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
