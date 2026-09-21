namespace Atea.UnifiedWorkplace.Api.Infrastructure.Http;

public static class ApiProblemCode
{
    public const string AuthenticationRequired = "authentication_required";
    public const string AuthorizationDenied = "authorization_denied";
    public const string CapabilityRequired = "capability_required";
    public const string ConsentRequired = "consent_required";
    public const string Throttled = "throttled";
    public const string ValidationFailed = "validation_failed";
    public const string Conflict = "conflict";
    public const string NotFound = "not_found";
    public const string TransientGraphFailure = "transient_graph_failure";
}
