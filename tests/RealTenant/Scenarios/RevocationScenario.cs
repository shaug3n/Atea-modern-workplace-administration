namespace Atea.UnifiedWorkplace.RealTenant;

/// <summary>
/// Manual consent and B2B revocation checks. The only network check described
/// here is a read-only connection-health check after the operator changes state
/// in Entra; no membership, role or session is removed by this code.
/// </summary>
public static class RevocationScenario
{
    public static IReadOnlyList<ManualScenarioStep> Steps { get; } =
    [
        new(
            "Establish a connected baseline",
            "POST",
            "/api/workspaces/current/connection-health/check",
            "200 with connected",
            "Run before changing consent. Record only the workspace/tenant object IDs, state and correlation IDs."),
        new(
            "Remove delegated consent manually",
            "MANUAL",
            "Microsoft Entra admin center > Enterprise applications > Permissions",
            "Customer administrator confirms the intended permission removal",
            "This is an explicit tenant-owner action. Do not automate consent revocation or use a production app."),
        new(
            "Observe consent revocation",
            "POST",
            "/api/workspaces/current/connection-health/check",
            "200 with consent_revoked and no false connected state",
            "Use a fresh token/session as needed; do not print the token or Graph error body."),
        new(
            "Restore consent through the approved handoff",
            "POST",
            "/api/workspaces/current/consent/start",
            "200 with the approved scopes; after interactive admin consent, a check returns connected",
            "Consent must be explicit and customer-admin approved. Never widen the scope list to make the check pass."),
        new(
            "Remove the Atea B2B guest role or membership manually",
            "MANUAL",
            "Microsoft Entra admin center > Users / role assignments",
            "Customer administrator confirms the recorded Atea guest object ID no longer has the customer-assigned active role",
            "Role and membership removal are destructive access changes and must be performed only by the tenant owner."),
        new(
            "Verify guest denial after refresh",
            "GET / MANUAL",
            "/api/capabilities and /api/workspaces/current/connection-health",
            "The guest is denied or receives a truthful missing-membership/role/consent explanation after a fresh session",
            "Do not rely on a stale browser session; refresh/sign out interactively and record only object IDs and safe status codes.")
    ];

    public static RealTenantEnvironmentValues? LoadEnvironment(out string reason) => RealTenantEnvironment.Load(out reason);
}
