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
using Atea.UnifiedWorkplace.Api.Infrastructure.Http;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("WorkplaceDb")));
builder.Services.AddPlatformAuthorization(builder.Configuration);
builder.Services.AddScoped<IWorkspaceProvisioningRepository, WorkspaceProvisioningRepository>();
builder.Services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
builder.Services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
builder.Services.AddScoped<IWorkspaceProvisioningService, WorkspaceProvisioningService>();
builder.Services.AddScoped<IWorkspaceSettingsService, WorkspaceSettingsService>();
builder.Services.AddScoped<WorkspaceOnboardingRepository>();
builder.Services.AddScoped<IOnboardingRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IInvitationRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<ConsentChallengeService>(_ => new ConsentChallengeService(builder.Configuration["Onboarding:ConsentSigningKey"]));
builder.Services.AddScoped<InvitationService>(services => new InvitationService(
    services.GetRequiredService<IInvitationRepository>(), new Uri(builder.Configuration["Onboarding:PublicBaseUrl"] ?? "https://workplace.example")));
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
builder.Services.AddDataProtection();
builder.Services.AddSingleton<AuditContinuationTokenProtector>();
builder.Services.AddSingleton(_ => new UserContinuationTokenProtector(
    UserContinuationConfiguration.ResolveSigningKey(builder.Configuration, builder.Environment.IsDevelopment())));
builder.Services.AddScoped<GraphUserLifecycle>();
builder.Services.AddScoped<IUserLifecycleCommands>(services => services.GetRequiredService<GraphUserLifecycle>());
builder.Services.AddScoped<GraphGroupMembershipService>();
builder.Services.AddScoped<IGroupMembershipReader>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<IGroupMembershipCommands>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<GraphLicenseService>();
builder.Services.AddScoped<IUserLicenseReader>(services => services.GetRequiredService<GraphLicenseService>());
builder.Services.AddScoped<ILicenseAssignmentCommands>(services => services.GetRequiredService<GraphLicenseService>());
builder.Services.AddScoped<ILicenseOverviewReader, GraphLicenseOverviewReader>();
builder.Services.AddScoped<ILicenseOverviewService, LicenseOverviewService>();
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
if (app.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("WorkplaceDb")))
{
    await using var scope = app.Services.CreateAsyncScope();
    await scope.ServiceProvider.GetRequiredService<WorkplaceDbContext>().Database.MigrateAsync();
}
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Json(new { status = "ok" })).AllowAnonymous();
app.UseMiddleware<CorrelationMiddleware>();
app.UseMiddleware<ApiProblemDetailsMiddleware>();
app.UsePlatformAuthorization();
app.MapGet("/api/ping", () => Results.Ok(new { status = "ok" }));
app.MapGet("/api/session", (IWorkspaceContextAccessor accessor) =>
{
    var context = accessor.Current!;
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
        workspace = new { id = context.Membership.WorkspaceId, name = context.Membership.WorkspaceName }
    });
}).RequireAuthorization();
app.MapWorkspaceEndpoints();
app.MapWorkspaceSettingsEndpoints();
app.MapCapabilityEndpoints();
app.MapUserPreferenceEndpoints();
app.MapUserEndpoints();
app.MapUserDetailEndpoints();
app.MapUserCommandEndpoints();
app.MapLicenseEndpoints();
app.MapGroupEndpoints();
app.MapRoleEndpoints();
app.MapPimEndpoints();
app.MapAuditEndpoints();
app.MapOverviewEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
