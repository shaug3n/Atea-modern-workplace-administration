using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Pim;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Pim;

public sealed class PimServiceTests
{
    [Fact]
    public async Task Get_user_pim_maps_active_eligible_and_missing_roles_to_safe_statuses()
    {
        var roles = new RecordingRoleAndPimReader
        {
            Roles =
            [
                new DirectoryRoleAssignment("assignment-1", EntraRoleCatalog.UserAdministratorTemplateId, "User Administrator", DirectoryRoleAssignmentState.Active, "/")
            ],
            Eligibility =
            [
                Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)
            ]
        };
        var service = CreateService(roles: roles);

        var result = await service.GetUserPimAsync(Workspace, "user-1", CancellationToken.None);

        result.Status.Should().Be(PimStatus.Active);
        result.Roles.Should().Contain(role => role.Status == PimStatus.Active && role.RoleTemplateId == EntraRoleCatalog.UserAdministratorTemplateId);
        result.Roles.Should().Contain(role => role.Status == PimStatus.EligibleInactive && role.RoleTemplateId == EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId);
    }

    [Theory]
    [InlineData(PimStatus.ApprovalRequired, PimStatus.ApprovalRequired, "Wait for PIM approval")]
    [InlineData(PimStatus.MfaRequired, PimStatus.MfaRequired, "Complete MFA for PIM activation")]
    [InlineData("justification_required", PimStatus.EligibleInactive, "Enter a business justification")]
    [InlineData(PimStatus.PolicyBlocked, PimStatus.PolicyBlocked, "Review the tenant PIM policy")]
    public async Task Get_user_pim_maps_policy_requirements_to_guided_handoff(string eligibilityStatus, string expectedStatus, string expectedNextStep)
    {
        var roles = new RecordingRoleAndPimReader
        {
            Eligibility =
            [
                Eligibility(
                    "eligibility-1",
                    EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId,
                    "Privileged Role Administrator",
                    eligibilityStatus,
                    requiresJustification: eligibilityStatus == "justification_required")
            ]
        };
        var service = CreateService(roles: roles);

        var result = await service.GetUserPimAsync(Workspace, "user-1", CancellationToken.None);

        result.Roles.Should().ContainSingle().Which.Status.Should().Be(expectedStatus);
        result.Roles.Single().Handoff!.NextStep.Should().Be(expectedNextStep);
        result.Roles.Single().Handoff!.PortalUrl.Should().Contain("entra.microsoft.com");
    }

    [Fact]
    public async Task Activate_requires_explicit_confirmation_before_reading_current_eligibility_or_calling_graph()
    {
        var roles = new RecordingRoleAndPimReader();
        var activations = new RecordingPimActivationCommands();
        var service = CreateService(roles: roles, activations: activations);

        var result = await service.ActivateAsync(Workspace, ValidActivation with { Confirmed = false }, "idem-1", CancellationToken.None);

        result.Status.Should().Be(PimStatus.PolicyBlocked);
        result.Error.Should().Be("confirmation_required");
        roles.EligibilityReads.Should().Be(0);
        activations.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Activate_checks_capability_and_denies_without_graph_when_role_is_not_currently_eligible()
    {
        var roles = new RecordingRoleAndPimReader();
        var activations = new RecordingPimActivationCommands();
        var service = CreateService(roles: roles, activations: activations);

        var result = await service.ActivateAsync(Workspace, ValidActivation, "idem-2", CancellationToken.None);

        result.Status.Should().Be(PimStatus.NotEligible);
        result.Error.Should().Be("not_eligible");
        activations.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Activate_rejects_unsupported_role_type_without_graph_mutation()
    {
        var roles = new RecordingRoleAndPimReader
        {
            Eligibility = [Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)]
        };
        var activations = new RecordingPimActivationCommands();
        var service = CreateService(roles: roles, activations: activations);

        var result = await service.ActivateAsync(Workspace, ValidActivation with { RoleType = "group" }, "idem-3", CancellationToken.None);

        result.Status.Should().Be(PimStatus.PolicyBlocked);
        result.Error.Should().Be("unsupported_role_type");
        activations.Requests.Should().BeEmpty();
    }

    [Theory]
    [InlineData(PimStatus.ActivationPending, "PendingApproval")]
    [InlineData(PimStatus.Active, "Provisioned")]
    public async Task Activate_returns_only_graph_reported_success_states(string expectedStatus, string graphStatus)
    {
        var roles = new RecordingRoleAndPimReader
        {
            Eligibility = [Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)]
        };
        var activations = new RecordingPimActivationCommands { Result = PimActivationGraphResult.Succeeded("request-1", graphStatus, "corr-1", "req-1") };
        var service = CreateService(roles: roles, activations: activations);

        var result = await service.ActivateAsync(Workspace, ValidActivation, "idem-4", CancellationToken.None);

        result.Status.Should().Be(expectedStatus);
        result.RequestId.Should().Be("request-1");
        result.GraphCorrelationId.Should().Be("corr-1");
        activations.Requests.Should().ContainSingle().Which.RoleDefinitionId.Should().Be("role-definition-id");
    }

    [Theory]
    [InlineData("not_authorized", PimStatus.NotAuthorized, "Ask an administrator to grant the required role or Graph consent")]
    [InlineData("consent_required", PimStatus.NotAuthorized, "Grant delegated Microsoft Graph consent")]
    [InlineData("mfa_required", PimStatus.MfaRequired, "Complete MFA for PIM activation")]
    [InlineData("policy_blocked", PimStatus.PolicyBlocked, "Review the tenant PIM policy")]
    [InlineData("throttled", PimStatus.TemporarilyUnavailable, "Refresh PIM state and retry")]
    public async Task Activate_maps_graph_failures_to_safe_guided_handoff_without_false_success(string graphCategory, string expectedStatus, string expectedNextStep)
    {
        var roles = new RecordingRoleAndPimReader
        {
            Eligibility = [Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)]
        };
        var activations = new RecordingPimActivationCommands { Result = PimActivationGraphResult.Failed(graphCategory, 403, "corr-1", "req-1") };
        var service = CreateService(roles: roles, activations: activations);

        var result = await service.ActivateAsync(Workspace, ValidActivation, "idem-5", CancellationToken.None);

        result.Status.Should().Be(expectedStatus);
        result.Error.Should().Be(graphCategory);
        result.Handoff!.NextStep.Should().Be(expectedNextStep);
        result.GraphCorrelationId.Should().Be("corr-1");
    }

    [Fact]
    public async Task Activate_replays_duplicate_browser_submission_without_second_graph_call()
    {
        var roles = new RecordingRoleAndPimReader
        {
            Eligibility = [Eligibility("eligibility-1", EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", PimStatus.EligibleInactive)]
        };
        var activations = new RecordingPimActivationCommands { Result = PimActivationGraphResult.Succeeded("request-1", "PendingApproval") };
        var service = CreateService(roles: roles, activations: activations, idempotency: new MemoryIdempotencyService());

        var first = await service.ActivateAsync(Workspace, ValidActivation, "same-key", CancellationToken.None);
        var replay = await service.ActivateAsync(Workspace, ValidActivation, "same-key", CancellationToken.None);

        first.Status.Should().Be(PimStatus.ActivationPending);
        replay.Status.Should().Be(PimStatus.ActivationPending);
        replay.Replayed.Should().BeTrue();
        activations.Requests.Should().ContainSingle();
    }

    private static PimService CreateService(
        RecordingRoleAndPimReader? roles = null,
        RecordingPimActivationCommands? activations = null,
        GraphAuthorizationSnapshot? snapshot = null,
        IIdempotencyService? idempotency = null) =>
        new(
            roles ?? new RecordingRoleAndPimReader(),
            activations ?? new RecordingPimActivationCommands(),
            new StaticCapabilityReader(snapshot ?? EligibleSnapshot),
            idempotency ?? new MemoryIdempotencyService());

    private static PimEligibility Eligibility(
        string id,
        string roleTemplateId,
        string displayName,
        string status,
        bool requiresJustification = true) =>
        new(
            id,
            roleTemplateId,
            displayName,
            status,
            Capability.PimActivate,
            status is PimStatus.EligibleInactive or PimStatus.ApprovalRequired or PimStatus.MfaRequired,
            status == PimStatus.ApprovalRequired,
            status == PimStatus.MfaRequired,
            requiresJustification,
            480,
            new PimActivationAction("request_activation", "/api/pim/activations", "POST", Capability.PimActivate, true))
        {
            RoleDefinitionId = "role-definition-id"
        };

    private static readonly PimActivationRequest ValidActivation = new(
        EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId,
        60,
        "Need to complete approved admin work",
        Confirmed: true);

    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(
            Guid.Parse("11111111-1111-1111-1111-111111111111"),
            Guid.Parse("22222222-2222-2222-2222-222222222222"),
            "alex@example.com",
            "Alex Example",
            "Member"),
        new WorkspaceMembership(
            Guid.Parse("55555555-5555-5555-5555-555555555555"),
            "Contoso Workplace",
            "member"));

    private static readonly GraphAuthorizationSnapshot EligibleSnapshot = GraphAuthorizationSnapshot.Available(
        "22222222-2222-2222-2222-222222222222",
        ["Directory.Read.All", "RoleManagement.Read.Directory", "RoleManagement.ReadWrite.Directory"],
        [
            new DirectoryRoleSnapshot(EntraRoleCatalog.GlobalReaderTemplateId, "Global Reader", DirectoryRoleAssignmentState.Active, "/"),
            new DirectoryRoleSnapshot(EntraRoleCatalog.PrivilegedRoleAdministratorTemplateId, "Privileged Role Administrator", DirectoryRoleAssignmentState.Eligible, "/", new PimStateSnapshot(PimRequirement.ActivationRequired))
        ]);

    private sealed class StaticCapabilityReader(GraphAuthorizationSnapshot snapshot) : IGraphAuthorizationSnapshotReader
    {
        public Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default) => Task.FromResult(snapshot);
    }

    private sealed class RecordingRoleAndPimReader : IRoleAndPimReader
    {
        public IReadOnlyList<DirectoryRoleAssignment> Roles { get; init; } = [];
        public IReadOnlyList<PimEligibility> Eligibility { get; init; } = [];
        public int EligibilityReads { get; private set; }

        public Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken) =>
            Task.FromResult(GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded(Roles));

        public Task<GraphReadResult<IReadOnlyList<PimEligibility>>> ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken)
        {
            EligibilityReads++;
            return Task.FromResult(GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded(Eligibility));
        }
    }

    private sealed class RecordingPimActivationCommands : IPimActivationCommands
    {
        public PimActivationGraphResult Result { get; init; } = PimActivationGraphResult.Succeeded("request-1", "PendingApproval");
        public List<PimActivationGraphRequest> Requests { get; } = [];

        public Task<PimActivationGraphResult> ActivateDirectoryRoleAsync(PimActivationGraphRequest request, string idempotencyKey, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Result);
        }
    }
}
