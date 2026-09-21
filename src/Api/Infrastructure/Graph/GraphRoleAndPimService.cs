using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Pim;
using Atea.UnifiedWorkplace.Api.Features.Users;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IRoleAndPimReader
{
    Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken);
    Task<GraphReadResult<IReadOnlyList<PimEligibility>>> ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken);
}

public interface IPimActivationCommands
{
    Task<PimActivationGraphResult> ActivateDirectoryRoleAsync(PimActivationGraphRequest request, string idempotencyKey, CancellationToken cancellationToken);
}

public sealed record PimActivationGraphRequest(
    string PrincipalId,
    string RoleDefinitionId,
    string RoleTemplateId,
    string DirectoryScopeId,
    int DurationMinutes,
    string? Justification);

public sealed record PimActivationGraphResult(
    bool IsSuccess,
    string Category,
    string? RequestId = null,
    string? Status = null,
    int? StatusCode = null,
    string? GraphCorrelationId = null,
    string? GraphRequestId = null)
{
    public static PimActivationGraphResult Succeeded(string? requestId, string? status, string? graphCorrelationId = null, string? graphRequestId = null) =>
        new(true, "success", requestId, status, GraphCorrelationId: graphCorrelationId, GraphRequestId: graphRequestId);

    public static PimActivationGraphResult Failed(string category, int? statusCode = null, string? graphCorrelationId = null, string? graphRequestId = null) =>
        new(false, category, StatusCode: statusCode, GraphCorrelationId: graphCorrelationId, GraphRequestId: graphRequestId);
}

public sealed class GraphRoleAndPimService(IDelegatedGraphClientFactory clientFactory) : IRoleAndPimReader, IPimActivationCommands, IGraphMutationExecutor
{
    async Task<GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>> IRoleAndPimReader.ReadUserRoleAssignmentsAsync(string userObjectId, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthorizationReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/roleManagement/directory/roleAssignments?$filter={PrincipalFilter(userObjectId)}&$expand=roleDefinition($select=id,templateId,displayName)&$select=id,roleDefinitionId,directoryScopeId"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Failed(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var assignments = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(MapRoleAssignment).ToArray()
            : [];
        return GraphReadResult<IReadOnlyList<DirectoryRoleAssignment>>.Succeeded(assignments);
    }

    async Task<GraphReadResult<IReadOnlyList<PimEligibility>>> IRoleAndPimReader.ReadUserPimEligibilityAsync(string userObjectId, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthorizationReadScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/roleManagement/directory/roleEligibilityScheduleInstances?$filter={PrincipalFilter(userObjectId)}&$expand=roleDefinition($select=id,templateId,displayName)&$select=id,roleDefinitionId,directoryScopeId,status,memberType,endDateTime"),
            cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyList<PimEligibility>>.Failed(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var eligibilities = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().Select(MapPimEligibility).ToArray()
            : [];
        return GraphReadResult<IReadOnlyList<PimEligibility>>.Succeeded(eligibilities);
    }

    public Task<GraphOperationResult> AssignDirectoryRoleAsync(
        string roleDefinitionId,
        string principalId,
        string directoryScopeId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        ExecuteAsync(new AssignDirectoryRoleMutation(roleDefinitionId, principalId, directoryScopeId), idempotencyKey, cancellationToken);

    public async Task<PimActivationGraphResult> ActivateDirectoryRoleAsync(
        PimActivationGraphRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.RoleAndPimScopes, cancellationToken);
        var response = await lease.Transport.SendAsync(new ActivateDirectoryRoleMutation(request).CreateRequest(idempotencyKey), cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return PimActivationGraphResult.Failed(
                ActivationFailureCategory(response.Result.Category, response.Content),
                response.Result.StatusCode,
                response.Result.CorrelationId,
                response.Result.RequestId);
        }

        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(response.Content) ? "{}" : response.Content);
        return PimActivationGraphResult.Succeeded(
            OptionalString(document.RootElement, "id"),
            OptionalString(document.RootElement, "status") ?? OptionalString(document.RootElement, "requestStatus"),
            response.Result.CorrelationId,
            response.Result.RequestId);
    }

    Task<GraphOperationResult> IGraphMutationExecutor.ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        ExecuteAsync(mutation, idempotencyKey, cancellationToken);

    private Task<GraphOperationResult> ExecuteAsync(GraphMutation mutation, string idempotencyKey, CancellationToken cancellationToken) =>
        GraphMutationExecutor.ExecuteAsync(clientFactory, mutation, idempotencyKey, cancellationToken);

    private static DirectoryRoleAssignment MapRoleAssignment(JsonElement element)
    {
        var roleDefinition = element.TryGetProperty("roleDefinition", out var role) && role.ValueKind == JsonValueKind.Object ? role : default;
        var templateId = OptionalString(roleDefinition, "templateId") ?? OptionalString(element, "roleTemplateId") ?? OptionalString(element, "roleDefinitionId") ?? string.Empty;
        return new DirectoryRoleAssignment(
            RequiredString(element, "id"),
            templateId,
            OptionalString(roleDefinition, "displayName") ?? DisplayNameFor(templateId),
            DirectoryRoleAssignmentState.Active,
            OptionalString(element, "directoryScopeId"));
    }

    private static PimEligibility MapPimEligibility(JsonElement element)
    {
        var roleDefinition = element.TryGetProperty("roleDefinition", out var role) && role.ValueKind == JsonValueKind.Object ? role : default;
        var roleDefinitionId = OptionalString(element, "roleDefinitionId") ?? OptionalString(roleDefinition, "id");
        var templateId = OptionalString(roleDefinition, "templateId") ?? OptionalString(element, "roleTemplateId") ?? roleDefinitionId ?? string.Empty;
        var requirement = PimStateMapper.ToPimRequirement(OptionalString(element, "status") ?? "Eligible");
        var status = requirement switch
        {
            PimRequirement.ApprovalRequired => "approval_required",
            PimRequirement.MfaRequired => "mfa_required",
            PimRequirement.EligibilityExpired => PimRequirement.EligibilityExpired,
            PimRequirement.ActivationRequired => "eligible_inactive",
            _ => "temporarily_unavailable"
        };
        var activationAvailable = status is "eligible_inactive" or "approval_required" or "mfa_required";

        return new PimEligibility(
            RequiredString(element, "id"),
            templateId,
            OptionalString(roleDefinition, "displayName") ?? DisplayNameFor(templateId),
            status,
            Capability.PimActivate,
            activationAvailable,
            status == "approval_required" || OptionalBool(element, "requiresApproval") == true,
            status == "mfa_required" || OptionalBool(element, "requiresMfa") == true,
            OptionalBool(element, "requiresJustification") ?? true,
            OptionalInt(element, "maximumDurationMinutes"),
            activationAvailable
                ? new PimActivationAction("request_activation", "/api/pim/activations", "POST", Capability.PimActivate, true)
                : null)
        {
            RoleDefinitionId = roleDefinitionId,
            DirectoryScopeId = OptionalString(element, "directoryScopeId"),
            ExpiresAt = OptionalDateTimeOffset(element, "endDateTime")
        };
    }

    private static string DisplayNameFor(string roleTemplateId) =>
        EntraRoleCatalog.DisplayNames.TryGetValue(roleTemplateId, out var displayName) ? displayName : roleTemplateId;

    private static string RequiredString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) ? value.GetString() ?? string.Empty : string.Empty;

    private static string? OptionalString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private static bool? OptionalBool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : null;

    private static int? OptionalInt(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.TryGetInt32(out var number) ? number : null;

    private static DateTimeOffset? OptionalDateTimeOffset(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
        && DateTimeOffset.TryParse(value.GetString(), out var date)
            ? date
            : null;

    private static string PrincipalFilter(string userObjectId) =>
        Uri.EscapeDataString($"principalId eq '{EscapeODataString(userObjectId)}'");

    private static string EscapeODataString(string value) => value.Trim().Replace("'", "''", StringComparison.Ordinal);

    private static string ActivationFailureCategory(string fallback, string content)
    {
        if (content.Contains("RoleAssignmentRequestPolicyValidationFailed", StringComparison.OrdinalIgnoreCase)
            || content.Contains("policy", StringComparison.OrdinalIgnoreCase)
            || content.Contains("approval", StringComparison.OrdinalIgnoreCase))
        {
            return "policy_blocked";
        }

        if (content.Contains("Mfa", StringComparison.OrdinalIgnoreCase)
            || content.Contains("interaction_required", StringComparison.OrdinalIgnoreCase))
        {
            return "mfa_required";
        }

        return fallback;
    }
}

internal sealed record AssignDirectoryRoleMutation(
    string RoleDefinitionId,
    string PrincipalId,
    string DirectoryScopeId) : JsonGraphMutation(GraphScopeCatalog.RoleAndPimScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => "/v1.0/roleManagement/directory/roleAssignments";
    internal override object Body => new
    {
        roleDefinitionId = RoleDefinitionId,
        principalId = PrincipalId,
        directoryScopeId = DirectoryScopeId
    };
}

internal sealed record ActivateDirectoryRoleMutation(PimActivationGraphRequest Request) : JsonGraphMutation(GraphScopeCatalog.RoleAndPimScopes)
{
    internal override HttpMethod Method => HttpMethod.Post;
    internal override string PathAndQuery => "/v1.0/roleManagement/directory/roleAssignmentScheduleRequests";
    internal override object Body => new
    {
        action = "selfActivate",
        principalId = Request.PrincipalId,
        roleDefinitionId = Request.RoleDefinitionId,
        directoryScopeId = Request.DirectoryScopeId,
        justification = Request.Justification,
        scheduleInfo = new
        {
            startDateTime = DateTimeOffset.UtcNow,
            expiration = new
            {
                type = "afterDuration",
                duration = $"PT{Request.DurationMinutes}M"
            }
        }
    };
}
