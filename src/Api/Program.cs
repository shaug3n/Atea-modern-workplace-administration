using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Atea.UnifiedWorkplace.Api.Features.Workspaces;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<WorkplaceDbContext>(options => options.UseNpgsql(builder.Configuration.GetConnectionString("WorkplaceDb")));
builder.Services.AddPlatformAuthorization(builder.Configuration);
builder.Services.AddScoped<IWorkspaceProvisioningRepository, WorkspaceProvisioningRepository>();
builder.Services.AddScoped<IWorkspaceProvisioningService, WorkspaceProvisioningService>();
builder.Services.AddScoped<WorkspaceOnboardingRepository>();
builder.Services.AddScoped<IOnboardingRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IInvitationRepository>(services => services.GetRequiredService<WorkspaceOnboardingRepository>());
builder.Services.AddScoped<IOnboardingService, OnboardingService>();
builder.Services.AddSingleton<ConsentChallengeService>(_ => new ConsentChallengeService(builder.Configuration["Onboarding:ConsentSigningKey"]));
builder.Services.AddScoped<InvitationService>(services => new InvitationService(
    services.GetRequiredService<IInvitationRepository>(), new Uri(builder.Configuration["Onboarding:PublicBaseUrl"] ?? "https://workplace.example")));
builder.Services.AddScoped<IDelegatedConnectionProbe, UnconfiguredDelegatedConnectionProbe>();
builder.Services.AddScoped<IConnectionHealthReader, ConnectionHealthReader>();
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
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
