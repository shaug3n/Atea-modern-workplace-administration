using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

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

        var reader = context.HttpContext.RequestServices.GetRequiredService<IGraphAuthorizationSnapshotReader>();
        var snapshot = await reader.ReadAsync(workspaceContext, context.HttpContext.RequestAborted);
        var capabilities = CapabilityEvaluator.Evaluate(snapshot, workspaceContext.Membership);
        var decision = capabilities[capability];
        if (decision.State == CapabilityState.Allowed)
        {
            return await next(context);
        }

        return Results.Json(new
        {
            error = "capability_required",
            decision.Capability,
            decision.State,
            decision.ReasonCode,
            requiredRole = decision.RequiredRoleTemplateId,
            nextStep = decision.NextStep
        }, statusCode: StatusCodes.Status403Forbidden);
    }
}
