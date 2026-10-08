using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Authorization;

public sealed class CapabilityEvaluatorTests
{
    [Fact]
    public void Authentication_campaign_view_is_activated_but_management_remains_reserved()
    {
        EntraRoleCatalog.ReportsReaderTemplateId.Should().Be("4a5d8f65-41da-4de4-8968-e035b65339cf");
        EntraRoleCatalog.DisplayNames[EntraRoleCatalog.ReportsReaderTemplateId].Should().Be("Reports Reader");
        Capability.Reserved.Should().Contain(Capability.AuthenticationCampaignsManage);
        Capability.Reserved.Should().NotContain(Capability.AuthenticationCampaignsView);
        Capability.All.Should().Contain(Capability.AuthenticationCampaignsView);
        Capability.All.Should().NotContain(Capability.AuthenticationCampaignsManage);

        var snapshot = AvailableSnapshot(
            GraphScopeCatalog.AuthenticationCampaignReportScopes,
            [ActiveRole(EntraRoleCatalog.ReportsReaderTemplateId)]);
        var decisions = CapabilityEvaluator.Evaluate(snapshot, Member());
        decisions.Capabilities.Select(decision => decision.Capability).Should().Contain(Capability.AuthenticationCampaignsView);
        decisions.Capabilities.Select(decision => decision.Capability).Should().NotContain(Capability.AuthenticationCampaignsManage);
        decisions[Capability.AuthenticationCampaignsView].State.Should().Be(CapabilityState.Allowed);
    }

    [Theory]
    [InlineData(EntraRoleCatalog.ReportsReaderTemplateId)]
    [InlineData(EntraRoleCatalog.SecurityReaderTemplateId)]
    [InlineData(EntraRoleCatalog.SecurityAdministratorTemplateId)]
    [InlineData(EntraRoleCatalog.GlobalReaderTemplateId)]
    public void Authentication_campaign_view_allows_supported_tenant_wide_active_roles(string roleTemplateId)
    {
        var decision = CapabilityEvaluator.Evaluate(
            AvailableSnapshot(GraphScopeCatalog.AuthenticationCampaignReportScopes, [ActiveRole(roleTemplateId)]),
            Member())[Capability.AuthenticationCampaignsView];

        decision.State.Should().Be(CapabilityState.Allowed);
    }

    [Fact]
    public void Authentication_campaign_view_requires_audit_log_read_scope()
    {
        var decision = CapabilityEvaluator.Evaluate(
            AvailableSnapshot(["Directory.Read.All"], [ActiveRole(EntraRoleCatalog.ReportsReaderTemplateId)]),
            Member())[Capability.AuthenticationCampaignsView];

        decision.State.Should().Be(CapabilityState.ConsentRequired);
        decision.MissingScopes.Should().ContainSingle("AuditLog.Read.All");
    }

    [Fact]
    public void Authentication_campaign_view_does_not_allow_unsupported_roles()
    {
        var decision = CapabilityEvaluator.Evaluate(
            AvailableSnapshot(GraphScopeCatalog.AuthenticationCampaignReportScopes, [ActiveRole(EntraRoleCatalog.GlobalAdministratorTemplateId)]),
            Member())[Capability.AuthenticationCampaignsView];

        decision.State.Should().Be(CapabilityState.Hidden);
        decision.State.Should().NotBe(CapabilityState.Allowed);
    }

    [Theory]
    [InlineData(PimRequirement.ActivationRequired, CapabilityState.PimActivationRequired)]
    [InlineData(PimRequirement.ApprovalRequired, CapabilityState.PimApprovalRequired)]
    [InlineData(PimRequirement.MfaRequired, CapabilityState.PimMfaRequired)]
    [InlineData(PimRequirement.EligibilityExpired, CapabilityState.PimEligibilityExpired)]
    public void Authentication_campaign_view_preserves_supported_eligible_role_PIM_state(
        string pimRequirement,
        string expectedState)
    {
        var decision = CapabilityEvaluator.Evaluate(
            AvailableSnapshot(
                GraphScopeCatalog.AuthenticationCampaignReportScopes,
                [EligibleRole(EntraRoleCatalog.ReportsReaderTemplateId, pimRequirement)]),
            Member())[Capability.AuthenticationCampaignsView];

        decision.State.Should().Be(expectedState);
        decision.State.Should().NotBe(CapabilityState.Allowed);
    }

    [Theory]
    [InlineData(DirectoryRoleAssignmentState.Active)]
    [InlineData(DirectoryRoleAssignmentState.Eligible)]
    public void Authentication_campaign_view_does_not_allow_administrative_unit_scoped_roles(string assignmentState)
    {
        var role = assignmentState == DirectoryRoleAssignmentState.Active
            ? ActiveRole(EntraRoleCatalog.ReportsReaderTemplateId, "/administrativeUnits/au-1")
            : EligibleRole(EntraRoleCatalog.ReportsReaderTemplateId, PimRequirement.ActivationRequired, "/administrativeUnits/au-1");
        var decision = CapabilityEvaluator.Evaluate(
            AvailableSnapshot(GraphScopeCatalog.AuthenticationCampaignReportScopes, [role]),
            Member())[Capability.AuthenticationCampaignsView];

        decision.State.Should().NotBe(CapabilityState.Allowed);
        decision.State.Should().NotBe(CapabilityState.ReadOnly);
        decision.ReasonCode.Should().Be("directory_role_scope_not_tenant_wide");
    }

    [Fact]
    public void Active_hygiene_capability_is_evaluated_and_other_reserved_capabilities_remain_inactive()
    {
        var reserved = new[]
        {
            Capability.AuthenticationCampaignsManage
        };
        var decisions = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            Member());

        Capability.Reserved.Should().Equal(reserved);
        Capability.All.Should().Contain(Capability.LicensesHygieneView);
        decisions.Capabilities.Select(decision => decision.Capability).Should().Contain(Capability.LicensesHygieneView);
        foreach (var capability in reserved)
        {
            Capability.All.Should().NotContain(capability);
            decisions.Capabilities.Select(decision => decision.Capability).Should().NotContain(capability);
        }
    }

    [Fact]
    public void Hygiene_view_uses_the_existing_license_read_contract()
    {
        var readerRoles = new[]
        {
            EntraRoleCatalog.GlobalReaderTemplateId,
            EntraRoleCatalog.LicenseAdministratorTemplateId
        };
        foreach (var roleTemplateId in readerRoles)
        {
            var withReadScope = CapabilityEvaluator.Evaluate(
                AvailableSnapshot(
                    ["Directory.Read.All"],
                    [ActiveRole(roleTemplateId)]),
                Member());

            withReadScope[Capability.LicensesView].State.Should().Be(withReadScope[Capability.LicensesHygieneView].State);
            withReadScope[Capability.LicensesView].ReasonCode.Should().Be(withReadScope[Capability.LicensesHygieneView].ReasonCode);
            withReadScope[Capability.LicensesHygieneView].State.Should().Be(CapabilityState.Allowed);
            withReadScope[Capability.LicensesAssign].State.Should().Be(
                roleTemplateId == EntraRoleCatalog.GlobalReaderTemplateId
                    ? CapabilityState.ReadOnly
                    : CapabilityState.ConsentRequired);

            var withoutReadScope = CapabilityEvaluator.Evaluate(
                AvailableSnapshot(["LicenseAssignment.ReadWrite.All"], [ActiveRole(roleTemplateId)]),
                Member());
            withoutReadScope[Capability.LicensesView].State.Should().Be(withoutReadScope[Capability.LicensesHygieneView].State);
            withoutReadScope[Capability.LicensesView].ReasonCode.Should().Be(withoutReadScope[Capability.LicensesHygieneView].ReasonCode);
            withoutReadScope[Capability.LicensesHygieneView].State.Should().Be(CapabilityState.Hidden);
            withoutReadScope[Capability.LicensesAssign].State.Should().Be(CapabilityState.Hidden);

            var withWriteScope = CapabilityEvaluator.Evaluate(
                AvailableSnapshot(["Directory.Read.All", "LicenseAssignment.ReadWrite.All"], [ActiveRole(roleTemplateId)]),
                Member());
            withWriteScope[Capability.LicensesHygieneView].State.Should().Be(CapabilityState.Allowed);
            withWriteScope[Capability.LicensesAssign].State.Should().Be(
                roleTemplateId == EntraRoleCatalog.GlobalReaderTemplateId
                    ? CapabilityState.ReadOnly
                    : CapabilityState.Allowed);
        }
    }

    [Theory]
    [InlineData(PimRequirement.ActivationRequired, CapabilityState.PimActivationRequired)]
    [InlineData(PimRequirement.ApprovalRequired, CapabilityState.PimApprovalRequired)]
    [InlineData(PimRequirement.MfaRequired, CapabilityState.PimMfaRequired)]
    [InlineData(PimRequirement.EligibilityExpired, CapabilityState.PimEligibilityExpired)]
    public void Hygiene_PIM_decisions_match_license_view(string pimRequirement, string expectedState)
    {
        var snapshot = AvailableSnapshot(
            ["Directory.Read.All"],
            [EligibleRole(EntraRoleCatalog.LicenseAdministratorTemplateId, pimRequirement)]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());

        var licensesView = capabilities[Capability.LicensesView];
        var hygieneView = capabilities[Capability.LicensesHygieneView];
        licensesView.State.Should().Be(expectedState);
        hygieneView.State.Should().Be(licensesView.State);
        hygieneView.ReasonCode.Should().Be(licensesView.ReasonCode);
        hygieneView.RequiredRoleTemplateId.Should().Be(licensesView.RequiredRoleTemplateId);
        hygieneView.Pim.Should().Be(licensesView.Pim);
        hygieneView.NextStep.Should().Be(licensesView.NextStep);
        capabilities[Capability.LicensesAssign].State.Should().Be(CapabilityState.ConsentRequired);
    }

    [Fact]
    public void Hygiene_role_evidence_uses_the_evaluated_license_reader_requirements()
    {
        var snapshot = AvailableSnapshot(
            ["Directory.Read.All"],
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                ActiveRole(EntraRoleCatalog.LicenseAdministratorTemplateId)
            ]);

        var decisions = CapabilityEvaluator.Evaluate(snapshot, Member());
        var hygiene = decisions[Capability.LicensesHygieneView].RoleEvidence!;
        var licenses = decisions[Capability.LicensesView].RoleEvidence!;

        hygiene.Should().BeEquivalentTo(licenses);
        hygiene.State.Should().Be("available");
        hygiene.RequiredRoleTemplateIds.Should().Contain(EntraRoleCatalog.LicenseAdministratorTemplateId);
        hygiene.Assignments.Select(assignment => assignment.RoleTemplateId).Should().Contain(
            [EntraRoleCatalog.GlobalReaderTemplateId, EntraRoleCatalog.LicenseAdministratorTemplateId]);
    }

    [Fact]
    public void Workspace_module_evidence_uses_effective_catalog_rules()
    {
        var membership = Member() with { ModuleKeys = ["users", "feedback"] };

        var snapshot = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            membership,
            ["users", "exchange", "authentication-campaigns"]);

        snapshot.WorkspaceModules.Should().BeEquivalentTo(
        [
            new WorkspaceModuleEvidence("users", "explicit", Enabled: true, Effective: true),
            new WorkspaceModuleEvidence("devices", "none", Enabled: false, Effective: false),
            new WorkspaceModuleEvidence("licenses", "none", Enabled: false, Effective: false),
            new WorkspaceModuleEvidence("exchange", "none", Enabled: true, Effective: false),
            new WorkspaceModuleEvidence("authentication-campaigns", "none", Enabled: true, Effective: false),
            new WorkspaceModuleEvidence("license-hygiene", "none", Enabled: false, Effective: false)
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public void Workspace_owner_evidence_is_inherited_only_for_enabled_modules()
    {
        var membership = Member("workspace_owner") with { ModuleKeys = [] };

        var snapshot = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            membership,
            ["users", "exchange", "license-hygiene"]);

        snapshot.WorkspaceModules.Should().BeEquivalentTo(
        [
            new WorkspaceModuleEvidence("users", "owner_inherited", Enabled: true, Effective: true),
            new WorkspaceModuleEvidence("devices", "none", Enabled: false, Effective: false),
            new WorkspaceModuleEvidence("licenses", "none", Enabled: false, Effective: false),
            new WorkspaceModuleEvidence("exchange", "owner_inherited", Enabled: true, Effective: true),
            new WorkspaceModuleEvidence("authentication-campaigns", "none", Enabled: false, Effective: false),
            new WorkspaceModuleEvidence("license-hygiene", "owner_inherited", Enabled: true, Effective: true)
        ], options => options.WithStrictOrdering());
    }

    [Fact]
    public void Role_evidence_is_filtered_to_existing_capability_requirements()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All", "DeviceManagementManagedDevices.Read.All"],
            roles:
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                ActiveRole(EntraRoleCatalog.UserAdministratorTemplateId, "/administrativeUnits/au-1"),
                ActiveRole(EntraRoleCatalog.IntuneAdministratorTemplateId),
                EligibleRole(EntraRoleCatalog.CloudDeviceAdministratorTemplateId, PimRequirement.ActivationRequired)
            ]);

        var capabilities = CapabilityEvaluator.Evaluate(snapshot, Member());
        var users = capabilities[Capability.UsersView].RoleEvidence!;
        var devices = capabilities[Capability.DevicesView].RoleEvidence!;
        var recovery = capabilities[Capability.DevicesLapsReveal].RoleEvidence!;

        users.State.Should().Be("available");
        users.RequiredRoleTemplateIds.Should().BeEquivalentTo([
            EntraRoleCatalog.GlobalAdministratorTemplateId,
            EntraRoleCatalog.GlobalReaderTemplateId,
            EntraRoleCatalog.UserAdministratorTemplateId
        ]);
        users.Assignments.Select(assignment => assignment.RoleTemplateId)
            .Should().BeEquivalentTo([
                EntraRoleCatalog.GlobalReaderTemplateId,
                EntraRoleCatalog.UserAdministratorTemplateId
            ]);
        users.Assignments.Single(assignment => assignment.RoleTemplateId == EntraRoleCatalog.UserAdministratorTemplateId)
            .Scope.Should().Be("scoped");
        System.Text.Json.JsonSerializer.Serialize(users).Should().NotContain("/administrativeUnits/au-1");

        devices.RequiredRoleTemplateIds.Should().BeEquivalentTo([
            EntraRoleCatalog.GlobalAdministratorTemplateId,
            EntraRoleCatalog.GlobalReaderTemplateId,
            EntraRoleCatalog.IntuneAdministratorTemplateId,
            EntraRoleCatalog.CloudDeviceAdministratorTemplateId
        ]);
        devices.Assignments.Select(assignment => assignment.RoleTemplateId)
            .Should().BeEquivalentTo([
                EntraRoleCatalog.GlobalReaderTemplateId,
                EntraRoleCatalog.IntuneAdministratorTemplateId,
                EntraRoleCatalog.CloudDeviceAdministratorTemplateId
            ]);
        devices.Assignments.Single(assignment => assignment.RoleTemplateId == EntraRoleCatalog.CloudDeviceAdministratorTemplateId)
            .Should().BeEquivalentTo(new CapabilityRoleAssignmentEvidence(
                EntraRoleCatalog.CloudDeviceAdministratorTemplateId,
                DirectoryRoleAssignmentState.Eligible,
                "tenant_wide",
                PimRequirement.ActivationRequired));
        recovery.RequiredRoleTemplateIds.Should().BeEquivalentTo([
            EntraRoleCatalog.CloudDeviceAdministratorTemplateId,
            EntraRoleCatalog.IntuneAdministratorTemplateId
        ]);
        recovery.Assignments.Select(assignment => assignment.RoleTemplateId)
            .Should().BeEquivalentTo([
                EntraRoleCatalog.CloudDeviceAdministratorTemplateId,
                EntraRoleCatalog.IntuneAdministratorTemplateId
            ]);
    }

    [Fact]
    public void Role_evidence_distinguishes_unavailable_and_not_applicable()
    {
        var unavailable = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            Member());

        unavailable[Capability.UsersView].RoleEvidence!.State.Should().Be("unavailable");
        unavailable[Capability.UsersView].RoleEvidence!.Assignments.Should().BeEmpty();
        unavailable[Capability.WorkspaceSettingsManage].RoleEvidence.Should().BeEquivalentTo(
            new CapabilityRoleEvidence("not_applicable", [], []));
    }

    [Fact]
    public void Decision_states_and_existing_fields_are_unchanged_when_workspace_module_evidence_is_projected()
    {
        var snapshot = AvailableSnapshot(
            scopes: ["Directory.Read.All", "User.Read.All", "User.Create"],
            roles:
            [
                ActiveRole(EntraRoleCatalog.GlobalReaderTemplateId),
                EligibleRole(EntraRoleCatalog.UserAdministratorTemplateId, PimRequirement.ActivationRequired)
            ]);
        var membership = Member();
        var withoutProjection = CapabilityEvaluator.Evaluate(snapshot, membership);
        var withProjection = CapabilityEvaluator.Evaluate(snapshot, membership, ["users"]);

        withoutProjection.WorkspaceModules.Should().BeNull();
        withProjection.Capabilities.Select(ToLegacyDecision).Should()
            .BeEquivalentTo(withoutProjection.Capabilities.Select(ToLegacyDecision), options => options.WithStrictOrdering());
    }

    [Fact]
    public void About_and_feedback_are_allowed_for_member_without_graph_authority()
    {
        var decisions = CapabilityEvaluator.Evaluate(
            GraphAuthorizationSnapshot.Unavailable("temporarily_unavailable"),
            Member("member"));

        Capability.All.Should().Contain(Capability.PlatformAboutView);
        Capability.All.Should().Contain(Capability.FeedbackSubmit);
        Capability.Reserved.Should().NotContain(Capability.PlatformAboutView);
        Capability.Reserved.Should().NotContain(Capability.FeedbackSubmit);
        decisions[Capability.PlatformAboutView].State.Should().Be(CapabilityState.Allowed);
        decisions[Capability.FeedbackSubmit].State.Should().Be(CapabilityState.Allowed);
    }

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
    public void License_administrator_can_view_inventory_with_read_scope_but_assignment_requires_write_scope()
    {
        var role = ActiveRole(EntraRoleCatalog.LicenseAdministratorTemplateId);
        var readOnly = CapabilityEvaluator.Evaluate(AvailableSnapshot(["Directory.Read.All", "User.Read.All"], [role]), Member());
        readOnly[Capability.LicensesView].State.Should().Be(CapabilityState.Allowed);
        readOnly[Capability.LicensesAssign].State.Should().Be(CapabilityState.ConsentRequired);

        var withWrite = CapabilityEvaluator.Evaluate(AvailableSnapshot(["Directory.Read.All", "User.Read.All", "LicenseAssignment.ReadWrite.All"], [role]), Member());
        withWrite[Capability.LicensesView].State.Should().Be(CapabilityState.Allowed);
        withWrite[Capability.LicensesAssign].State.Should().Be(CapabilityState.Allowed);
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

    private static object ToLegacyDecision(CapabilityDecision decision) => new
    {
        decision.Capability,
        decision.State,
        decision.ReasonCode,
        decision.RequiredRoleTemplateId,
        decision.Pim,
        decision.NextStep,
        decision.MissingScopes
    };
}
