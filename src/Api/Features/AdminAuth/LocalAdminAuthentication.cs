using System.Security.Claims;
using Microsoft.Extensions.Hosting;

namespace Atea.UnifiedWorkplace.Api.Features.AdminAuth;

public static class LocalAdminAuthentication
{
    public const string Scheme = "LocalAteaAdmin";
    public const string LocalAdminClaim = "atea_local_admin";
    public const string AllowAllWorkspacesClaim = "atea_allow_all_workspaces";

    public static ClaimsPrincipal? Authenticate(LocalAdminOptions options, string username, string password)
    {
        if (!string.Equals(username, options.Username, StringComparison.Ordinal) ||
            !string.Equals(password, options.Password, StringComparison.Ordinal)) return null;
        return CreatePrincipal(options);
    }

    public static ClaimsPrincipal CreatePrincipal(LocalAdminOptions options)
    {
        var claims = new List<Claim>
        {
            new("oid", options.ObjectId),
            new(ClaimTypes.Name, options.DisplayName),
            new("name", options.DisplayName),
            new(ClaimTypes.Role, "PlatformAdmin"),
            new(LocalAdminClaim, "true")
        };
        if (options.AllowAllWorkspaces) claims.Add(new Claim(AllowAllWorkspacesClaim, "true"));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role));
    }

    public static bool IsConfigured(IHostEnvironment environment, LocalAdminOptions options) =>
        environment.IsDevelopment() && options.Enabled &&
        !string.IsNullOrWhiteSpace(options.Username) &&
        !string.IsNullOrWhiteSpace(options.Password) &&
        Guid.TryParse(options.ObjectId, out var objectId) && objectId != Guid.Empty;

    public static bool IsAllWorkspacesAllowed(IHostEnvironment environment, LocalAdminOptions options) =>
        environment.IsDevelopment() && options.AllowAllWorkspaces && IsConfigured(environment, options);
}
