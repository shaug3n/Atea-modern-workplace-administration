using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
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
            return ApiProblemDetails.Result(
                ApiProblemCode.AuthorizationDenied,
                context.HttpContext,
                "The signed-in user is not assigned to this workspace.");
        }

        var decision = CapabilityEvaluator.IsPlatformOnly(capability)
            ? CapabilityEvaluator.EvaluatePlatformCapability(capability, workspaceContext.Membership)
            : await EvaluateGraphCapabilityAsync(context.HttpContext, workspaceContext);

        if (decision.State == CapabilityState.Allowed)
        {
            return await next(context);
        }

        return ApiProblemDetails.Result(
            ApiProblemCode.CapabilityRequired,
            context.HttpContext,
            extensions: new Dictionary<string, object?>
            {
                ["error"] = ApiProblemCode.CapabilityRequired,
                ["capability"] = decision.Capability,
                ["state"] = decision.State,
                ["reasonCode"] = decision.ReasonCode,
                ["requiredRole"] = decision.RequiredRoleTemplateId,
                ["nextStep"] = decision.NextStep
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
