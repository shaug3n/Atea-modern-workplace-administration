using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddPlatformAuthorization(builder.Configuration);
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
app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }
