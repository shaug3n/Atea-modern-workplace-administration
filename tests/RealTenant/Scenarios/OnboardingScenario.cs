namespace Atea.UnifiedWorkplace.RealTenant;

/// <summary>
/// Environment and runbook primitives shared by the manual real-tenant scenarios.
/// This directory is intentionally not a test project: the records below are a
/// compile-ready scenario manifest for an operator following README.md.
/// </summary>
public sealed record RealTenantEnvironmentValues(
    string BaseUrl,
    string TenantId,
    string AccessToken,
    string? WorkspaceId,
    string? CustomerAdminObjectId,
    string? GlobalReaderObjectId,
    string? UserAdministratorObjectId,
    string? PimEligibleObjectId,
    string? PimApprovalObjectId,
    string? AteaGuestObjectId,
    string? TestUserObjectId,
    string? TestGroupObjectId,
    string? InvitationNonce);

public sealed record ManualScenarioStep(
    string Name,
    string Method,
    string Path,
    string Expected,
    string SafetyNote);

public static class RealTenantEnvironment
{
    public const string RunVariable = "ATEA_REAL_TENANT_RUN";
    public const string BaseUrlVariable = "ATEA_REAL_TENANT_BASE_URL";
    public const string TenantIdVariable = "ATEA_REAL_TENANT_TENANT_ID";
    public const string AccessTokenVariable = "ATEA_REAL_TENANT_ACCESS_TOKEN";
    public const string WorkspaceIdVariable = "ATEA_REAL_TENANT_WORKSPACE_ID";
    public const string CustomerAdminObjectIdVariable = "ATEA_REAL_TENANT_CUSTOMER_ADMIN_OBJECT_ID";
    public const string GlobalReaderObjectIdVariable = "ATEA_REAL_TENANT_GLOBAL_READER_OBJECT_ID";
    public const string UserAdministratorObjectIdVariable = "ATEA_REAL_TENANT_USER_ADMINISTRATOR_OBJECT_ID";
    public const string PimEligibleObjectIdVariable = "ATEA_REAL_TENANT_PIM_ELIGIBLE_OBJECT_ID";
    public const string PimApprovalObjectIdVariable = "ATEA_REAL_TENANT_PIM_APPROVAL_OBJECT_ID";
    public const string AteaGuestObjectIdVariable = "ATEA_REAL_TENANT_ATEA_GUEST_OBJECT_ID";
    public const string TestUserObjectIdVariable = "ATEA_REAL_TENANT_TEST_USER_OBJECT_ID";
    public const string TestGroupObjectIdVariable = "ATEA_REAL_TENANT_TEST_GROUP_OBJECT_ID";
    public const string InvitationNonceVariable = "ATEA_REAL_TENANT_INVITATION_NONCE";

    /// <summary>
    /// Returns null when the harness is not explicitly enabled. The access token
    /// and invitation nonce are read into memory only and are never included in
    /// scenario output or evidence.
    /// </summary>
    public static RealTenantEnvironmentValues? Load(out string reason)
    {
        if (!string.Equals(Environment.GetEnvironmentVariable(RunVariable), "true", StringComparison.OrdinalIgnoreCase))
        {
            reason = $"Set {RunVariable}=true to opt in; no tenant call is permitted otherwise.";
            return null;
        }

        var baseUrl = Environment.GetEnvironmentVariable(BaseUrlVariable)?.Trim();
        var tenantId = Environment.GetEnvironmentVariable(TenantIdVariable)?.Trim();
        var accessToken = Environment.GetEnvironmentVariable(AccessTokenVariable);

        if (string.IsNullOrWhiteSpace(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out var parsedBaseUrl) || parsedBaseUrl.Scheme != Uri.UriSchemeHttps)
        {
            reason = $"{BaseUrlVariable} must be an absolute HTTPS URL.";
            return null;
        }

        if (!Guid.TryParse(tenantId, out _))
        {
            reason = $"{TenantIdVariable} must contain the dedicated tenant object ID (GUID).";
            return null;
        }

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            reason = $"{AccessTokenVariable} is required in process memory for the manual API call.";
            return null;
        }

        var invalidObjectId = ObjectIdVariables
            .Select(variable => (variable, value: Environment.GetEnvironmentVariable(variable)))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.value) && !Guid.TryParse(entry.value, out _))
            .Select(entry => entry.variable)
            .FirstOrDefault();
        if (invalidObjectId is not null)
        {
            reason = $"{invalidObjectId} must be a GUID object ID when supplied.";
            return null;
        }

        reason = "Opted in; values loaded without logging secrets or raw Graph payloads.";
        return new RealTenantEnvironmentValues(
            parsedBaseUrl.ToString().TrimEnd('/'),
            tenantId!,
            accessToken,
            Optional(WorkspaceIdVariable),
            Optional(CustomerAdminObjectIdVariable),
            Optional(GlobalReaderObjectIdVariable),
            Optional(UserAdministratorObjectIdVariable),
            Optional(PimEligibleObjectIdVariable),
            Optional(PimApprovalObjectIdVariable),
            Optional(AteaGuestObjectIdVariable),
            Optional(TestUserObjectIdVariable),
            Optional(TestGroupObjectIdVariable),
            Environment.GetEnvironmentVariable(InvitationNonceVariable));
    }

    private static readonly string[] ObjectIdVariables =
    [
        WorkspaceIdVariable,
        CustomerAdminObjectIdVariable,
        GlobalReaderObjectIdVariable,
        UserAdministratorObjectIdVariable,
        PimEligibleObjectIdVariable,
        PimApprovalObjectIdVariable,
        AteaGuestObjectIdVariable,
        TestUserObjectIdVariable,
        TestGroupObjectIdVariable
    ];

    private static string? Optional(string variable) =>
        string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(variable))
            ? null
            : Environment.GetEnvironmentVariable(variable)!.Trim();
}

public static class OnboardingScenario
{
    public static IReadOnlyList<ManualScenarioStep> Steps { get; } =
    [
        new(
            "Provision or select the workspace",
            "POST",
            "/api/platform/workspaces",
            "201 Created, or 409 workspace_already_exists when the recorded workspace object ID is reused",
            "Run only with the platform-admin token in the dedicated test tenant. Never point this at production."),
        new(
            "Create the one-time invitation",
            "POST",
            "/api/platform/workspaces/{workspaceId}/invitations",
            "200 with an invitation URL; copy it once through the approved secure channel and do not log it",
            "The invitation nonce is a credential. Keep it transient; do not put it in evidence or source control."),
        new(
            "Redeem as the customer administrator",
            "POST",
            "/api/invitations/{nonce}/redeem",
            "200 with status consent_required",
            "Switch to the customer-admin session. The API must validate tenant, object ID, email match and nonce expiry."),
        new(
            "Start delegated consent",
            "POST",
            "/api/workspaces/current/consent/start",
            "200 with the approved delegated scope list and an authorization URL",
            "Explicit customer-admin consent is required. Do not grant extra Graph scopes."),
        new(
            "Verify the delegated connection",
            "POST",
            "/api/workspaces/current/connection-health/check",
            "200 with status connected, or a truthful permission/availability state",
            "Record only status, approved scope names, correlation ID and object IDs."),
        new(
            "Verify revoked consent",
            "POST",
            "/api/workspaces/current/connection-health/check",
            "200 with status consent_revoked after the operator removes consent in Entra",
            "Revocation is a manual Entra action. This scenario never revokes consent automatically.")
    ];

    public static RealTenantEnvironmentValues? LoadEnvironment(out string reason) => RealTenantEnvironment.Load(out reason);
}
