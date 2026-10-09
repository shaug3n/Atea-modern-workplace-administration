using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Identity;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IAuthenticationMethodCommands
{
    Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken);
    Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, TemporaryAccessPassRequest request, CancellationToken cancellationToken) =>
        request.LifetimeInMinutes == 60 && request.IsUsableOnce
            ? CreateTemporaryAccessPassAsync(userObjectId, idempotencyKey, cancellationToken)
            : Task.FromResult(new GraphTemporaryAccessPassResult(null, null, null, null, null, new GraphOperationResult(false, "invalid_request")));
}

public sealed record GraphTemporaryAccessPassResult(
    string? TemporaryAccessPass,
    string? Id,
    DateTimeOffset? StartDateTime,
    int? LifetimeInMinutes,
    bool? IsUsableOnce,
    GraphOperationResult? Error = null,
    string? CorrelationId = null,
    string? RequestId = null);

public sealed class GraphAuthenticationMethodCommands(IDelegatedGraphClientFactory clientFactory) : IAuthenticationMethodCommands
{
    public Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, CancellationToken cancellationToken) =>
        CreateTemporaryAccessPassAsync(userObjectId, idempotencyKey, new TemporaryAccessPassRequest(), cancellationToken);

    public async Task<GraphTemporaryAccessPassResult> CreateTemporaryAccessPassAsync(string userObjectId, string idempotencyKey, TemporaryAccessPassRequest request, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userObjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        ArgumentNullException.ThrowIfNull(request);
        if (request.LifetimeInMinutes is < 10 or > 1440)
        {
            return new(null, null, null, null, null, new GraphOperationResult(false, "invalid_request"));
        }

        GraphClientLease lease;
        try
        {
            lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthenticationMethodWriteScopes, cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return new(null, null, null, null, null, GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (Microsoft.Identity.Client.MsalUiRequiredException exception)
        {
            return new(null, null, null, null, null, GraphTokenAcquisitionErrorMapper.Map(exception));
        }

        await using (lease)
        {
            using var body = new StringContent(
                JsonSerializer.Serialize(new { lifetimeInMinutes = request.LifetimeInMinutes, isUsableOnce = request.IsUsableOnce }),
                System.Text.Encoding.UTF8,
                "application/json");
            var response = await lease.Transport.SendAsync(new GraphRequest(
                HttpMethod.Post,
                $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/authentication/temporaryAccessPassMethods",
                body,
                new Dictionary<string, string> { ["Idempotency-Key"] = idempotencyKey }), cancellationToken);
            if (!response.Result.IsSuccess)
            {
                var error = response.Result with { Category = TenantPolicyCategory(response.Content) ?? response.Result.Category };
                return new(null, null, null, null, null, error, error.CorrelationId, error.RequestId);
            }

            try
            {
                using var document = JsonDocument.Parse(response.Content);
                var root = document.RootElement;
                var temporaryAccessPass = Optional(root, "temporaryAccessPass");
                var id = Optional(root, "id");
                var lifetimeInMinutes = Integer(root, "lifetimeInMinutes");
                var isUsableOnce = Boolean(root, "isUsableOnce");
                if (string.IsNullOrWhiteSpace(temporaryAccessPass) || string.IsNullOrWhiteSpace(id)
                    || lifetimeInMinutes != request.LifetimeInMinutes || isUsableOnce != request.IsUsableOnce)
                {
                    return InvalidResponse(response.Result);
                }

                return new(temporaryAccessPass, id, Date(root, "startDateTime"), lifetimeInMinutes, isUsableOnce, null, response.Result.CorrelationId, response.Result.RequestId);
            }
            catch (JsonException)
            {
                return new(null, null, null, null, null, new GraphOperationResult(false, "invalid_response", response.Result.StatusCode, CorrelationId: response.Result.CorrelationId, RequestId: response.Result.RequestId));
            }
        }
    }

    public Task<GraphOperationResult> RemoveAsync(string userObjectId, string methodObjectId, string methodType, string idempotencyKey, CancellationToken cancellationToken)
    {
        var collection = CollectionFor(methodType);
        if (collection is null)
        {
            return Task.FromResult(new GraphOperationResult(false, "invalid_request"));
        }

        return GraphMutationExecutor.ExecuteAsync(
            clientFactory,
            new DeleteAuthenticationMethodMutation(userObjectId, methodObjectId, collection),
            idempotencyKey,
            cancellationToken);
    }

    private static string? CollectionFor(string type) => type switch
    {
        "microsoftAuthenticatorAuthenticationMethod" => "microsoftAuthenticatorMethods",
        "phoneAuthenticationMethod" => "phoneMethods",
        "fido2AuthenticationMethod" => "fido2Methods",
        "emailAuthenticationMethod" => "emailMethods",
        "softwareOathAuthenticationMethod" => "softwareOathMethods",
        "temporaryAccessPassAuthenticationMethod" => "temporaryAccessPassMethods",
        "windowsHelloForBusinessAuthenticationMethod" => "windowsHelloForBusinessMethods",
        "platformCredentialAuthenticationMethod" => "platformCredentialMethods",
        _ => null
    };

    private static string? Optional(JsonElement root, string name) => root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    private static DateTimeOffset? Date(JsonElement root, string name) => root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(property.GetString(), out var value) ? value : null;
    private static int? Integer(JsonElement root, string name) => root.TryGetProperty(name, out var property) && property.TryGetInt32(out var value) ? value : null;
    private static bool? Boolean(JsonElement root, string name) => root.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False ? property.GetBoolean() : null;
    private static GraphTemporaryAccessPassResult InvalidResponse(GraphOperationResult result) => new(null, null, null, null, null, new GraphOperationResult(false, "invalid_response", result.StatusCode, CorrelationId: result.CorrelationId, RequestId: result.RequestId), result.CorrelationId, result.RequestId);

    private static string? TenantPolicyCategory(string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            if (!document.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var code = Optional(error, "code");
            var message = Optional(error, "message");
            return string.Equals(code, "RoleAssignmentRequestPolicyValidationFailed", StringComparison.OrdinalIgnoreCase)
                || (message?.Contains("temporary access pass", StringComparison.OrdinalIgnoreCase) == true
                    && message.Contains("policy", StringComparison.OrdinalIgnoreCase))
                ? "tenant_policy_rejected"
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

internal sealed record DeleteAuthenticationMethodMutation(string UserObjectId, string MethodObjectId, string Collection) : JsonGraphMutation(GraphScopeCatalog.AuthenticationMethodWriteScopes)
{
    internal override HttpMethod Method => HttpMethod.Delete;
    internal override string PathAndQuery => $"/v1.0/users/{Uri.EscapeDataString(UserObjectId)}/authentication/{Collection}/{Uri.EscapeDataString(MethodObjectId)}";
    internal override object? Body => null;
}
