using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Atea.UnifiedWorkplace.Api.Features.Feedback;

public static class FeedbackEndpoints
{
    private const string SubmissionsPath = "/api/feedback/submissions";

    public static IEndpointRouteBuilder MapFeedbackEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(SubmissionsPath, async (
                FeedbackSubmissionRequest? request,
                HttpContext httpContext,
                IWorkspaceContextAccessor workspaceContextAccessor,
                IFeedbackService feedbackService,
                CancellationToken cancellationToken) =>
            {
                var context = workspaceContextAccessor.Current;
                if (context is null)
                    return ApiProblemDetails.Result(ApiProblemCode.AuthorizationDenied, httpContext);

                if (request is null)
                {
                    return ValidationError(httpContext, new Dictionary<string, string>
                    {
                        ["request"] = "required"
                    });
                }
                var errors = FeedbackValidation.Validate(request);
                if (errors.Count > 0)
                    return ValidationError(httpContext, errors);

                var idempotencyKey = httpContext.Request.Headers["Idempotency-Key"].FirstOrDefault();
                if (string.IsNullOrWhiteSpace(idempotencyKey) || idempotencyKey.Length > 200)
                {
                    return ValidationError(httpContext, new Dictionary<string, string>
                    {
                        ["Idempotency-Key"] = "required_or_too_long"
                    });
                }

                var result = await feedbackService.CreateAsync(context, request, idempotencyKey, cancellationToken);
                return result.Status switch
                {
                    FeedbackCreateStatus.Created => Results.Created(SubmissionsPath, result.Receipt),
                    FeedbackCreateStatus.Replayed => Results.Ok(result.Receipt),
                    FeedbackCreateStatus.KeyReused => Results.Conflict(new { error = "idempotency_key_reused" }),
                    FeedbackCreateStatus.KeyExpired => Results.Conflict(new { error = "idempotency_key_expired" }),
                    _ => throw new InvalidOperationException("Unknown feedback create status.")
                };
            })
            .RequireAuthorization()
            .RequireWorkspaceModule("feedback")
            .RequireCapability(Capability.FeedbackSubmit);

        endpoints.MapGet(SubmissionsPath, async (
                string? cursor,
                HttpContext httpContext,
                IWorkspaceContextAccessor workspaceContextAccessor,
                IFeedbackService feedbackService,
                CancellationToken cancellationToken) =>
            {
                var context = workspaceContextAccessor.Current;
                if (context is null)
                    return ApiProblemDetails.Result(ApiProblemCode.AuthorizationDenied, httpContext);
                if (cursor is not null && !FeedbackCursor.TryDecode(cursor, out _, out _))
                {
                    return ValidationError(httpContext, new Dictionary<string, string>
                    {
                        ["cursor"] = "invalid"
                    });
                }

                var page = await feedbackService.GetMineAsync(context, cursor, cancellationToken);
                return Results.Ok(page);
            })
            .RequireAuthorization()
            .RequireWorkspaceModule("feedback")
            .RequireCapability(Capability.FeedbackSubmit);
        return endpoints;
    }

    private static IResult ValidationError(HttpContext httpContext, IReadOnlyDictionary<string, string> errors) =>
        ApiProblemDetails.Result(
            ApiProblemCode.ValidationFailed,
            httpContext,
            extensions: new Dictionary<string, object?> { ["errors"] = errors });
}
