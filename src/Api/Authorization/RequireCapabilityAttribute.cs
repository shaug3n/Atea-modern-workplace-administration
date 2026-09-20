using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Microsoft.AspNetCore.Mvc;

namespace Atea.UnifiedWorkplace.Api.Authorization;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Delegate)]
public sealed class RequireCapabilityAttribute(string capability) : Attribute
{
    public string Capability { get; } = capability;
}

public static class CapabilityEndpointConventionBuilderExtensions
{
    public static RouteHandlerBuilder RequireCapability(this RouteHandlerBuilder builder, string capability)
    {
        builder.Add(endpointBuilder =>
        {
            endpointBuilder.Metadata.Add(new RequireCapabilityAttribute(capability));
        });
        builder.AddEndpointFilter(new CapabilityEndpointFilter(capability));
        return builder;
    }
}

internal sealed class CapabilityEndpointFilter(string capability) : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var accessor = context.HttpContext.RequestServices.GetRequiredService<IWorkspaceContextAccessor>();
        var workspaceContext = accessor.Current;
        if (workspaceContext is null)
        {
            return Results.Json(new { error = "workspace_membership_required" }, statusCode: StatusCodes.Status403Forbidden);
        }

        var decision = CapabilityEvaluator.IsPlatformOnly(capability)
            ? CapabilityEvaluator.EvaluatePlatformCapability(capability, workspaceContext.Membership)
            : await EvaluateGraphCapabilityAsync(context.HttpContext, workspaceContext);

        if (decision.State == CapabilityState.Allowed)
        {
            return await next(context);
        }

        return Results.Problem(new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Capability required",
            Type = "https://httpstatuses.com/403",
            Extensions =
            {
                ["error"] = "capability_required",
                ["capability"] = decision.Capability,
                ["state"] = decision.State,
                ["reasonCode"] = decision.ReasonCode,
                ["requiredRole"] = decision.RequiredRoleTemplateId,
                ["nextStep"] = decision.NextStep
            }
        });
    }

    private async Task<CapabilityDecision> EvaluateGraphCapabilityAsync(HttpContext httpContext, WorkspaceContext workspaceContext)
    {
        var reader = httpContext.RequestServices.GetRequiredService<IGraphAuthorizationSnapshotReader>();
        var snapshot = await reader.ReadAsync(workspaceContext, httpContext.RequestAborted);
        var capabilities = CapabilityEvaluator.Evaluate(snapshot, workspaceContext.Membership);
        return capabilities[capability];
    }
}
