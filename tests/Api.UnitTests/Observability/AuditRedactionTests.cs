using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;
using AuthorizationWorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Observability;

public sealed class AuditRedactionTests
{
    [Fact]
    public void Redactor_replaces_secret_bearing_values_without_removing_safe_references()
    {
        var input = """
        Authorization: Bearer eyJhbGciOiJsecret
        access_token=secret-token
        refresh_token=secret-refresh
        client_secret=secret-client
        password=Temporary-Password-123!
        mfa_code=123456
        Cookie: sessionid=secret-cookie
        request-id=req-123 graph-correlation-id=corr-456
        """;

        var redacted = RedactingLogEnricher.Redact(input);

        redacted.Should().NotContain("eyJhbGci");
        redacted.Should().NotContain("secret-token");
        redacted.Should().NotContain("secret-refresh");
        redacted.Should().NotContain("secret-client");
        redacted.Should().NotContain("Temporary-Password-123");
        redacted.Should().NotContain("123456");
        redacted.Should().NotContain("secret-cookie");
        redacted.Should().Contain("[REDACTED]");
        redacted.Should().Contain("req-123");
        redacted.Should().Contain("corr-456");
    }

    [Fact]
    public async Task User_mutation_succeeds_when_audit_writer_fails()
    {
        var service = new UserCommandService(
            new RecordingDirectoryReader(),
            new RecordingUserCommands(),
            new RecordingGroupCommands(),
            new RecordingLicenseCommands(),
            new StaticCapabilityReader(),
            new MemoryIdempotencyService(),
            new FailingAuditWriter(),
            () => "Temp-Password-12345!");

        var result = await service.UpdateAsync(Workspace, "user-1", ValidUpdate, "audit-failure-key", CancellationToken.None);

        result.Status.Should().Be(UserCommandStatus.Succeeded);
        result.GraphRequestId.Should().Be("graph-request-1");
    }

    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "alex@example.com",
            "Alex Example",
            "Member"),
        new AuthorizationWorkspaceMembership(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Contoso Workplace", "member"));

    private static readonly UpdateUserCommand ValidUpdate = new(
        "Ada Lovelace",
        "Ada",
        "Lovelace",
        "Principal Engineer",
        "Digital Workplace",
        "Oslo",
        "+47 22 00 00 00",
        "NO",
        true);

    private sealed class StaticCapabilityReader : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(GraphAuthorizationSnapshot.Available(
                "actor-1",
                ["Directory.Read.All", "User.Read.All", "User.ReadWrite.All"],
                [new DirectoryRoleSnapshot(EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Active, "/")]));
    }

    private sealed class RecordingDirectoryReader : IUserDirectoryReader
    {
        public Task<PagedResult<UserSummary>> SearchAsync(WorkspaceContext context, UserSearchQuery query, CancellationToken cancellationToken) =>
            Task.FromResult(new PagedResult<UserSummary>([], [], null));

        public Task<UserDetails?> GetAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult<UserDetails?>(new UserDetails(userObjectId, "Ada Lovelace", "ada@example.com", "ada@example.com", true, "Member"));
    }

    private sealed class RecordingUserCommands : IUserLifecycleCommands
    {
        public Task<GraphOperationResult> CreateUserAsync(GraphUserCreateRequest request, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success("graph-corr-1", "graph-request-1"));

        public Task<GraphOperationResult> UpdateProfileAsync(string userObjectId, GraphUserProfileUpdate update, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success("graph-corr-1", "graph-request-1"));

        public Task<GraphOperationResult> SetAccountEnabledAsync(string userObjectId, bool accountEnabled, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success("graph-corr-1", "graph-request-1"));
    }

    private sealed class RecordingGroupCommands : IGroupMembershipCommands
    {
        public Task<GraphOperationResult> AddMemberAsync(string groupObjectId, string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphOperationResult> RemoveMemberAsync(string groupObjectId, string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());
    }

    private sealed class RecordingLicenseCommands : ILicenseAssignmentCommands
    {
        public Task<GraphOperationResult> AssignLicenseAsync(string userObjectId, LicenseAssignmentCommand command, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());

        public Task<GraphOperationResult> RemoveLicenseAsync(string userObjectId, string skuId, string idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(GraphOperationResult.Success());
    }

    private sealed class FailingAuditWriter : IAuditWriter
    {
        public Task WriteAsync(AuditEvent auditEvent, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database password=secret failed");
    }
}
