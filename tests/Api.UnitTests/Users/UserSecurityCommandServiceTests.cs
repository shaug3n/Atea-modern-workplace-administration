using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Users;

public sealed class UserSecurityCommandServiceTests
{
    [Fact]
    public async Task First_tap_response_exposes_the_code_once_but_safe_idempotency_json_does_not_contain_it()
    {
        var idempotency = new MemoryIdempotencyService();
        var audit = new RecordingAuditWriter();
        var service = CreateAuthenticationService(idempotency, audit, new GraphTemporaryAccessPassResult("ABC123", "tap-1", null, 60, true));

        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

        result.TemporaryAccessPass.Should().Be("ABC123");
        idempotency.Records.Single().SafeResultJson.Should().NotContain("ABC123");
        audit.Events.Single().SafeMetadataJson.Should().Be("{}");
    }

    [Fact]
    public async Task Replayed_tap_request_never_returns_the_original_code()
    {
        var idempotency = new MemoryIdempotencyService();
        var service = CreateAuthenticationService(idempotency, new RecordingAuditWriter(), new GraphTemporaryAccessPassResult("ABC123", "tap-1", null, 60, true));

        _ = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);
        var result = await service.CreateTemporaryAccessPassAsync(Context(), "user-1", "key-1", CancellationToken.None);

        result.Replayed.Should().BeTrue();
        result.TemporaryAccessPass.Should().BeNull();
        result.Error.Should().Be("temporary_access_pass_already_issued");
    }

    private static AuthenticationMethodService CreateAuthenticationService(
        IIdempotencyService idempotency,
        IAuditWriter audit,
        GraphTemporaryAccessPassResult tap) =>
        new(
            new StubAuthenticationMethodReader(),
            new StaticAuthorizationReader(),
            new StubAuthenticationMethodCommands(tap),
            idempotency,
            audit);

    private static WorkspaceContext Context() => new(
        new AuthenticatedUser(Guid.Parse("11111111-1111-1111-1111-111111111111"), Guid.Parse("22222222-2222-2222-2222-222222222222"), "admin@example.com", "Admin", "Member", null),
        new AuthorizationWorkspaceMembership(Guid.Parse("33333333-3333-3333-3333-333333333333"), "Workspace", "member"));

    private sealed class StubAuthenticationMethodReader : IAuthenticationMethodReader
    {
        public Task<GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>> ReadAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Succeeded([]));
    }

    private sealed class StubAuthenticationMethodCommands(GraphTemporaryAccessPassResult tap) : IAuthenticationMethodCommands
    {
        public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(tap);
    }

    private sealed class StaticAuthorizationReader : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(GraphAuthorizationSnapshot.Available("actor-1", ["UserAuthenticationMethod.Read.All", "UserAuthenticationMethod.ReadWrite.All"], [new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalAdministratorTemplateId, "Global Administrator", DirectoryRoleAssignmentState.Active, "/")]));
    }

    private sealed class RecordingAuditWriter : IAuditWriter
    {
        public List<AuditEvent> Events { get; } = [];
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken)
        {
            Events.Add(auditEvent);
            return Task.CompletedTask;
        }
    }
}
