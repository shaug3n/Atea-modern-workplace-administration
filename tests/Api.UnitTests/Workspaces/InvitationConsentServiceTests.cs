using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Workspaces;

public sealed class InvitationConsentServiceTests
{
    private static readonly Guid InvitationId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid WorkspaceId = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private const string Nonce = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private const string Base64SigningKey = "MDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDAwMDA=";

    [Fact]
    public async Task Preview_returns_only_safe_workspace_and_catalog_scope_data()
    {
        var repository = new FixtureInvitationRepository(LiveInvitation("customer_admin"));
        var service = CreateService(repository);

        var result = await service.PreviewAsync(Nonce);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var propertyNames = json.RootElement.EnumerateObject().Select(property => property.Name).ToArray();

        propertyNames.Should().Equal("WorkspaceName", "ExpiresAt", "Flow", "PermissionScopes");
        result!.Flow.Should().Be("consent_first");
        result.WorkspaceName.Should().Be("Customer workspace");
        result.PermissionScopes.Should().Contain("MailboxSettings.Read");
        json.RootElement.ToString().Should().NotContain("tenantId").And.NotContain("invitationId").And.NotContain("email");
    }

    [Theory]
    [InlineData("member", "sign_in")]
    [InlineData("customer_admin", "consent_first")]
    [InlineData("workspace_owner", "consent_first")]
    public async Task Preview_classifies_only_customer_administrators_for_consent(string role, string expectedFlow)
    {
        var service = CreateService(new FixtureInvitationRepository(LiveInvitation(role)));

        var result = await service.PreviewAsync(Nonce);

        result!.Flow.Should().Be(expectedFlow);
    }

    [Fact]
    public async Task Member_cannot_start_consent()
    {
        var service = CreateService(new FixtureInvitationRepository(LiveInvitation("member")));

        var action = () => service.StartAsync(Nonce);

        await action.Should().ThrowAsync<InvitationConsentConflictException>();
    }

    [Fact]
    public async Task Start_targets_customer_spa_and_configured_api_application_uri()
    {
        var readRepository = new FixtureInvitationRepository(LiveInvitation("customer_admin"));
        var challengeRepository = new FixtureConsentChallengeRepository();
        var service = CreateService(readRepository, challengeRepository);

        var result = await service.StartAsync(Nonce);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(new Uri(result.AuthorizationUrl).Query);

        query["client_id"].ToString().Should().Be("spa-client");
        query["scope"].ToString().Should().Be("api://customer-api/.default");
        new Uri(result.AuthorizationUrl).AbsolutePath.Should().Be($"/{TenantId}/v2.0/adminconsent");
        result.Scopes.Should().Contain("User.Read.All");
        challengeRepository.Record.Should().NotBeNull();
        challengeRepository.Record!.Purpose.Should().Be("invitation");
        challengeRepository.Record.InvitationId.Should().Be(InvitationId);
        challengeRepository.Record.StateHash.Should().Be(ConsentChallengeService.HashState(result.Challenge));
    }

    [Fact]
    public async Task Invalid_nonce_is_rejected_before_repository_access()
    {
        var repository = new FixtureInvitationRepository(LiveInvitation("customer_admin"));
        var service = CreateService(repository);

        var result = await service.PreviewAsync("too-short");

        result.Should().BeNull();
        repository.LookupCalls.Should().Be(0);
    }

    [Theory]
    [InlineData("expired")]
    [InlineData("revoked")]
    [InlineData("redeemed")]
    [InlineData("unknown")]
    public async Task Preview_does_not_distinguish_unavailable_invitation_states(string condition)
    {
        var lookup = condition switch
        {
            "expired" => LiveInvitation("customer_admin", expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1)),
            "revoked" => LiveInvitation("customer_admin", isRevoked: true),
            "redeemed" => LiveInvitation("customer_admin", isRedeemed: true),
            _ => null
        };
        var service = CreateService(new FixtureInvitationRepository(lookup));

        var result = await service.PreviewAsync(Nonce);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Resume_requires_matching_server_binding_and_does_not_consume_state()
    {
        var invitation = LiveInvitation("customer_admin");
        var readRepository = new FixtureInvitationRepository(invitation);
        var challenges = new ConsentChallengeService(Convert.FromBase64String(Base64SigningKey));
        var signed = challenges.CreateInvitation(WorkspaceId, TenantId, InvitationId, invitation.ExpiresAt);
        var challengeRepository = new FixtureConsentChallengeRepository();
        challengeRepository.Add(new InvitationConsentChallengeRecord(
            ConsentChallengeService.HashState(signed.Challenge), WorkspaceId, TenantId, InvitationId,
            "invitation", signed.CorrelationId, signed.ExpiresAt, null));
        var service = CreateService(readRepository, challengeRepository, challenges);

        var result = await service.ResumeAsync(Nonce, signed.Challenge, TenantId);

        result.Valid.Should().BeTrue();
        result.Status.Should().Be("ready_to_sign_in");
        result.TenantId.Should().Be(TenantId);
        challengeRepository.ConsumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Resume_does_not_trust_a_mismatched_callback_tenant()
    {
        var invitation = LiveInvitation("customer_admin");
        var readRepository = new FixtureInvitationRepository(invitation);
        var challenges = new ConsentChallengeService(Convert.FromBase64String(Base64SigningKey));
        var signed = challenges.CreateInvitation(WorkspaceId, TenantId, InvitationId, invitation.ExpiresAt);
        var challengeRepository = new FixtureConsentChallengeRepository();
        challengeRepository.Add(new InvitationConsentChallengeRecord(
            ConsentChallengeService.HashState(signed.Challenge), WorkspaceId, TenantId, InvitationId,
            "invitation", signed.CorrelationId, signed.ExpiresAt, null));
        var service = CreateService(readRepository, challengeRepository, challenges);

        var result = await service.ResumeAsync(Nonce, signed.Challenge, Guid.NewGuid());

        result.Valid.Should().BeFalse();
        result.TenantId.Should().BeNull();
        challengeRepository.ConsumeCalls.Should().Be(0);
    }

    [Fact]
    public async Task Invalid_signed_state_is_rejected_before_invitation_database_lookup()
    {
        var repository = new FixtureInvitationRepository(LiveInvitation("customer_admin"));
        var service = CreateService(repository);

        var result = await service.ResumeAsync(Nonce, "tampered-state", TenantId);

        result.Valid.Should().BeFalse();
        repository.LookupCalls.Should().Be(0);
    }

    [Fact]
    public async Task Persistence_failure_is_safe_and_does_not_return_provider_details()
    {
        var service = CreateService(new ThrowingInvitationRepository(), new FixtureConsentChallengeRepository());

        var action = () => service.StartAsync(Nonce);

        var exception = await action.Should().ThrowAsync<InvitationConsentUnavailableException>();
        exception.Which.Message.Should().NotContain("private database detail");
    }

    [Fact]
    public async Task Start_fails_safely_when_consent_registration_configuration_is_incomplete()
    {
        var repository = new FixtureInvitationRepository(LiveInvitation("customer_admin"));
        var service = new InvitationConsentService(
            repository,
            new FixtureConsentChallengeRepository(),
            new ConsentChallengeService(Convert.FromBase64String(Base64SigningKey)),
            new OnboardingOptions());

        var action = () => service.StartAsync(Nonce);

        await action.Should().ThrowAsync<InvitationConsentUnavailableException>();
        repository.LookupCalls.Should().Be(0);
    }

    private static InvitationConsentService CreateService(
        IInvitationReadRepository readRepository,
        IConsentChallengeRepository? challengeRepository = null,
        ConsentChallengeService? challengeService = null)
    {
        var options = new OnboardingOptions
        {
            PublicBaseUrl = "https://workplace.example",
            ConsentRedirectUri = "https://workplace.example/onboarding/consent/callback",
            ConsentSigningKey = Base64SigningKey,
            CustomerClientId = "spa-client",
            ApiApplicationIdUri = "api://customer-api"
        };
        return new InvitationConsentService(
            readRepository,
            challengeRepository ?? new FixtureConsentChallengeRepository(),
            challengeService ?? new ConsentChallengeService(Convert.FromBase64String(Base64SigningKey)),
            options);
    }

    private static InvitationLookup LiveInvitation(
        string role,
        DateTimeOffset? expiresAt = null,
        bool isRedeemed = false,
        bool isRevoked = false) => new(
            InvitationId, WorkspaceId, "Customer workspace", TenantId, role,
            expiresAt ?? DateTimeOffset.UtcNow.AddHours(1), isRedeemed, isRevoked);

    private sealed class FixtureInvitationRepository(InvitationLookup? lookup) : IInvitationReadRepository
    {
        public int LookupCalls { get; private set; }

        public Task<InvitationLookup?> FindByNonceHashAsync(string nonceHash, CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Nonce))).ToLowerInvariant();
            return Task.FromResult(nonceHash == expected ? lookup : null);
        }
    }

    private sealed class ThrowingInvitationRepository : IInvitationReadRepository
    {
        public Task<InvitationLookup?> FindByNonceHashAsync(string nonceHash, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("private database detail");
    }

    private sealed class FixtureConsentChallengeRepository : IConsentChallengeRepository
    {
        public InvitationConsentChallengeRecord? Record { get; private set; }
        public int ConsumeCalls { get; private set; }

        public Task CreateAsync(Guid workspaceId, Guid tenantId, string stateHash, string correlationId, DateTimeOffset expiresAt, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<bool> TryConsumeAsync(Guid workspaceId, Guid tenantId, string stateHash, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);

        public Task CreateInvitationAsync(InvitationConsentChallengeRecord record, CancellationToken cancellationToken = default)
        {
            Record = record;
            return Task.CompletedTask;
        }

        public Task<InvitationConsentChallengeRecord?> FindInvitationAsync(string stateHash, CancellationToken cancellationToken = default) =>
            Task.FromResult(Record?.StateHash == stateHash ? Record : null);

        public Task<bool> TryConsumeInvitationAsync(string stateHash, Guid invitationId, Guid workspaceId, Guid tenantId, Guid redeemerObjectId, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            ConsumeCalls++;
            return Task.FromResult(false);
        }

        public void Add(InvitationConsentChallengeRecord record) => Record = record;
    }
}
