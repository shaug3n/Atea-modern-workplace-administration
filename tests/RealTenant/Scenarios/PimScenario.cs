namespace Atea.UnifiedWorkplace.RealTenant;

/// <summary>
/// Manual Microsoft Entra directory-role PIM checks. Activation is represented
/// as a guided operator step and is never silently requested by this harness.
/// </summary>
public static class PimScenario
{
    public const string PrivilegedRoleAdministratorTemplateId = "e8611ab8-c189-46e8-94e1-60213ab1f814";
    public const string UserAdministratorTemplateId = "fe930be7-5e62-47db-91af-98c3a49a38b1";

    public static IReadOnlyList<ManualScenarioStep> Steps { get; } =
    [
        new(
            "Read an eligible inactive role",
            "GET",
            "/api/users/{pimEligibleObjectId}/pim",
            "200 with a role status of eligible_inactive and activation available",
            "Use a disposable eligible assignment. No activation request is made by this manifest."),
        new(
            "Request activation with explicit confirmation",
            "MANUAL",
            "/api/pim/activations",
            "Operator chooses a supported duration, justification and confirmation; result is active or activation_pending only after Graph accepts it",
            "This is a privileged mutation. Require customer-owner consent, a fresh Idempotency-Key and the tenant PIM policy's MFA/approval steps."),
        new(
            "Verify the Graph-backed active state",
            "GET",
            "/api/users/{pimEligibleObjectId}/pim",
            "200 with active after successful activation, otherwise the exact pending/policy state",
            "Never claim active based only on a submitted request; refresh the Graph-backed state."),
        new(
            "Exercise approval-required PIM",
            "GET / MANUAL",
            "/api/users/{pimApprovalObjectId}/pim and /api/pim/activations",
            "Read shows approval_required; activation stays pending/guided and does not bypass approval",
            "Approval is completed by the tenant's designated approver in Entra. Do not approve or activate from automation."),
        new(
            "Exercise MFA-required PIM",
            "GET / MANUAL",
            "/api/users/{pimEligibleObjectId}/pim and /api/pim/activations",
            "Read/activation exposes mfa_required or a guided handoff; no path silently activates",
            "Complete MFA interactively in the tenant. Do not store claims, tokens or raw Graph responses."),
        new(
            "Confirm unsupported PIM boundaries",
            "MANUAL",
            "Azure resource-role or group-PIM admin center flows",
            "The MVP does not treat unsupported targets as directory-role activation and returns a safe non-success state",
            "Do not convert this check into an Azure resource or group mutation."
        )
    ];

    public static RealTenantEnvironmentValues? LoadEnvironment(out string reason) => RealTenantEnvironment.Load(out reason);
}
