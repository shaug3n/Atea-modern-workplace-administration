using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;

namespace Atea.UnifiedWorkplace.Api.Features.Pim;

public interface IPimService
{
    Task<PimUserResult> GetUserPimAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken);
    Task<PimActivationResult> ActivateAsync(WorkspaceContext context, PimActivationRequest request, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed class PimService(
    IUserDirectoryReader directoryReader,
    IRoleAndPimReader roleReader,
    IPimActivationCommands activationCommands,
    IGraphAuthorizationSnapshotReader authorizationSnapshotReader,
    IIdempotencyService idempotency,
    Func<DateTimeOffset>? utcNow = null) : IPimService
{
    private readonly Func<DateTimeOffset> utcNow = utcNow ?? (() => DateTimeOffset.UtcNow);

    public async Task<PimUserResult> GetUserPimAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        var authorization = await AuthorizeAsync(context, cancellationToken);
        if (authorization.State == CapabilityState.Hidden)
        {
            return PimUserResult(EmptyUserResponse(
                userObjectId,
                PimStatus.NotAuthorized,
                authorization,
                Error("capability_required", "PIM details are not visible for the current capability state.", authorization.State),
                UserDirectoryFreshness.Unavailable,
                PimStatus.NotAuthorized));
        }

        var user = await ReadUserAsync(context, userObjectId, cancellationToken);
        if (user.Error is not null)
        {
            return PimUserResult(VerificationFailureResponse(userObjectId, authorization, user.Error));
        }

        if (user.Value is null)
        {
            return new PimUserResult(
                PimUserOutcome.NotFound,
                Error: Error("user_not_found", "The user was removed or is no longer visible in the current tenant."));
        }

        if (user.Value.DirectoryTenantId is { } directoryTenantId && directoryTenantId != context.User.TenantId)
        {
            return new PimUserResult(
                PimUserOutcome.NotFound,
                Error: Error("user_not_found", "The user was removed or is no longer visible in the current tenant."));
        }

        var roles = await roleReader.ReadUserRoleAssignmentsAsync(userObjectId, cancellationToken);
        if (roles.Error is not null)
        {
            return PimUserResult(UnavailableUserResponse(userObjectId, authorization, roles.Error));
        }

        var eligibility = await roleReader.ReadUserPimEligibilityAsync(userObjectId, cancellationToken);
        if (eligibility.Error is not null)
        {
            return PimUserResult(UnavailableUserResponse(userObjectId, authorization, eligibility.Error));
        }

        var active = roles.Value
            .Where(role => string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Active, StringComparison.OrdinalIgnoreCase))
            .Select(role => new PimRoleStatus(
                role.RoleTemplateId,
                null,
                role.DisplayName,
                PimStatus.Active,
                ActivationAvailable: false))
            .ToArray();

        var eligible = eligibility.Value.Select(MapEligibility).ToArray();
        var combined = active.Concat(eligible).ToArray();
        var status = combined.Length == 0 ? PimStatus.NotEligible : AggregateStatus(combined.Select(role => role.Status));
        return PimUserResult(new PimUserResponse(userObjectId, status, combined, combined.FirstOrDefault(role => role.Handoff is not null)?.Handoff)
        {
            Access = Access(authorization),
            Items = eligibility.Value
        });
    }

    public async Task<PimActivationResult> ActivateAsync(WorkspaceContext context, PimActivationRequest request, string idempotencyKey, CancellationToken cancellationToken)
    {
        if (!request.Confirmed)
        {
            return Blocked(request, "confirmation_required", "Confirm the PIM activation request before it is sent to Microsoft Graph.");
        }

        if (!string.Equals(request.RoleType, PimRoleType.DirectoryRole, StringComparison.OrdinalIgnoreCase))
        {
            return Blocked(request, "unsupported_role_type", "Only Microsoft Entra directory-role PIM activation is supported.");
        }

        if (request.DurationMinutes is < 1 or > 1440)
        {
            return Blocked(request, "invalid_duration", "Choose an activation duration allowed by the tenant PIM policy.");
        }

        var authorization = await AuthorizeAsync(context, cancellationToken);
        if (!IsActivationCapabilityState(authorization.State))
        {
            return new PimActivationResult(
                PimStatus.NotAuthorized,
                Capability.PimActivate,
                RoleTemplateId: request.RoleTemplateId,
                Error: "capability_required",
                Authorization: authorization,
                Handoff: HandoffFor("capability_required", request.RoleTemplateId, DisplayNameFor(request.RoleTemplateId), authorization.NextStep?.Href));
        }

        var actorObjectId = context.User.ObjectId.ToString();
        var roles = await roleReader.ReadUserRoleAssignmentsAsync(actorObjectId, cancellationToken);
        if (roles.Error is not null)
        {
            return GraphFailure(request, roles.Error, DisplayNameFor(request.RoleTemplateId));
        }

        var activeRole = roles.Value.FirstOrDefault(role =>
            string.Equals(role.RoleTemplateId, request.RoleTemplateId, StringComparison.OrdinalIgnoreCase)
            && string.Equals(role.AssignmentState, DirectoryRoleAssignmentState.Active, StringComparison.OrdinalIgnoreCase)
            && IsTenantWide(role.DirectoryScopeId));
        if (activeRole is not null)
        {
            return new PimActivationResult(
                PimStatus.Active,
                Capability.PimActivate,
                RoleTemplateId: activeRole.RoleTemplateId,
                DisplayName: activeRole.DisplayName);
        }

        var eligibility = await roleReader.ReadUserPimEligibilityAsync(actorObjectId, cancellationToken);
        if (eligibility.Error is not null)
        {
            return GraphFailure(request, eligibility.Error, DisplayNameFor(request.RoleTemplateId));
        }

        var currentEligibility = eligibility.Value.FirstOrDefault(candidate =>
            string.Equals(candidate.RoleTemplateId, request.RoleTemplateId, StringComparison.OrdinalIgnoreCase)
            && IsTenantWide(candidate.DirectoryScopeId));
        if (currentEligibility is null)
        {
            return new PimActivationResult(
                PimStatus.NotEligible,
                Capability.PimActivate,
                RoleTemplateId: request.RoleTemplateId,
                Error: "not_eligible",
                Handoff: HandoffFor("not_eligible", request.RoleTemplateId, DisplayNameFor(request.RoleTemplateId)));
        }

        var currentStatus = NormalizeEligibilityStatus(currentEligibility.Status, currentEligibility.RequiresJustification);
        if (currentStatus == PimStatus.ApprovalRequired)
        {
            return RequirementResult(currentEligibility, PimStatus.ApprovalRequired, "approval_required");
        }

        if (currentStatus == PimStatus.MfaRequired)
        {
            return RequirementResult(currentEligibility, PimStatus.MfaRequired, "mfa_required");
        }

        if (currentStatus == PimStatus.PolicyBlocked)
        {
            return RequirementResult(currentEligibility, PimStatus.PolicyBlocked, "policy_blocked");
        }

        if (currentStatus != PimStatus.EligibleInactive)
        {
            return new PimActivationResult(
                PimStatus.NotEligible,
                Capability.PimActivate,
                RoleTemplateId: request.RoleTemplateId,
                RoleDefinitionId: currentEligibility.RoleDefinitionId,
                DisplayName: currentEligibility.DisplayName,
                Error: "not_eligible",
                Handoff: HandoffFor("not_eligible", request.RoleTemplateId, currentEligibility.DisplayName));
        }

        if (currentEligibility.RequiresJustification && string.IsNullOrWhiteSpace(request.Justification))
        {
            return new PimActivationResult(
                PimStatus.PolicyBlocked,
                Capability.PimActivate,
                RoleTemplateId: request.RoleTemplateId,
                RoleDefinitionId: currentEligibility.RoleDefinitionId,
                DisplayName: currentEligibility.DisplayName,
                Error: "justification_required",
                Handoff: HandoffFor("justification_required", request.RoleTemplateId, currentEligibility.DisplayName));
        }

        return await ExecuteActivationAsync(context, actorObjectId, currentEligibility, request, idempotencyKey, cancellationToken);
    }

    private async Task<PimActivationResult> ExecuteActivationAsync(
        WorkspaceContext context,
        string actorObjectId,
        PimEligibility eligibility,
        PimActivationRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            request.RoleTemplateId,
            eligibility.RoleDefinitionId,
            request.DurationMinutes,
            request.Justification,
            request.Confirmed,
            request.RoleType
        };
        var outcome = await idempotency.ExecuteAsync(
            new IdempotencyScope(context.Membership.WorkspaceId, context.User.ObjectId, "pim.activate", request.RoleTemplateId, idempotencyKey),
            payload,
            async () =>
            {
                var graph = await activationCommands.ActivateDirectoryRoleAsync(
                    new PimActivationGraphRequest(
                        actorObjectId,
                        eligibility.RoleDefinitionId ?? eligibility.RoleTemplateId,
                        eligibility.RoleTemplateId,
                        "/",
                        request.DurationMinutes,
                        request.Justification),
                    idempotencyKey,
                    cancellationToken);
                var result = MapActivationGraphResult(graph, eligibility);
                return new IdempotentOperationResult(
                    StatusCodeFor(result),
                    result.Status,
                    JsonSerializer.Serialize(result, JsonOptions),
                    result.GraphCorrelationId,
                    result.GraphRequestId);
            },
            cancellationToken);

        if (outcome.Kind == IdempotencyOutcomeKind.KeyReused)
        {
            return new PimActivationResult(PimStatus.PolicyBlocked, Capability.PimActivate, Error: "idempotency_key_reused");
        }

        if (outcome.Kind == IdempotencyOutcomeKind.InProgress)
        {
            return new PimActivationResult(PimStatus.TemporarilyUnavailable, Capability.PimActivate, Error: "idempotency_in_progress");
        }

        var stored = JsonSerializer.Deserialize<PimActivationResult>(outcome.Result.SafeResultJson, JsonOptions)
            ?? new PimActivationResult(PimStatus.TemporarilyUnavailable, Capability.PimActivate, Error: "idempotency_result_unavailable");
        return outcome.Kind == IdempotencyOutcomeKind.Replayed ? stored with { Replayed = true } : stored;
    }

    private static PimActivationResult MapActivationGraphResult(PimActivationGraphResult graph, PimEligibility eligibility)
    {
        if (graph.IsSuccess)
        {
            var status = graph.Status switch
            {
                "Provisioned" or "Granted" or "Active" => PimStatus.Active,
                "PendingApproval" or "PendingScheduleCreation" or "PendingProvisioning" => PimStatus.ActivationPending,
                _ => PimStatus.TemporarilyUnavailable
            };
            return new PimActivationResult(
                status,
                Capability.PimActivate,
                RoleTemplateId: eligibility.RoleTemplateId,
                RoleDefinitionId: eligibility.RoleDefinitionId,
                DisplayName: eligibility.DisplayName,
                RequestId: graph.RequestId,
                Error: status == PimStatus.TemporarilyUnavailable ? "graph_status_unrecognized" : null,
                Handoff: status == PimStatus.ActivationPending ? HandoffFor(PimStatus.ActivationPending, eligibility.RoleTemplateId, eligibility.DisplayName, graphCorrelationId: graph.GraphCorrelationId, graphRequestId: graph.GraphRequestId) : null,
                GraphCorrelationId: graph.GraphCorrelationId,
                GraphRequestId: graph.GraphRequestId);
        }

        var safeStatus = StatusForGraphCategory(graph.Category);
        return new PimActivationResult(
            safeStatus,
            Capability.PimActivate,
            RoleTemplateId: eligibility.RoleTemplateId,
            RoleDefinitionId: eligibility.RoleDefinitionId,
            DisplayName: eligibility.DisplayName,
            Error: graph.Category,
            Handoff: HandoffFor(graph.Category, eligibility.RoleTemplateId, eligibility.DisplayName, graphCorrelationId: graph.GraphCorrelationId, graphRequestId: graph.GraphRequestId),
            GraphCorrelationId: graph.GraphCorrelationId,
            GraphRequestId: graph.GraphRequestId);
    }

    private async Task<CapabilityDecision> AuthorizeAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        var snapshot = await authorizationSnapshotReader.ReadAsync(context, cancellationToken);
        return CapabilityEvaluator.Evaluate(snapshot, context.Membership)[Capability.PimActivate];
    }

    private async Task<GraphReadResult<UserDetails?>> ReadUserAsync(WorkspaceContext context, string userObjectId, CancellationToken cancellationToken)
    {
        try
        {
            return GraphReadResult<UserDetails?>.Succeeded(await directoryReader.GetAsync(context, userObjectId, cancellationToken));
        }
        catch (GraphAdapterException exception) when (exception.Result.Category == "not_found")
        {
            return GraphReadResult<UserDetails?>.Succeeded(null);
        }
        catch (GraphAdapterException exception)
        {
            return GraphReadResult<UserDetails?>.Failed(exception.Result);
        }
    }

    private static PimRoleStatus MapEligibility(PimEligibility eligibility)
    {
        var status = NormalizeEligibilityStatus(eligibility.Status, eligibility.RequiresJustification);
        var handoffCategory = string.Equals(eligibility.Status, "justification_required", StringComparison.OrdinalIgnoreCase)
            ? "justification_required"
            : status;
        return new PimRoleStatus(
            eligibility.RoleTemplateId,
            eligibility.RoleDefinitionId,
            eligibility.DisplayName,
            status,
            eligibility.ExpiresAt,
            ActivationAvailable: eligibility.ActivationAvailable && status == PimStatus.EligibleInactive,
            Handoff: status == PimStatus.Active ? null : HandoffFor(handoffCategory, eligibility.RoleTemplateId, eligibility.DisplayName));
    }

    private static PimActivationResult RequirementResult(PimEligibility eligibility, string status, string error) =>
        new(
            status,
            Capability.PimActivate,
            RoleTemplateId: eligibility.RoleTemplateId,
            RoleDefinitionId: eligibility.RoleDefinitionId,
            DisplayName: eligibility.DisplayName,
            Error: error,
            Handoff: HandoffFor(error, eligibility.RoleTemplateId, eligibility.DisplayName));

    private static PimActivationResult Blocked(PimActivationRequest request, string error, string nextStep) =>
        new(
            PimStatus.PolicyBlocked,
            Capability.PimActivate,
            RoleTemplateId: request.RoleTemplateId,
            Error: error,
            Handoff: new PimGuidedHandoff(nextStep, request.RoleTemplateId, DisplayNameFor(request.RoleTemplateId), PortalUrlFor(request.RoleTemplateId)));

    private static PimActivationResult GraphFailure(PimActivationRequest request, GraphOperationResult graph, string? displayName) =>
        new(
            StatusForGraphCategory(graph.Category),
            Capability.PimActivate,
            RoleTemplateId: request.RoleTemplateId,
            DisplayName: displayName,
            Error: graph.Category,
            Handoff: HandoffFor(graph.Category, request.RoleTemplateId, displayName, graphCorrelationId: graph.CorrelationId, graphRequestId: graph.RequestId),
            GraphCorrelationId: graph.CorrelationId,
            GraphRequestId: graph.RequestId);

    private PimUserResponse VerificationFailureResponse(string userObjectId, CapabilityDecision authorization, GraphOperationResult graph) =>
        EmptyUserResponse(
            userObjectId,
            PimStatus.TemporarilyUnavailable,
            authorization,
            Error(graph.Category, VerificationMessageFor(graph.Category), authorization.State, graph.StatusCode, graph.RetryAfter),
            FreshnessFor(graph.Category),
            graph.Category,
            graph.CorrelationId,
            graph.RequestId);

    private PimUserResponse UnavailableUserResponse(string userObjectId, CapabilityDecision authorization, GraphOperationResult graph) =>
        EmptyUserResponse(
            userObjectId,
            StatusForGraphCategory(graph.Category),
            authorization,
            Error(graph.Category, MessageFor(graph.Category), authorization.State, graph.StatusCode, graph.RetryAfter),
            FreshnessFor(graph.Category),
            graph.Category,
            graph.CorrelationId,
            graph.RequestId);

    private PimUserResponse EmptyUserResponse(
        string userObjectId,
        string status,
        CapabilityDecision authorization,
        UserDirectoryError? error,
        string freshness,
        string handoffCategory,
        string? graphCorrelationId = null,
        string? graphRequestId = null) =>
        new(
            userObjectId,
            status,
            [],
            HandoffFor(handoffCategory, string.Empty, null, graphCorrelationId: graphCorrelationId, graphRequestId: graphRequestId),
            error?.Category,
            graphCorrelationId,
            graphRequestId)
        {
            Access = Access(authorization, freshness, true, error),
            Items = []
        };

    private SectionAccessState Access(
        CapabilityDecision authorization,
        string freshness = UserDirectoryFreshness.Fresh,
        bool partialData = false,
        UserDirectoryError? error = null) =>
        new(authorization, utcNow(), freshness, partialData, error);

    private static UserDirectoryError Error(
        string category,
        string message,
        string? state = null,
        int? statusCode = null,
        TimeSpan? retryAfter = null) =>
        new(category, message, state, statusCode, retryAfter is null ? null : (int)Math.Ceiling(retryAfter.Value.TotalSeconds));

    private static PimUserResult PimUserResult(PimUserResponse response) =>
        new(PimUserOutcome.Found, response);

    private static string FreshnessFor(string category) => category switch
    {
        "throttled" => UserDirectoryFreshness.Stale,
        _ => UserDirectoryFreshness.Unavailable
    };

    private static string MessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to read PIM for this user.",
        "consent_required" => "Delegated Microsoft Graph consent is required to read PIM for this user.",
        "throttled" => "Microsoft Graph throttled this PIM request.",
        _ => "PIM details are temporarily unavailable."
    };

    private static string VerificationMessageFor(string category) => category switch
    {
        "not_authorized" => "The signed-in user is not authorized to verify this user.",
        "consent_required" => "Delegated Microsoft Graph consent is required to verify this user.",
        "throttled" => "Microsoft Graph throttled user verification.",
        _ => "User verification is temporarily unavailable."
    };

    private static string NormalizeEligibilityStatus(string status, bool requiresJustification) => status switch
    {
        PimStatus.Active => PimStatus.Active,
        PimStatus.EligibleInactive => PimStatus.EligibleInactive,
        PimStatus.NotEligible => PimStatus.NotEligible,
        PimStatus.ActivationPending => PimStatus.ActivationPending,
        PimStatus.ApprovalRequired => PimStatus.ApprovalRequired,
        PimStatus.MfaRequired => PimStatus.MfaRequired,
        PimStatus.PolicyBlocked => PimStatus.PolicyBlocked,
        "justification_required" when requiresJustification => PimStatus.EligibleInactive,
        _ => PimStatus.TemporarilyUnavailable
    };

    private static string AggregateStatus(IEnumerable<string> statuses)
    {
        var ordered = statuses.ToArray();
        if (ordered.Contains(PimStatus.Active, StringComparer.OrdinalIgnoreCase))
        {
            return PimStatus.Active;
        }

        return ordered.Contains(PimStatus.EligibleInactive, StringComparer.OrdinalIgnoreCase)
            ? PimStatus.EligibleInactive
            : ordered.FirstOrDefault() ?? PimStatus.NotEligible;
    }

    private static string StatusForGraphCategory(string category) => category switch
    {
        "not_authorized" or "consent_required" or "unauthenticated" => PimStatus.NotAuthorized,
        "mfa_required" => PimStatus.MfaRequired,
        "policy_blocked" or "authorization_pending" or "request_denied" => PimStatus.PolicyBlocked,
        _ => PimStatus.TemporarilyUnavailable
    };

    private static PimGuidedHandoff HandoffFor(
        string category,
        string roleTemplateId,
        string? roleDisplayName,
        string? portalUrl = null,
        string? graphCorrelationId = null,
        string? graphRequestId = null) =>
        new(
            NextStepFor(category),
            roleTemplateId,
            roleDisplayName,
            portalUrl ?? PortalUrlFor(roleTemplateId),
            GraphCorrelationId: graphCorrelationId,
            GraphRequestId: graphRequestId);

    private static string NextStepFor(string category) => category switch
    {
        PimStatus.EligibleInactive => "Request activation in Microsoft Entra PIM",
        PimStatus.ActivationPending => "Wait for the PIM activation request to finish, then refresh",
        PimStatus.ApprovalRequired or "approval_required" => "Wait for PIM approval",
        PimStatus.MfaRequired or "mfa_required" => "Complete MFA for PIM activation",
        PimStatus.PolicyBlocked or "policy_blocked" or "authorization_pending" or "request_denied" => "Review the tenant PIM policy",
        "justification_required" => "Enter a business justification",
        "consent_required" => "Grant delegated Microsoft Graph consent",
        "not_authorized" or "capability_required" => "Ask an administrator to grant the required role or Graph consent",
        "not_eligible" => "Ask an administrator to assign eligible directory-role PIM access",
        "confirmation_required" => "Confirm the PIM activation request before it is sent to Microsoft Graph.",
        "unsupported_role_type" => "Only Microsoft Entra directory-role PIM activation is supported.",
        _ => "Refresh PIM state and retry"
    };

    private static bool IsActivationCapabilityState(string state) =>
        state is CapabilityState.Allowed or CapabilityState.PimActivationRequired or CapabilityState.PimApprovalRequired or CapabilityState.PimMfaRequired;

    private static bool IsTenantWide(string? directoryScopeId) =>
        string.IsNullOrWhiteSpace(directoryScopeId)
        || string.Equals(directoryScopeId, "/", StringComparison.Ordinal);

    private static string DisplayNameFor(string roleTemplateId) =>
        EntraRoleCatalog.DisplayNames.TryGetValue(roleTemplateId, out var displayName) ? displayName : roleTemplateId;

    private static string PortalUrlFor(string roleTemplateId) =>
        string.IsNullOrWhiteSpace(roleTemplateId)
            ? "https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade/~/aadmigratedroles"
            : $"https://entra.microsoft.com/#view/Microsoft_Azure_PIMCommon/ActivationMenuBlade/~/aadmigratedroles/roleTemplateId/{Uri.EscapeDataString(roleTemplateId)}";

    private static int StatusCodeFor(PimActivationResult result) => result.Status switch
    {
        PimStatus.Active or PimStatus.ActivationPending => StatusCodes.Status200OK,
        PimStatus.NotEligible or PimStatus.PolicyBlocked => StatusCodes.Status409Conflict,
        PimStatus.NotAuthorized => StatusCodes.Status403Forbidden,
        _ => StatusCodes.Status503ServiceUnavailable
    };

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
