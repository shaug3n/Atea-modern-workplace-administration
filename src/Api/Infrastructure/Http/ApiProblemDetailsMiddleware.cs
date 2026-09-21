namespace Atea.UnifiedWorkplace.Api.Infrastructure.Http;

public sealed class ApiProblemDetailsMiddleware(RequestDelegate next, ILogger<ApiProblemDetailsMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        try
        {
            await next(httpContext);
        }
        catch (KeyNotFoundException exception) when (!httpContext.Response.HasStarted)
        {
            logger.LogInformation(exception, "Mapped not-found exception to ProblemDetails.");
            await ApiProblemDetails.WriteAsync(httpContext, ApiProblemCode.NotFound);
        }
        catch (BadHttpRequestException exception) when (!httpContext.Response.HasStarted)
        {
            logger.LogInformation(exception, "Mapped validation exception to ProblemDetails.");
            await ApiProblemDetails.WriteAsync(httpContext, ApiProblemCode.ValidationFailed);
        }
        catch (HttpRequestException exception) when (!httpContext.Response.HasStarted)
        {
            logger.LogWarning(exception, "Mapped upstream request exception to ProblemDetails.");
            await ApiProblemDetails.WriteAsync(httpContext, ApiProblemCode.TransientGraphFailure);
        }
    }
}
