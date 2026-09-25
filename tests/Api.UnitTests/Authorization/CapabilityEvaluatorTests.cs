using Atea.UnifiedWorkplace.Api.Authorization;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class CapabilityEvaluatorTests
{
    [Fact]
    public void Recovery_capabilities_separate_basic_and_secret_scopes_without_requiring_a_known_role()
    {
        var snapshot = AvailableSnapshot(scopes: ["BitlockerKey.ReadBasic.All", "DeviceLocalCredential.Read.All"], roles: []);
        var decisions = CapabilityEvaluator.Evaluate(snapshot, Member());
        decisions[Capability.DevicesBitlockerMetadata].State.Should().Be(CapabilityState.Allowed);
        decisions[Capability.DevicesBitlockerReveal].State.Should().Be(CapabilityState.ConsentRequired);
        decisions[Capability.DevicesBitlockerReveal].MissingScopes.Should().ContainSingle("BitlockerKey.Read.All");
        decisions[Capability.DevicesLapsMetadata].State.Should().Be(CapabilityState.Allowed);
        decisions[Capability.DevicesLapsReveal].State.Should().Be(CapabilityState.Allowed);
    }

    [Fact]
    public void Known_eligible_PIM_role_adds_guidance_without_hiding_reveal()
    {
        var snapshot = AvailableSnapshot(scopes: ["DeviceLocalCredential.Read.All"],
            roles: [EligibleRole(EntraRoleCatalog.CloudDeviceAdministratorTemplateId, PimRequirement.ActivationRequired)]);
        var decision = CapabilityEvaluator.Evaluate(snapshot, Member())[Capability.DevicesLapsReveal];
        decision.State.Should().Be(CapabilityState.Allowed);
        decision.RequiredRoleTemplateId.Should().Be(EntraRoleCatalog.CloudDeviceAdministratorTemplateId);
        decision.Pim!.State.Should().Be(PimRequirement.ActivationRequired);
        decision.NextStep!.Label.Should().Contain("Cloud Device Administrator");
        decision.NextStep!.Href.Should().Be("/identity");
    }

    [Fact]
    public void Global_reader_eligibility_does_not_claim_to_unlock_LAPS_passwords()
    {
        var snapshot = AvailableSnapshot(scopes: ["DeviceLocalCredential.Read.All"],
            roles: [EligibleRole(EntraRoleCatalog.GlobalReaderTemplateId, PimRequirement.ActivationRequired)]);
        var decision = CapabilityEvaluator.Evaluate(snapshot, Member())[Capability.DevicesLapsReveal];
        decision.State.Should().Be(CapabilityState.Allowed);
        decision.NextStep.Should().BeNull();
    }

    [Theory]
    [InlineData(Capability.DevicesBitlockerMetadata, "729827e3-9c14-49f7-bb1b-9608f156bbb8")]
    [InlineData(Capability.DevicesBitlockerReveal, "729827e3-9c14-49f7-bb1b-9608f156bbb8")]
    [InlineData(Capability.DevicesBitlockerMetadata, "194ae4cb-b126-40b2-bd5b-6091b380977d")]
    [InlineData(Capability.DevicesBitlockerReveal, "5d6b6bb7-de71-4623-b4af-96380a352509")]
    [InlineData(Capability.DevicesLapsMetadata, "729827e3-9c14-49f7-bb1b-9608f156bbb8")]
    [InlineData(Capability.DevicesLapsMetadata, "194ae4cb-b126-40b2-bd5b-6091b380977d")]
    [InlineData(Capability.DevicesLapsMetadata, "5d6b6bb7-de71-4623-b4af-96380a352509")]
    [InlineData(Capability.DevicesLapsMetadata, EntraRoleCatalog.GlobalReaderTemplateId)]
    public void Recovery_capability_guides_eligible_supported_role_without_preempting_Graph(string capability, string roleTemplateId)
    {
        var scopes = capability.Contains("laps", StringComparison.OrdinalIgnoreCase)
            ? new[] { "DeviceLocalCredential.Read.All" } : new[] { "BitlockerKey.Read.All" };
        var snapshot = AvailableSnapshot(scopes, [EligibleRole(roleTemplateId, PimRequirement.ActivationRequired)]);

        var decision = CapabilityEvaluator.Evaluate(snapshot, Member())[capability];

        decision.State.Should().Be(CapabilityState.Allowed);
        decision.NextStep!.Href.Should().Be("/identity");
    }

    [Theory]
    [InlineData("729827e3-9c14-49f7-bb1b-9608f156bbb8")]
    [InlineData("194ae4cb-b126-40b2-bd5b-6091b380977d")]
    [InlineData("5d6b6bb7-de71-4623-b4af-96380a352509")]
    [InlineData(EntraRoleCatalog.GlobalReaderTemplateId)]
    public void LAPS_password_capability_does_not_suggest_activating_a_metadata_only_role(string roleTemplateId)
    {
        var snapshot = AvailableSnapshot(["DeviceLocalCredential.Read.All"], [EligibleRole(roleTemplateId, PimRequirement.ActivationRequired)]);
        var decision = CapabilityEvaluator.Evaluate(snapshot, Member())[Capability.DevicesLapsReveal];
        decision.State.Should().Be(CapabilityState.Allowed);
        decision.NextStep.Should().BeNull();
    }

    [Theory]
    [InlineData(Capability.WorkspaceMembersManage)]
    [InlineData(Capability.WorkspaceSettingsManage)]
    public void Workspace_owner_can_manage_workspace_platform_settings_and_members(string capability)
    {
        var membership = new WorkspaceMembership(Guid.NewGuid(), "Customer workspace", "workspace_owner");

        var decision = CapabilityEvaluator.EvaluatePlatformCapability(capability, membership);

        decision.State.Should().Be(CapabilityState.Allowed);
    }

    [Fact]
    public void Privileged_device_commands_require_privileged_operations_consent()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["DeviceManagementManagedDevices.ReadWrite.All"],
            roles: [ActiveRole(EntraRoleCatalog.IntuneAdministratorTemplateId)]);

        var decision = CapabilityEvaluator.Evaluate(snapshot, Member("admin"))[Capability.DevicesPrivilegedManage];

        decision.State.Should().Be(CapabilityState.ConsentRequired);
        decision.MissingScopes.Should().Contain("DeviceManagementManagedDevices.PrivilegedOperations.All");
    }

    [Fact]
    public void Session_revocation_requires_user_revoke_sessions_scope()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["User.ReadWrite.All"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

        CapabilityEvaluator.Evaluate(snapshot, Member("admin"))[Capability.UsersRevokeSessions]
            .State.Should().Be(CapabilityState.ConsentRequired);
    }

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
    public void PIM_eligible_global_reader_is_guided_to_activate_before_viewing_users()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All", "RoleManagement.Read.Directory"],
            roles: [EligibleRole(EntraRoleCatalog.GlobalReaderTemplateId, PimRequirement.ActivationRequired)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersView].State.Should().Be(CapabilityState.PimActivationRequired);
        capabilities[Capability.UsersView].RequiredRoleTemplateId.Should().Be(EntraRoleCatalog.GlobalReaderTemplateId);
        capabilities[Capability.UsersView].NextStep!.Href.Should().Be("/identity");
    }

    [Fact]
    public void PIM_eligible_role_grants_self_activation_capability()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "RoleManagement.Read.Directory", "RoleManagement.ReadWrite.Directory"],
            roles: [EligibleRole(EntraRoleCatalog.GlobalReaderTemplateId, PimRequirement.ActivationRequired)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.PimActivate].State.Should().Be(CapabilityState.PimActivationRequired);
        capabilities[Capability.PimActivate].ReasonCode.Should().Be(CapabilityState.PimActivationRequired);
        capabilities[Capability.PimActivate].RequiredRoleTemplateId.Should().Be(EntraRoleCatalog.GlobalReaderTemplateId);
    }

    [Fact]
    public void Global_reader_can_view_license_overview_without_assignment_access()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All"],
            roles: [ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.LicensesView].State.Should().Be(CapabilityState.Allowed);
        capabilities[Capability.LicensesAssign].State.Should().Be(CapabilityState.ReadOnly);
    }

    [Fact]
    public void User_administrator_does_not_receive_device_or_authentication_method_visibility()
    {
        var snapshot = AvailableSnapshot(
            scopes: [
                "DeviceManagementManagedDevices.Read.All",
                "UserAuthenticationMethod.Read.All",
                "Directory.Read.All"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.DevicesView].State.Should().Be(CapabilityState.Hidden);
        capabilities[Capability.AuthenticationMethodsView].State.Should().Be(CapabilityState.Hidden);
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
    public void Missing_optional_module_scope_explains_that_delegated_consent_is_required()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "RoleManagement.Read.Directory"],
            roles: [ActiveRole(EntraRoleCatalog.GlobalAdministratorTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.DevicesView].State.Should().Be(CapabilityState.ConsentRequired);
        capabilities[Capability.DevicesView].ReasonCode.Should().Be("delegated_scope_required");
        capabilities[Capability.DevicesView].NextStep!.Label.Should().Be("Grant delegated consent");
        capabilities[Capability.DevicesView].MissingScopes.Should().ContainSingle("DeviceManagementManagedDevices.Read.All");

        var manageMissingScopes = capabilities[Capability.DevicesManage].MissingScopes!;
        manageMissingScopes.Should().BeEquivalentTo([
            "DeviceManagementManagedDevices.Read.All",
            "DeviceManagementManagedDevices.ReadWrite.All"]);
    }

    [Fact]
    public void Missing_user_write_scope_identifies_the_scope_required_for_editing_users()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All"],
            roles: [ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.UsersUpdate].State.Should().Be(CapabilityState.ConsentRequired);
        capabilities[Capability.UsersUpdate].MissingScopes.Should().ContainSingle("User.ReadWrite.All");
    }

    [Fact]
    public void Transient_scope_probe_failure_is_not_reported_as_missing_consent()
    {
        var scope = "DeviceManagementManagedDevices.Read.All";
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All"],
            roles: [ActiveRole(EntraRoleCatalog.GlobalAdministratorTemplateId)],
            scopeAvailability: new Dictionary<string, bool> { [scope] = false },
            scopeProblems: new Dictionary<string, string> { [scope] = "temporarily_unavailable" });

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        capabilities[Capability.DevicesView].State.Should().Be(CapabilityState.TemporarilyUnavailable);
        capabilities[Capability.DevicesView].ReasonCode.Should().Be("scope_probe_unavailable");
        capabilities[Capability.DevicesView].NextStep!.Label.Should().Be("Retry authorization checks");
        capabilities[Capability.DevicesView].MissingScopes.Should().ContainSingle(scope);
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

    private static GraphAuthorizationSnapshot AvailableSnapshot(
        IReadOnlyCollection<string> scopes,
        IReadOnlyCollection<DirectoryRoleSnapshot> roles,
        IReadOnlyDictionary<string, bool>? scopeAvailability = null,
        IReadOnlyDictionary<string, string>? scopeProblems = null) =>
        new(
            IsAvailable: true,
            UserObjectId: "user-1",
            GrantedScopes: scopes,
            DirectoryRoles: roles,
            AdministrativeUnitScopeIds: [],
            TenantPolicyFlags: new Dictionary<string, bool>(),
            ScopeAvailability: scopeAvailability,
            ScopeProblems: scopeProblems);

    private static DirectoryRoleSnapshot ActiveRole(string templateId, string directoryScopeId = "/") =>
        new(templateId, "presentation only", DirectoryRoleAssignmentState.Active, DirectoryScopeId: directoryScopeId);

    private static DirectoryRoleSnapshot EligibleRole(string templateId, string pimRequirement, string directoryScopeId = "/") =>
        new(templateId, "presentation only", DirectoryRoleAssignmentState.Eligible, DirectoryScopeId: directoryScopeId, new PimStateSnapshot(pimRequirement, "https://entra.example/activate"));

    private static WorkspaceMembership Member(string platformRole = "member") =>
        new(Guid.Parse("55555555-5555-5555-5555-555555555555"), "Customer workspace", platformRole);
}
