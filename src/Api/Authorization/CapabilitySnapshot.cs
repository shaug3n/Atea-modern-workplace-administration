namespace Atea.UnifiedWorkplace.Api.Authorization;

public sealed record CapabilitySnapshot(
    Guid WorkspaceId,
    DateTimeOffset EvaluatedAt,
    IReadOnlyList<CapabilityDecision> Capabilities,
    string? SourceState = null,
    string? SourceReasonCode = null)
{
    public CapabilityDecision this[string capability] =>
        Capabilities.Single(entry => string.Equals(entry.Capability, capability, StringComparison.OrdinalIgnoreCase));
}

public sealed record CapabilityDecision(
    string Capability,
    string State,
    string ReasonCode,
    string? RequiredRoleTemplateId = null,
    CapabilityPimState? Pim = null,
    CapabilityNextStep? NextStep = null,
    IReadOnlyCollection<string>? MissingScopes = null);

public sealed record CapabilityPimState(string State, string? ActivationUrl = null);

public sealed record CapabilityNextStep(string Label, string? Href = null);

public sealed record GraphAuthorizationSnapshot(
    bool IsAvailable,
    string? UserObjectId,
    IReadOnlyCollection<string> GrantedScopes,
    IReadOnlyCollection<DirectoryRoleSnapshot> DirectoryRoles,
    IReadOnlyCollection<string> AdministrativeUnitScopeIds,
    IReadOnlyDictionary<string, bool> TenantPolicyFlags,
    string? ProblemCategory = null,
    bool ConsentRequired = false,
    IReadOnlyDictionary<string, bool>? ScopeAvailability = null,
    IReadOnlyDictionary<string, string>? ScopeProblems = null)
{
    public static GraphAuthorizationSnapshot Available(
        string userObjectId,
        IReadOnlyCollection<string> grantedScopes,
        IReadOnlyCollection<DirectoryRoleSnapshot> directoryRoles,
        IReadOnlyCollection<string>? administrativeUnitScopeIds = null,
        IReadOnlyDictionary<string, bool>? tenantPolicyFlags = null,
        IReadOnlyDictionary<string, bool>? scopeAvailability = null,
        IReadOnlyDictionary<string, string>? scopeProblems = null) =>
        new(
            IsAvailable: true,
            UserObjectId: userObjectId,
            GrantedScopes: grantedScopes,
            DirectoryRoles: directoryRoles,
            AdministrativeUnitScopeIds: administrativeUnitScopeIds ?? [],
            TenantPolicyFlags: tenantPolicyFlags ?? new Dictionary<string, bool>(),
            ScopeAvailability: scopeAvailability,
            ScopeProblems: scopeProblems);

    public static GraphAuthorizationSnapshot Unavailable(string problemCategory, bool consentRequired = false) =>
        new(
            IsAvailable: false,
            UserObjectId: null,
            GrantedScopes: [],
            DirectoryRoles: [],
            AdministrativeUnitScopeIds: [],
            TenantPolicyFlags: new Dictionary<string, bool>(),
            ProblemCategory: problemCategory,
            ConsentRequired: consentRequired || string.Equals(problemCategory, CapabilityState.ConsentRequired, StringComparison.OrdinalIgnoreCase));
}

public sealed record DirectoryRoleSnapshot(
    string RoleTemplateId,
    string? DisplayName,
    string AssignmentState,
    string? DirectoryScopeId,
    PimStateSnapshot? Pim = null);

public sealed record PimStateSnapshot(string State, string? ActivationUrl = null);
