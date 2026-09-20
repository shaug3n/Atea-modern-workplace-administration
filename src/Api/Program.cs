using System.Text.Json;
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

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("WorkplaceDb")));
builder.Services.AddPlatformAuthorization(builder.Configuration);
builder.Services.AddScoped<IWorkspaceProvisioningRepository, WorkspaceProvisioningRepository>();
builder.Services.AddScoped<IUserPreferenceRepository, UserPreferenceRepository>();
builder.Services.AddScoped<IThemePreferenceService, ThemePreferenceService>();
builder.Services.AddScoped<IWorkspaceProvisioningService, WorkspaceProvisioningService>();
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
builder.Services.AddHttpClient("MicrosoftGraph", client => client.BaseAddress = new Uri("https://graph.microsoft.com"));
builder.Services.AddScoped<IGraphTokenProvider, MicrosoftIdentityGraphTokenProvider>();
builder.Services.AddScoped<IDelegatedGraphClientFactory, DelegatedGraphClientFactory>();
builder.Services.AddScoped<IUserDirectoryReader, GraphDirectoryReader>();
builder.Services.AddScoped<IUserQueryService, UserQueryService>();
builder.Services.AddScoped<IUserDetailService, UserDetailService>();
builder.Services.AddSingleton(_ => new UserContinuationTokenProtector(
    UserContinuationConfiguration.ResolveSigningKey(builder.Configuration, builder.Environment.IsDevelopment())));
builder.Services.AddScoped<GraphUserLifecycle>();
builder.Services.AddScoped<GraphGroupMembershipService>();
builder.Services.AddScoped<IGroupMembershipReader>(services => services.GetRequiredService<GraphGroupMembershipService>());
builder.Services.AddScoped<GraphLicenseService>();
builder.Services.AddScoped<IUserLicenseReader>(services => services.GetRequiredService<GraphLicenseService>());
builder.Services.AddScoped<GraphRoleAndPimService>();
builder.Services.AddScoped<IRoleAndPimReader>(services => services.GetRequiredService<GraphRoleAndPimService>());
builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
{
    options.Events ??= new JwtBearerEvents();
    options.Events.OnChallenge = async context =>
    {
        context.HandleResponse();
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "authentication_required" }));
    };
    options.Events.OnForbidden = async context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(new { error = "forbidden" }));
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
app.MapCapabilityEndpoints();
app.MapUserPreferenceEndpoints();
app.MapUserEndpoints();
app.MapUserDetailEndpoints();
app.MapLicenseEndpoints();
app.MapGroupEndpoints();
app.MapRoleEndpoints();
app.MapPimEndpoints();
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
