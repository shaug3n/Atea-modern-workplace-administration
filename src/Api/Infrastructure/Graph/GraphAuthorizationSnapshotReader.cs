using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Pim;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class GraphAuthorizationSnapshotReader(IDelegatedGraphClientFactory clientFactory) : IGraphAuthorizationSnapshotReader
{
    public async Task<GraphAuthorizationSnapshot> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken = default)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthorizationReadScopes, cancellationToken);

        var me = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, "/v1.0/me?$select=id,userPrincipalName,displayName"), cancellationToken);
        if (!me.Result.IsSuccess)
        {
            return Unavailable(me.Result);
        }

        var userObjectId = ReadId(me.Content);
        var roleDefinitions = await ReadRoleDefinitionsAsync(lease.Transport, cancellationToken);
        if (roleDefinitions.Error is not null)
        {
            return Unavailable(roleDefinitions.Error);
        }

        var assignments = await ReadRoleAssignmentsAsync(lease.Transport, userObjectId, roleDefinitions.Value, cancellationToken);
        if (assignments.Error is not null)
        {
            return Unavailable(assignments.Error);
        }

        return GraphAuthorizationSnapshot.Available(
            userObjectId,
            lease.Scopes,
            assignments.Value,
            assignments.Value
                .Select(role => role.DirectoryScopeId)
                .Where(scope => !string.IsNullOrWhiteSpace(scope) && scope.StartsWith("/administrativeUnits/", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToArray(),
            new Dictionary<string, bool>());
    }

    private static async Task<GraphReadResult<IReadOnlyDictionary<string, RoleDefinition>>> ReadRoleDefinitionsAsync(IGraphTransport transport, CancellationToken cancellationToken)
    {
        var response = await transport.SendAsync(new GraphRequest(HttpMethod.Get, "/v1.0/roleManagement/directory/roleDefinitions?$select=id,templateId,displayName"), cancellationToken);
        if (!response.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyDictionary<string, RoleDefinition>>.Failed(response.Result);
        }

        using var document = JsonDocument.Parse(response.Content);
        var definitions = new Dictionary<string, RoleDefinition>(StringComparer.OrdinalIgnoreCase);
        foreach (var element in Values(document))
        {
            var id = OptionalString(element, "id");
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            definitions[id] = new RoleDefinition(OptionalString(element, "templateId") ?? id, OptionalString(element, "displayName"));
        }

        return GraphReadResult<IReadOnlyDictionary<string, RoleDefinition>>.Succeeded(definitions);
    }

    private static async Task<GraphReadResult<IReadOnlyCollection<DirectoryRoleSnapshot>>> ReadRoleAssignmentsAsync(
        IGraphTransport transport,
        string userObjectId,
        IReadOnlyDictionary<string, RoleDefinition> roleDefinitions,
        CancellationToken cancellationToken)
    {
        var roles = new List<DirectoryRoleSnapshot>();
        var assignments = await transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/roleManagement/directory/roleAssignments?$filter=principalId eq '{Uri.EscapeDataString(userObjectId)}'&$select=id,principalId,roleDefinitionId,directoryScopeId"),
            cancellationToken);
        if (!assignments.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyCollection<DirectoryRoleSnapshot>>.Failed(assignments.Result);
        }

        using (var document = JsonDocument.Parse(assignments.Content))
        {
            foreach (var element in Values(document))
            {
                roles.Add(MapRole(element, DirectoryRoleAssignmentState.Active, roleDefinitions, null));
            }
        }

        var eligibilities = await transport.SendAsync(
            new GraphRequest(HttpMethod.Get, $"/v1.0/roleManagement/directory/roleEligibilityScheduleInstances?$filter=principalId eq '{Uri.EscapeDataString(userObjectId)}'&$select=id,principalId,roleDefinitionId,directoryScopeId,status,endDateTime"),
            cancellationToken);
        if (!eligibilities.Result.IsSuccess)
        {
            return GraphReadResult<IReadOnlyCollection<DirectoryRoleSnapshot>>.Failed(eligibilities.Result);
        }

        using (var document = JsonDocument.Parse(eligibilities.Content))
        {
            foreach (var element in Values(document))
            {
                var status = OptionalString(element, "status") ?? "Eligible";
                roles.Add(MapRole(
                    element,
                    DirectoryRoleAssignmentState.Eligible,
                    roleDefinitions,
                    new PimStateSnapshot(PimStateMapper.ToPimRequirement(status), "/api/capabilities")));
            }
        }

        return GraphReadResult<IReadOnlyCollection<DirectoryRoleSnapshot>>.Succeeded(roles);
    }

    private static DirectoryRoleSnapshot MapRole(JsonElement element, string assignmentState, IReadOnlyDictionary<string, RoleDefinition> roleDefinitions, PimStateSnapshot? pim)
    {
        var roleDefinitionId = OptionalString(element, "roleDefinitionId") ?? string.Empty;
        var definition = roleDefinitions.TryGetValue(roleDefinitionId, out var resolved) ? resolved : new RoleDefinition(roleDefinitionId, null);
        return new DirectoryRoleSnapshot(definition.TemplateId, definition.DisplayName, assignmentState, OptionalString(element, "directoryScopeId"), pim);
    }

    private static GraphAuthorizationSnapshot Unavailable(GraphOperationResult result) =>
        GraphAuthorizationSnapshot.Unavailable(result.Category, string.Equals(result.Category, "consent_required", StringComparison.OrdinalIgnoreCase));

    private static string ReadId(string content)
    {
        using var document = JsonDocument.Parse(content);
        return OptionalString(document.RootElement, "id") ?? string.Empty;
    }

    private static IEnumerable<JsonElement> Values(JsonDocument document) =>
        document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array ? value.EnumerateArray() : [];

    private static string? OptionalString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;

    private sealed record RoleDefinition(string TemplateId, string? DisplayName);

    private sealed record GraphReadResult<T>(T Value, GraphOperationResult? Error)
    {
        public static GraphReadResult<T> Succeeded(T value) => new(value, null);
        public static GraphReadResult<T> Failed(GraphOperationResult error) => new(default!, error);
    }
}
