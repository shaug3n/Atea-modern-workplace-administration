using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class CapabilityEvaluatorTests
{
    [Fact]
    public void Global_reader_can_view_users_but_mutations_are_read_only()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All", "User.ReadWrite.All", "User.EnableDisableAccount.All", "User-PasswordProfile.ReadWrite.All"],
            roles: [ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersView].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.ReadOnly);
        capabilities[Capability.UsersUpdate].State.Should().Be(CapabilityState.ReadOnly);
        capabilities[Capability.UsersDisable].State.Should().Be(CapabilityState.ReadOnly);
        capabilities[Capability.UsersResetPassword].State.Should().Be(CapabilityState.ReadOnly);
    }

    [Fact]
    public void User_administrator_can_use_user_lifecycle_capabilities_when_delegated_scopes_are_present()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All", "User.Create", "User.ReadWrite.All", "User.EnableDisableAccount.All", "User-PasswordProfile.ReadWrite.All"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersView].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.UsersUpdate].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.UsersDisable].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.UsersResetPassword].State.Should().Be(CapabilityState.Allowed);
    }

    [Fact]
    public void Missing_directory_read_permission_hides_user_sections()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["User.Create", "User.ReadWrite.All"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersView].State.Should().Be(CapabilityState.Hidden);
        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.Hidden);
        capabilities[Capability.UsersUpdate].State.Should().Be(CapabilityState.Hidden);
    }

    [Fact]
    public void Consent_missing_is_reported_as_consent_required()
    {
        var snapshot = GraphAuthorizationSnapshot.Unavailable("consent_required", consentRequired: true);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersView].State.Should().Be(CapabilityState.ConsentRequired);
        capabilities[Capability.UsersView].ReasonCode.Should().Be("consent_required");
    }

    [Fact]
    public void Eligible_inactive_role_requires_pim_activation_for_mutations()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Create", "User.ReadWrite.All", "User.EnableDisableAccount.All", "User-PasswordProfile.ReadWrite.All"],
            roles:
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                EligibleRole(EntraRoleCatalog.UserAdministratorTemplateId, PimRequirement.ActivationRequired)
            ]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.PimActivationRequired);
        capabilities[Capability.UsersCreate].RequiredRoleTemplateId.Should().Be(EntraRoleCatalog.UserAdministratorTemplateId);
        capabilities[Capability.UsersCreate].Pim!.State.Should().Be(PimRequirement.ActivationRequired);
    }

    [Theory]
    [InlineData(PimRequirement.ApprovalRequired, CapabilityState.PimApprovalRequired)]
    [InlineData(PimRequirement.MfaRequired, CapabilityState.PimMfaRequired)]
    [InlineData(PimRequirement.EligibilityExpired, CapabilityState.PimEligibilityExpired)]
    public void Pim_failures_are_preserved_as_exact_capability_states(string pimRequirement, string expectedState)
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Create"],
            roles:
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                EligibleRole(EntraRoleCatalog.UserAdministratorTemplateId, pimRequirement)
            ]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersCreate].State.Should().Be(expectedState);
        capabilities[Capability.UsersCreate].Pim!.State.Should().Be(pimRequirement);
    }

    [Fact]
    public void Unknown_pim_status_fails_closed_without_actionable_activation_prompt()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Create"],
            roles:
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                EligibleRole(EntraRoleCatalog.UserAdministratorTemplateId, CapabilityState.TemporarilyUnavailable)
            ]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.TemporarilyUnavailable);
        capabilities[Capability.UsersCreate].ReasonCode.Should().Be("pim_status_unavailable");
        capabilities[Capability.UsersCreate].NextStep!.Label.Should().Be("Retry after PIM status is available");
        capabilities[Capability.UsersCreate].NextStep!.Href.Should().BeNull();
    }

    [Fact]
    public void Administrative_unit_scoped_active_role_does_not_allow_tenant_wide_mutation()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Create"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId, directoryScopeId: "/administrativeUnits/au-1")]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.ReadOnly);
        capabilities[Capability.UsersCreate].ReasonCode.Should().Be("directory_role_scope_not_tenant_wide");
        capabilities[Capability.UsersCreate].RequiredRoleTemplateId.Should().Be(EntraRoleCatalog.UserAdministratorTemplateId);
    }

    [Fact]
    public void Administrative_unit_scoped_eligible_role_does_not_produce_pim_activation_guidance()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Create"],
            roles: [EligibleRole(EntraRoleCatalog.UserAdministratorTemplateId, PimRequirement.ActivationRequired, "/administrativeUnits/au-1")]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.ReadOnly);
        capabilities[Capability.UsersCreate].ReasonCode.Should().Be("directory_role_scope_not_tenant_wide");
        capabilities[Capability.UsersCreate].NextStep.Should().BeNull();
    }

    [Fact]
    public void Unknown_graph_snapshot_fails_closed_for_mutations()
    {
        var snapshot = GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable");

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member(platformRole: "admin"));

        capabilities[Capability.UsersCreate].State.Should().Be(CapabilityState.TemporarilyUnavailable);
        capabilities[Capability.UsersCreate].State.Should().NotBe(CapabilityState.Allowed);
        capabilities[Capability.WorkspaceSettingsManage].State.Should().Be(CapabilityState.Allowed);
    }

    [Theory]
    [InlineData("admin", false)]
    [InlineData("workspace-manager", false)]
    [InlineData("owner", false)]
    [InlineData("member", true)]
    public void Audit_view_is_platform_capability_for_workspace_managers_and_owners(string platformRole, bool hidden)
    {
        var capabilities = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            Member(platformRole));

        capabilities[Capability.AuditView].State.Should().Be(hidden ? CapabilityState.Hidden : CapabilityState.Allowed);
    }

    [Fact]
    public void Audit_view_is_allowed_for_atea_operator_without_graph_availability()
    {
        var capabilities = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            new WorkspaceMembership(Guid.NewGuid(), "Customer workspace", "member", IsAteaOperator: true));

        capabilities[Capability.AuditView].State.Should().Be(CapabilityState.Allowed);
    }

    private static GraphAuthorizationSnapshot AvailableSnapshot(IReadOnlyCollection<string> scopes, IReadOnlyCollection<DirectoryRoleSnapshot> roles) =>
        new(
            IsAvailable: true,
            UserObjectId: "user-1",
            GrantedScopes: scopes,
            DirectoryRoles: roles,
            AdministrativeUnitScopeIds: [],
            TenantPolicyFlags: new Dictionary<string, bool>());

    private static DirectoryRoleSnapshot ActiveRole(string templateId, string directoryScopeId = "/") =>
        new(templateId, "presentation only", DirectoryRoleAssignmentState.Active, DirectoryScopeId: directoryScopeId);

    private static DirectoryRoleSnapshot EligibleRole(string templateId, string pimRequirement, string directoryScopeId = "/") =>
        new(templateId, "presentation only", DirectoryRoleAssignmentState.Eligible, DirectoryScopeId: directoryScopeId, new PimStateSnapshot(pimRequirement, "https://entra.example/activate"));

    private static WorkspaceMembership Member(string platformRole = "member") =>
        new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Customer workspace", platformRole);
}
