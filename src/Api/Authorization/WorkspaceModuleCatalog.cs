using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;

namespace Atea.UnifiedWorkplace.Api.Authorization;

public static class WorkspaceModuleCatalog
{
    private static readonly string[] KnownModules = ["users", "devices", "licenses", "exchange", "authentication-campaigns", "license-hygiene", "about", "feedback"];

    public static IReadOnlyList<string> ReservedModules => [];
    public static IReadOnlyList<string> All => KnownModules;
    public static IReadOnlyList<string> Core => ["users", "devices", "licenses"];

    public static bool IsKnown(string? key) =>
        key is not null && KnownModules.Contains(key.Trim(), StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Normalize(IEnumerable<string>? keys)
    {
        var requested = (keys ?? []).Where(IsKnown).Select(key => key.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return KnownModules.Where(requested.Contains).ToArray();
    }

    public static bool IsOwner(string? role) =>
        string.Equals(role, "workspace_owner", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "owner", StringComparison.OrdinalIgnoreCase);

    public static bool IsCustomerAdministrator(string? role) =>
        IsOwner(role)
        || string.Equals(role, "customer_admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "customeradmin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase)
        || string.Equals(role, "workspace-manager", StringComparison.OrdinalIgnoreCase);

    public static IReadOnlyList<string> EffectiveModules(string? role, IEnumerable<string>? enabled, IEnumerable<string>? granted)
    {
        var enabledKeys = Normalize(enabled);
        if (IsOwner(role)) return enabledKeys;
        var grantedKeys = Normalize(granted).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return enabledKeys.Where(grantedKeys.Contains).ToArray();
    }
}

public static class WorkspaceModuleEndpointExtensions
{
    public static RouteHandlerBuilder RequireWorkspaceModule(this RouteHandlerBuilder builder, string moduleKey) =>
        builder.AddEndpointFilter(new WorkspaceModuleEndpointFilter(moduleKey));
}

public sealed class WorkspaceModuleEndpointFilter(string moduleKey) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext invocationContext, EndpointFilterDelegate next)
    {
        var services = invocationContext.HttpContext.RequestServices;
        var workspace = services.GetRequiredService<IWorkspaceContextAccessor>().Current;
        if (workspace is null)
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);

        var settings = await services.GetRequiredService<IWorkspaceSettingsService>()
            .GetConfigurationAsync(workspace, invocationContext.HttpContext.RequestAborted);
        var access = WorkspaceModuleCatalog.EffectiveModules(
            workspace.Membership.PlatformRole, settings.EnabledModules, workspace.Membership.ModuleKeys);
        if (!access.Contains(moduleKey, StringComparer.OrdinalIgnoreCase))
            return Results.Json(new { error = "workspace_module_unavailable", module = moduleKey }, statusCode: StatusCodes.Status403Forbidden);

        return await next(invocationContext);
    }
}
