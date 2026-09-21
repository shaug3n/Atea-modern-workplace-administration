using System.Collections.Concurrent;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record WorkspaceSettingsRequest(string? DisplayName = null, IReadOnlyCollection<string>? EnabledModules = null, IReadOnlyCollection<string>? DefaultColumns = null, IReadOnlyDictionary<string, string>? DefaultFilters = null, string? SupportInstructions = null, string? DefaultTheme = null);
public sealed record WorkspaceSettingsResponse(string DisplayName, IReadOnlyCollection<string> EnabledModules, IReadOnlyCollection<string> DefaultColumns, IReadOnlyDictionary<string, string> DefaultFilters, string SupportInstructions, string DefaultTheme, CapabilityDecision Access);

public interface IWorkspaceSettingsService
{
    Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken);
    Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken);
}

public sealed class WorkspaceSettingsService(WorkplaceDbContext db, IConfiguration configuration) : IWorkspaceSettingsService
{
    private static readonly ConcurrentDictionary<Guid, WorkspaceSettingsResponse> Memory = new();
    private static readonly HashSet<string> AllowedModules = new(StringComparer.OrdinalIgnoreCase) { "overview", "users", "licenses", "audit", "workspace-settings" };
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase) { "displayName", "userPrincipalName", "mail", "accountStatus", "userType" };
    private static readonly HashSet<string> AllowedFilters = new(StringComparer.OrdinalIgnoreCase) { "accountStatus", "tenantRole", "license", "userType" };

    public async Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        var access = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceSettingsManage, context.Membership);
        if (access.State != CapabilityState.Allowed) return new WorkspaceSettingsResponse(string.Empty, [], [], new Dictionary<string, string>(), string.Empty, "light", access);
        if (HasDatabase())
        {
            var settings = await db.WorkspaceSettings.AsNoTracking().Include(x => x.Workspace).SingleOrDefaultAsync(x => x.WorkspaceId == context.Membership.WorkspaceId, cancellationToken);
            if (settings is not null) return FromEntity(settings, access);
        }
        return Memory.GetOrAdd(context.Membership.WorkspaceId, _ => Defaults(context, access));
    }

    public async Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken)
    {
        var access = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceSettingsManage, context.Membership);
        if (access.State != CapabilityState.Allowed) throw new WorkspaceSettingsForbiddenException();
        var current = await GetAsync(context, cancellationToken);
        var updated = new WorkspaceSettingsResponse(
            ValidateDisplayName(request.DisplayName ?? current.DisplayName),
            ValidateList(request.EnabledModules ?? current.EnabledModules, AllowedModules, "enabledModules"),
            ValidateList(request.DefaultColumns ?? current.DefaultColumns, AllowedColumns, "defaultColumns"),
            ValidateFilters(request.DefaultFilters ?? current.DefaultFilters),
            ValidateSupportInstructions(request.SupportInstructions ?? current.SupportInstructions),
            ValidateTheme(request.DefaultTheme ?? current.DefaultTheme), access);
        if (HasDatabase())
        {
            var entity = await db.WorkspaceSettings.SingleOrDefaultAsync(x => x.WorkspaceId == context.Membership.WorkspaceId, cancellationToken);
            if (entity is null) { entity = new WorkspaceSettings { WorkspaceId = context.Membership.WorkspaceId }; db.WorkspaceSettings.Add(entity); }
            var workspace = await db.Workspaces.SingleOrDefaultAsync(x => x.Id == context.Membership.WorkspaceId, cancellationToken);
            if (workspace is not null) { workspace.DisplayName = updated.DisplayName; workspace.UpdatedAt = DateTimeOffset.UtcNow; }
            entity.DefaultTheme = updated.DefaultTheme;
            entity.EnabledModulesJson = JsonSerializer.Serialize(updated.EnabledModules);
            entity.DefaultColumnsJson = JsonSerializer.Serialize(updated.DefaultColumns);
            entity.DefaultFiltersJson = JsonSerializer.Serialize(updated.DefaultFilters);
            entity.SupportInstructions = updated.SupportInstructions;
            await db.SaveChangesAsync(cancellationToken);
        }
        Memory[context.Membership.WorkspaceId] = updated;
        return updated;
    }

    private bool HasDatabase() => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("WorkplaceDb"));
    private static WorkspaceSettingsResponse Defaults(WorkspaceContext context, CapabilityDecision access) => new(context.Membership.WorkspaceName, ["overview", "users", "licenses"], ["displayName", "userPrincipalName"], new Dictionary<string, string>(), string.Empty, "light", access);
    private static WorkspaceSettingsResponse FromEntity(WorkspaceSettings entity, CapabilityDecision access) => new(entity.Workspace.DisplayName, ParseList(entity.EnabledModulesJson), ParseList(entity.DefaultColumnsJson), ParseFilters(entity.DefaultFiltersJson), entity.SupportInstructions, entity.DefaultTheme, access);
    private static IReadOnlyCollection<string> ParseList(string value) { try { return JsonSerializer.Deserialize<string[]>(value) ?? []; } catch (JsonException) { return []; } }
    private static IReadOnlyDictionary<string, string> ParseFilters(string value) { try { return JsonSerializer.Deserialize<Dictionary<string, string>>(value) ?? new Dictionary<string, string>(); } catch (JsonException) { return new Dictionary<string, string>(); } }
    private static string ValidateDisplayName(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200 && !value.Any(char.IsControl) ? value.Trim() : throw new WorkspaceSettingsValidationException("displayName is invalid.", "displayName");
    private static IReadOnlyCollection<string> ValidateList(IReadOnlyCollection<string> values, HashSet<string> allowed, string field) { var normalized = values.Select(value => value.Trim()).ToArray(); if (normalized.Any(value => !allowed.Contains(value))) throw new WorkspaceSettingsValidationException($"{field} contains an unsupported value.", field); return normalized; }
    private static IReadOnlyDictionary<string, string> ValidateFilters(IReadOnlyDictionary<string, string> values) { if (values.Keys.Any(key => !AllowedFilters.Contains(key)) || values.Values.Any(value => value.Length > 100 || value.Any(char.IsControl))) throw new WorkspaceSettingsValidationException("defaultFilters contains an unsupported value.", "defaultFilters"); return new Dictionary<string, string>(values, StringComparer.OrdinalIgnoreCase); }
    private static string ValidateSupportInstructions(string value) => value.Length <= 2000 && !value.Any(char.IsControl) && !value.Contains("<script", StringComparison.OrdinalIgnoreCase) && !value.Contains("javascript:", StringComparison.OrdinalIgnoreCase) ? value : throw new WorkspaceSettingsValidationException("supportInstructions contains unsafe content.", "supportInstructions");
    private static string ValidateTheme(string value) => value is "light" or "dark" ? value : throw new WorkspaceSettingsValidationException("defaultTheme must be light or dark.", "defaultTheme");
}

public sealed class WorkspaceSettingsValidationException(string message, string field) : Exception(message) { public string Field { get; } = field; }
public sealed class WorkspaceSettingsForbiddenException : Exception;

public static class WorkspaceSettingsEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceSettingsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/workspaces/current/settings", GetAsync).RequireAuthorization().RequireCapability(Capability.WorkspaceSettingsManage);
        endpoints.MapPatch("/api/workspaces/current/settings", UpdateAsync).RequireAuthorization().RequireCapability(Capability.WorkspaceSettingsManage);
        return endpoints;
    }
    private static async Task<IResult> GetAsync(IWorkspaceContextAccessor accessor, IWorkspaceSettingsService service, CancellationToken cancellationToken) => accessor.Current is { } context ? Results.Ok(await service.GetAsync(context, cancellationToken)) : Results.StatusCode(StatusCodes.Status403Forbidden);
    private static async Task<IResult> UpdateAsync(WorkspaceSettingsRequest request, IWorkspaceContextAccessor accessor, IWorkspaceSettingsService service, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        try { return Results.Ok(await service.UpdateAsync(context, request, cancellationToken)); }
        catch (WorkspaceSettingsValidationException exception) { return Results.ValidationProblem(new Dictionary<string, string[]> { [exception.Field] = [exception.Message] }); }
        catch (WorkspaceSettingsForbiddenException) { return Results.StatusCode(StatusCodes.Status403Forbidden); }
    }
}
