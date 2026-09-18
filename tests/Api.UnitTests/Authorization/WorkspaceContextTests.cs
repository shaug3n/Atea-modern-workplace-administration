using System.Security.Claims;
using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class WorkspaceContextTests
{
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly AuthenticatedUser FixtureUser = new(
        TenantId,
        ObjectId,
        "alex@example.com",
        "Alex Example",
        "Member",
        Guid.Parse("33333333-3333-3333-3333-333333333333"));

    [Fact]
    public async Task MissingTenantIdReturnsAnExplicitFailureReason()
    {
        var result = await Resolve(CreatePrincipal(oid: ObjectId.ToString()));

        result.FailureReason.Should().Be(WorkspaceContextFailureReason.MissingTenantId);
    }

    [Fact]
    public async Task MissingObjectIdReturnsAnExplicitFailureReason()
    {
        var result = await Resolve(CreatePrincipal(tid: TenantId.ToString()));

        result.FailureReason.Should().Be(WorkspaceContextFailureReason.MissingObjectId);
    }

    [Fact]
    public async Task WrongAudienceReturnsAnExplicitFailureReason()
    {
        var result = await Resolve(CreatePrincipal(audience: "api://wrong-audience"));

        result.FailureReason.Should().Be(WorkspaceContextFailureReason.WrongAudience);
    }

    [Fact]
    public async Task UnknownWorkspaceMembershipReturnsAnExplicitFailureReason()
    {
        var result = await Resolve(CreatePrincipal(), new StubMembershipReader(null));

        result.FailureReason.Should().Be(WorkspaceContextFailureReason.WorkspaceMembershipRequired);
    }

    [Fact]
    public async Task ValidCustomerTenantIdentityProducesVerifiedContext()
    {
        var membership = new WorkspaceMembership(Guid.Parse("44444444-4444-4444-4444-444444444444"), "Customer workspace");
        var reader = new StubMembershipReader(membership);
        var result = await Resolve(CreatePrincipal(), reader);

        result.Succeeded.Should().BeTrue();
        result.Context!.User.Should().Be(FixtureUser);
        result.Context.Membership.Should().Be(membership);
        reader.TenantId.Should().Be(TenantId);
        reader.ObjectId.Should().Be(ObjectId);
    }

    [Fact]
    public async Task IdentityValidationFailureIsNotAWorkspaceMembershipFailure()
    {
        var result = await Resolve(CreatePrincipal(oid: ObjectId.ToString()));

        result.IsAuthenticationFailure.Should().BeTrue();
        result.FailureReason.Should().NotBe(WorkspaceContextFailureReason.WorkspaceMembershipRequired);
    }

    private static Task<WorkspaceContextResolution> Resolve(ClaimsPrincipal principal, IWorkspaceMembershipReader? reader = null) =>
        new WorkspaceContextResolver("api://atea-unified-workplace-api", reader ?? new StubMembershipReader(new WorkspaceMembership(Guid.NewGuid(), "Fixture workspace")))
            .ResolveAsync(principal);

    private static ClaimsPrincipal CreatePrincipal(string? tid = null, string? oid = null, string audience = "api://atea-unified-workplace-api")
    {
        var claims = new List<Claim>
        {
            new("preferred_username", FixtureUser.UserPrincipalName),
            new("name", FixtureUser.DisplayName),
            new("userType", FixtureUser.UserType),
            new("home_tid", FixtureUser.HomeTenantId!.Value.ToString()),
            new("aud", audience)
        };
        if (tid is not null) claims.Add(new Claim("tid", tid));
        if (oid is not null) claims.Add(new Claim("oid", oid));
        if (tid is null && oid is null) claims.Add(new Claim("tid", TenantId.ToString()));
        if (oid is null && tid is null) claims.Add(new Claim("oid", ObjectId.ToString()));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private sealed class StubMembershipReader(WorkspaceMembership? membership) : IWorkspaceMembershipReader
    {
        public Guid TenantId { get; private set; }
        public Guid ObjectId { get; private set; }

        public Task<WorkspaceMembership?> FindMembershipAsync(Guid tenantId, Guid objectId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record(tenantId, objectId));

        private WorkspaceMembership? Record(Guid tenantId, Guid objectId)
        {
            TenantId = tenantId;
            ObjectId = objectId;
            return membership;
        }
    }
}
