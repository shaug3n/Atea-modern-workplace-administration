using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Atea.UnifiedWorkplace.Api.Features.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Features.Groups;
using Atea.UnifiedWorkplace.Api.Features.Licenses;
using Atea.UnifiedWorkplace.Api.Features.Pim;
using Atea.UnifiedWorkplace.Api.Features.Roles;
using Atea.UnifiedWorkplace.Api.Features.UserPreferences;
using Atea.UnifiedWorkplace.Api.Features.Users;
using Atea.UnifiedWorkplace.Api.Infrastructure.Observability;
using Atea.UnifiedWorkplace.Api.Infrastructure.Security;
using Atea.UnifiedWorkplace.Api.Features.Audit;
using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Features.AdminAuth;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Atea.UnifiedWorkplace.Api.Features.Exchange;
using Atea.UnifiedWorkplace.Api.Features.Exports;
using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
using Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddHostedService<HostedConfigurationValidationService>();
builder.Services.AddOptions<OnboardingOptions>()
    .Bind(builder.Configuration.GetSection("Onboarding"))
    .Validate(options =>
    {
        options.Validate(builder.Environment);
        return true;
    }, "Onboarding configuration is invalid.")
    .ValidateOnStart();
builder.Services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("WorkplaceDb")));
builder.Services.AddPlatformAuthorization(builder.Configuration, builder.Environment);
builder.Services.AddScoped<IWorkspaceProvisioningRepository, WorkspaceProvisioningRepository>();
builder.Services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
builder.Services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
builder.Services.AddScoped<IWorkspaceProvisioningService, WorkspaceProvisioningService>();
builder.Services.AddScoped<IWorkspaceSettingsService, WorkspaceSettingsService>();
builder.Services.AddSingleton<WorkspaceSettingsMemoryCache>();
builder.Services.AddScoped<WorkspaceOnboardingRepository>();
builder.Services.AddScoped<IWorkspaceAccessRepository, WorkspaceAccessRepository>();
builder.Services.AddScoped<IOnboardingRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IInvitationRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IConsentChallengeRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<ConsentChallengeService>(services => new ConsentChallengeService(
    services.GetRequiredService<IOptions<OnboardingOptions>>().Value.ConsentSigningKey));
builder.Services.AddScoped<InvitationService>(services =>
{
    var options = services.GetRequiredService<IOptions<OnboardingOptions>>().Value;
    return new InvitationService(services.GetRequiredService<IInvitationRepository>(), new Uri(options.PublicBaseUrl, UriKind.Absolute));
});
builder.Services.AddScoped<IDelegatedConnectionProbe, DelegatedGraphConnectionProbe>();
builder.Services.AddScoped<IConnectionHealthReader, ConnectionHealthReader>();
builder.Services.AddScoped<IGraphAuthorizationSnapshotReader, GraphAuthorizationSnapshotReader>();
builder.Services.AddSingleton<OverviewDataCache>();
builder.Services.AddScoped<IOverviewDataReader, GraphOverviewDataReader>();
builder.Services.AddScoped<IOverviewService, OverviewService>();
builder.Services.AddHttpClient("MicrosoftGraph", client => client.BaseAddress = new Uri("https://graph.microsoft.com"));
builder.Services.AddScoped<IGraphTokenProvider, MicrosoftIdentityGraphTokenProvider>();
builder.Services.AddScoped<IDelegatedGraphClientFactory, DelegatedGraphClientFactory>();
builder.Services.AddScoped<IUserDirectoryReader, GraphDirectoryReader>();
builder.Services.AddScoped<IUserQueryService, UserQueryService>();
builder.Services.AddScoped<IUserDetailService, UserDetailService>();
builder.Services.AddScoped<IUserCommandService, UserCommandService>();
builder.Services.AddScoped<IIdempotencyService, IdempotencyService>();
builder.Services.AddSingleton<ICorrelationContextAccessor, CorrelationContextAccessor>();
builder.Services.AddScoped<IAuditWriter, AuditWriter>();
builder.Services.AddSharedDataProtection(builder.Configuration, builder.Environment);
builder.Services.AddSingleton<AuditContinuationTokenProtector>();
builder.Services.AddSingleton(_ => new UserContinuationTokenProtector(
    UserContinuationConfiguration.ResolveSigningKey(builder.Configuration, builder.Environment.IsDevelopment())));
builder.Services.AddSingleton<DeviceContinuationTokenProtector>();
builder.Services.AddScoped<GraphUserLifecycle>();
builder.Services.AddScoped<IUserLifecycleCommands>(services => services.GetRequiredService<GraphUserLifecycle>());
builder.Services.AddScoped<GraphGroupMembershipService>();
builder.Services.AddScoped<IGroupMembershipReader>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<IGroupCatalogReader>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<IGroupMembershipCommands>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<GraphLicenseService>();
builder.Services.AddScoped<IUserLicenseReader>(services => services.GetRequiredService<GraphLicenseService>());
builder.Services.AddScoped<ILicenseAssignmentCommands>(services => services.GetRequiredService<GraphLicenseService>());
builder.Services.AddScoped<ILicenseOverviewReader, GraphLicenseOverviewReader>();
builder.Services.AddScoped<ILicenseOverviewService, LicenseOverviewService>();
builder.Services.AddScoped<ILicenseAssigneeService, LicenseAssigneeService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddScoped<GraphManagedDeviceReader>();
builder.Services.AddScoped<IManagedDeviceReader>(services => services.GetRequiredService<GraphManagedDeviceReader>());
builder.Services.AddScoped<IManagedDeviceDetailReader>(services => services.GetRequiredService<GraphManagedDeviceReader>());
builder.Services.AddScoped<IGraphDeviceRecoveryReader, GraphDeviceRecoveryReader>();
builder.Services.AddScoped<IDeviceRecoveryService, DeviceRecoveryService>();
builder.Services.AddScoped<IManagedDeviceCommands, GraphManagedDeviceCommands>();
builder.Services.AddScoped<IDeviceService, DeviceService>();
builder.Services.AddScoped<IDeviceCommandService, DeviceCommandService>();
builder.Services.AddScoped<UserAssociatedDeviceService>();
builder.Services.AddScoped<IAuthenticationMethodReader, GraphAuthenticationMethodReader>();
builder.Services.AddScoped<IAuthenticationMethodCommands, GraphAuthenticationMethodCommands>();
builder.Services.AddScoped<IAuthenticationMethodService, AuthenticationMethodService>();
builder.Services.AddScoped<IUserSessionCommands, GraphUserSessionCommands>();
builder.Services.AddScoped<IExchangeService, ExchangeService>();
builder.Services.AddScoped<IUserSessionCommandService, UserSessionCommandService>();
builder.Services.AddScoped<GraphRoleAndPimService>();
builder.Services.AddScoped<IRoleAndPimReader>(services => services.GetRequiredService<GraphRoleAndPimService>());
builder.Services.AddScoped<IPimActivationCommands>(services => services.GetRequiredService<GraphRoleAndPimService>());
builder.Services.AddScoped<IPimService, PimService>();
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    options.Events.OnChallenge = async context =>
    {
        context.HandleResponse();
        await ApiProblemDetails.WriteAsync(context.HttpContext, ApiProblemCode.AuthenticationRequired);
    };
    options.Events.OnForbidden = async context =>
    {
        await ApiProblemDetails.WriteAsync(context.HttpContext, ApiProblemCode.AuthorizationDenied);
    };
});

var app = builder.Build();
if (args.Length == 1 && string.Equals(args[0], "--migrate", StringComparison.Ordinal))
{
    Environment.ExitCode = await DatabaseMigrationRunner.RunAsync(app.Services, CancellationToken.None);
}
else
{
if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("WorkplaceDb")))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>().Database.MigrateAsync();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Json(new { status = "ok" })).AllowAnonymous();
app.MapGet("/health/ready", (WorkplaceDbContext database, CancellationToken cancellationToken) =>
    HealthEndpoints.CheckDatabaseReadinessAsync(database.Database.CanConnectAsync, cancellationToken)).AllowAnonymous();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ApiProblemDetailsMiddleware>();
app.UseAuthentication();
app.UseWorkspaceContext();
app.UseAuthorization();
app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));
app.MapAdminAuthEndpoints();
app.MapGet("/api/session", async (IWorkspaceContextAccessor accessor, IWorkspaceSettingsService settingsService, CancellationToken cancellationToken) =>
{
    var context = accessor.Current!;
    var configuration = await settingsService.GetConfigurationAsync(context, cancellationToken);
    return Results.Ok(new
    {
        user = new
        {
            tenantId = context.User.TenantId,
            objectId = context.User.ObjectId,
            userPrincipalName = context.User.UserPrincipalName,
            displayName = context.User.DisplayName,
            userType = context.User.UserType,
            homeTenantId = context.User.HomeTenantId
        },
        workspace = new
        {
            id = context.Membership.WorkspaceId,
            name = context.Membership.WorkspaceName,
            enabledModules = configuration.EnabledModules,
            defaultColumns = configuration.DefaultColumns,
            defaultFilters = configuration.DefaultFilters,
            supportInstructions = configuration.SupportInstructions,
            defaultTheme = configuration.DefaultTheme,
            moduleAccess = WorkspaceModuleCatalog.EffectiveModules(context.Membership.PlatformRole, configuration.EnabledModules, context.Membership.ModuleKeys)
        },
        workspaceAccess = new
        {
            role = context.Membership.PlatformRole,
            isOwner = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole),
            canManageMembers = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceMembersManage, context.Membership).State == CapabilityState.Allowed,
            canManageSettings = CapabilityEvaluator.EvaluatePlatformCapability(Capability.WorkspaceSettingsManage, context.Membership).State == CapabilityState.Allowed,
            canManageModules = WorkspaceModuleCatalog.IsOwner(context.Membership.PlatformRole),
            canManageMemberModules = WorkspaceModuleCatalog.IsCustomerAdministrator(context.Membership.PlatformRole)
        }
    });
}).RequireAuthorization();
app.MapWorkspaceEndpoints();
app.MapWorkspaceAccessEndpoints();
app.MapWorkspaceSettingsEndpoints();
app.MapCapabilityEndpoints();
app.MapUserPreferenceEndpoints();
app.MapUserEndpoints();
app.MapUserDetailEndpoints();
app.MapUserCommandEndpoints();
app.MapLicenseEndpoints();
app.MapCsvExportEndpoints();
app.MapGroupEndpoints();
app.MapRoleEndpoints();
app.MapPimEndpoints();
app.MapAuditEndpoints();
app.MapOverviewEndpoints();
app.MapDeviceEndpoints();
app.MapUserAssociatedDeviceEndpoints();
app.MapAuthenticationMethodEndpoints();
app.MapUserSessionCommandEndpoints();
app.MapExchangeEndpoints();
app.MapFallback("/api/{**path}", () => Results.NotFound())
    .WithMetadata(new HttpMethodMetadata(["GET", "HEAD"]));
app.MapFallbackToFile("index.html").AllowAnonymous();

await app.RunAsync();
}

public partial class Program { }
