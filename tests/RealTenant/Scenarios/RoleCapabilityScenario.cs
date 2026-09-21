namespace Atea.UnifiedWorkplace.RealTenant;

/// <summary>
/// Manual role/capability checks. Mutation rows are intentionally operator-run
/// only; this manifest never sends a write request to Microsoft Graph.
/// </summary>
public static class RoleCapabilityScenario
{
    public const string GlobalReaderTemplateId = "f2ef992c-3afb-46b9-b7cf-a126ee74c451";
    public const string UserAdministratorTemplateId = "fe930be7-5e62-47db-91af-98c3a49a38b1";

    public static IReadOnlyList<ManualScenarioStep> Steps { get; } =
    [
        new(
            "Read capabilities as Global Reader",
            "GET",
            "/api/capabilities",
            "users.view=allowed; user mutation capabilities are read_only or another explicit non-allowed state",
            "Use the recorded Global Reader object ID and a fresh delegated token. Do not infer authority from the display name."),
        new(
            "Read permitted directory data",
            "GET",
            "/api/users?pageSize=1",
            "200 with directory data or a truthful unavailable/permission state",
            "Only inspect the minimum data needed for the check; do not save raw response bodies."),
        new(
            "Confirm Global Reader mutation denial",
            "MANUAL",
            "/api/users/{testUserObjectId}/disable",
            "Do not execute automatically. If the operator explicitly approves a disposable check, expect 403 with a capability/read_only explanation.",
            "Disabling a user is a destructive operation. Prefer the UI permission state and leave this endpoint untouched."),
        new(
            "Read capabilities as User Administrator",
            "GET",
            "/api/capabilities",
            "users.create/users.update/users.disable are allowed when consent and tenant-wide role scope are present",
            "Use only the recorded User Administrator object ID. The API remains the security boundary."),
        new(
            "Inspect a disposable test user",
            "GET",
            "/api/users/{testUserObjectId}",
            "200 with the expected user detail sections and access decisions",
            "Use a pre-created disposable user object ID; do not create a user or alter its account in this manifest."),
        new(
            "Perform an approved lifecycle operation",
            "MANUAL",
            "/api/users/{testUserObjectId} or /api/users/{testUserObjectId}/disable",
            "Operator may use the approved UI flow with a fresh Idempotency-Key; record only outcome, capability state and correlation IDs",
            "Require explicit customer-owner approval. Never automate create, disable, license or group membership mutations here.")
    ];

    public static RealTenantEnvironmentValues? LoadEnvironment(out string reason) => RealTenantEnvironment.Load(out reason);
}
