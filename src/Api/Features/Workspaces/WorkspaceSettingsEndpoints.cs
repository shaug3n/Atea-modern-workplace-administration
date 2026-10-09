using System.Collections.Concurrent;
using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Atea.UnifiedWorkplace.Api.Features.Workspaces;

public sealed record WorkspaceSettingsRequest(string? DisplayName = null, IReadOnlyCollection<string>? EnabledModules = null, IReadOnlyCollection<string>? DefaultColumns = null, IReadOnlyDictionary<string, string>? DefaultFilters = null, string? SupportInstructions = null, string? DefaultTheme = null);
public sealed record WorkspaceSettingsResponse(string DisplayName, IReadOnlyCollection<string> EnabledModules, IReadOnlyCollection<string> DefaultColumns, IReadOnlyDictionary<string, string> DefaultFilters, string SupportInstructions, string DefaultTheme, CapabilityDecision Access);
public sealed record WorkspaceConfiguration(IReadOnlyCollection<string> EnabledModules, IReadOnlyCollection<string> DefaultColumns, IReadOnlyDictionary<string, string> DefaultFilters, string SupportInstructions, string DefaultTheme);

public sealed class WorkspaceSettingsMemoryCache
{
    private readonly ConcurrentDictionary<Guid, WorkspaceConfiguration> entries = new();

    public bool TryGet(Guid workspaceId, out WorkspaceConfiguration configuration) => entries.TryGetValue(workspaceId, out configuration!);

    public void Set(Guid workspaceId, WorkspaceConfiguration configuration) => entries[workspaceId] = configuration;
}

public interface IWorkspaceSettingsService
{
    Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken);
    Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken);
    Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken);
}

public sealed class WorkspaceSettingsService(WorkplaceDbContext db, IConfiguration configuration, WorkspaceSettingsMemoryCache memoryCache) : IWorkspaceSettingsService
{
    private static readonly HashSet<string> AllowedModules = WorkspaceModuleCatalog.All.ToHashSet(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> AllowedColumns = new(StringComparer.OrdinalIgnoreCase) { "displayName", "userPrincipalName", "mail", "accountStatus", "userType" };
    private static readonly HashSet<string> AllowedFilters = new(StringComparer.OrdinalIgnoreCase) { "accountStatus", "tenantRole", "license", "userType" };

    public async Task<WorkspaceSettingsResponse> GetAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        var access = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceSettingsManage, context.Membership);
        if (access.State != CapabilityState.Allowed) return new WorkspaceSettingsResponse(string.Empty, [], [], new Dictionary<string, string>(), string.Empty, "light", access);
        var configuration = await GetConfigurationAsync(context, cancellationToken);
        return new WorkspaceSettingsResponse(context.Membership.WorkspaceName, configuration.EnabledModules, configuration.DefaultColumns, configuration.DefaultFilters, configuration.SupportInstructions, configuration.DefaultTheme, access);
    }

    public async Task<WorkspaceConfiguration> GetConfigurationAsync(WorkspaceContext context, CancellationToken cancellationToken)
    {
        if (HasDatabase())
        {
            var settings = await db.WorkspaceSettings.AsNoTracking().SingleOrDefaultAsync(x => x.WorkspaceId == context.Membership.WorkspaceId, cancellationToken);
            if (settings is not null) return FromEntity(settings);
        }
        return memoryCache.TryGet(context.Membership.WorkspaceId, out var cached)
            ? cached
            : DefaultsConfiguration();
    }

    public async Task<WorkspaceSettingsResponse> UpdateAsync(WorkspaceContext context, WorkspaceSettingsRequest request, CancellationToken cancellationToken)
    {
        var access = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceSettingsManage, context.Membership);
        if (access.State != CapabilityState.Allowed) throw new WorkspaceSettingsForbiddenException();
        if (request.EnabledModules is not null) throw new WorkspaceSettingsValidationException("enabledModules must be changed through the audited modules endpoint.", "enabledModules");
        var current = await GetConfigurationAsync(context, cancellationToken);
        var updated = new WorkspaceSettingsResponse(
            ValidateDisplayName(request.DisplayName ?? context.Membership.WorkspaceName),
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
        var updatedConfiguration = new WorkspaceConfiguration(updated.EnabledModules, updated.DefaultColumns, updated.DefaultFilters, updated.SupportInstructions, updated.DefaultTheme);
        memoryCache.Set(context.Membership.WorkspaceId, updatedConfiguration);
        return updated;
    }

    private bool HasDatabase() => !string.IsNullOrWhiteSpace(configuration.GetConnectionString("WorkplaceDb"));
    private static WorkspaceConfiguration DefaultsConfiguration() => new(WorkspaceModuleCatalog.Core, ["displayName", "userPrincipalName"], new Dictionary<string, string>(), string.Empty, "light");
    private static WorkspaceConfiguration FromEntity(WorkspaceSettings entity) => new(ParseList(entity.EnabledModulesJson), ParseList(entity.DefaultColumnsJson), ParseFilters(entity.DefaultFiltersJson), entity.SupportInstructions, entity.DefaultTheme);
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
        endpoints.MapGet("/api/workspaces/current/modules", GetModulesAsync).RequireAuthorization();
        endpoints.MapPatch("/api/workspaces/current/modules", UpdateModulesAsync).RequireAuthorization();
        return endpoints;
    }

    private static async Task<IResult> GetModulesAsync(IWorkspaceContextAccessor accessor, IWorkspaceSettingsService service, IWorkspaceAccessRepository repository, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        var enabled = (await service.GetConfigurationAsync(context, cancellationToken)).EnabledModules;
        var owner = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole);
        var (memberships, invitations) = await repository.ListAsync(context.Membership.WorkspaceId, cancellationToken);
        var dormant = memberships.Where(member => !WorkspaceModuleCatalog.IsOwner(member.PlatformRole))
            .Sum(member => ParseModules(member.ModuleGrantsJson).Except(enabled, StringComparer.OrdinalIgnoreCase).Count())
            + invitations.Where(invite => invite.RevokedAt is null && invite.RedeemedAt is null).Sum(invite => ParseModules(invite.ModuleKeysJson).Except(enabled, StringComparer.OrdinalIgnoreCase).Count());
        return Results.Ok(new { enabledModules = enabled, canManageModules = owner, dormantGrantCount = dormant });
    }

    private static async Task<IResult> UpdateModulesAsync(WorkspaceEnabledModulesRequest request, IWorkspaceContextAccessor accessor, IWorkspaceSettingsService service, WorkplaceDbContext db, Atea.UnifiedWorkplace.Api.Infrastructure.Observability.IAuditWriter audit, CancellationToken cancellationToken)
    {
        if (accessor.Current is not { } context) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (!WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole)) return Results.StatusCode(StatusCodes.Status403Forbidden);
        if (request.EnabledModules is null || request.EnabledModules.Any(key => !WorkspaceModuleCatalog.IsKnown(key))) return Results.BadRequest(new { error = "invalid_enabled_modules" });
        var normalized = WorkspaceModuleCatalog.Normalize(request.EnabledModules);
        if (normalized.Count != request.EnabledModules.Distinct(StringComparer.OrdinalIgnoreCase).Count()) return Results.BadRequest(new { error = "invalid_enabled_modules" });

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var current = await service.GetConfigurationAsync(context, cancellationToken);
        var settings = await db.WorkspaceSettings.SingleOrDefaultAsync(x => x.WorkspaceId == context.Membership.WorkspaceId, cancellationToken);
        if (settings is null)
        {
            settings = new WorkspaceSettings { WorkspaceId = context.Membership.WorkspaceId };
            db.WorkspaceSettings.Add(settings);
        }
        settings.EnabledModulesJson = JsonSerializer.Serialize(normalized);
        await db.SaveChangesAsync(cancellationToken);
        await audit.WriteAsync(new AuditEvent
        {
            WorkspaceId = context.Membership.WorkspaceId, TenantId = context.User.TenantId,
            ActorTenantId = context.User.TenantId, ActorObjectId = context.User.ObjectId,
            Action = "workspace.modules.changed", TargetType = "workspace", TargetId = context.Membership.WorkspaceId.ToString("D"),
            Outcome = "success", Timestamp = DateTimeOffset.UtcNow,
            SafeMetadataJson = JsonSerializer.Serialize(new { enabledModules = normalized })
        }, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Results.Ok(new { enabledModules = normalized, restoredDormantGrants = normalized.Except(current.EnabledModules, StringComparer.OrdinalIgnoreCase).Any() });
    }

    private static IReadOnlyCollection<string> ParseModules(string json)
    {
        try { return WorkspaceModuleCatalog.Normalize(JsonSerializer.Deserialize<string[]>(json)); }
        catch (JsonException) { return []; }
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
