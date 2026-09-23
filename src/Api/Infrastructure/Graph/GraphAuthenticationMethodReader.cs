using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Features.Identity;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IAuthenticationMethodReader
{
    Task<GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>> ReadAsync(string userObjectId, CancellationToken cancellationToken);
}

public sealed class GraphAuthenticationMethodReader(IDelegatedGraphClientFactory clientFactory) : IAuthenticationMethodReader
{
    public async Task<GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>> ReadAsync(string userObjectId, CancellationToken cancellationToken)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(GraphScopeCatalog.AuthenticationMethodReadScopes, cancellationToken);
        // This collection is polymorphic. Selecting displayName on the base
        // authenticationMethod type makes Graph return HTTP 400 for tenants
        // that include methods without that property. Keep the projection in
        // the safe mapper below instead of asking Graph for an invalid base
        // type projection.
        var path = $"/v1.0/users/{Uri.EscapeDataString(userObjectId)}/authentication/methods";
        var response = await lease.Transport.SendAsync(new GraphRequest(HttpMethod.Get, path), cancellationToken);
        if (!response.Result.IsSuccess) return GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Failed(response.Result);

        try
        {
            using var document = JsonDocument.Parse(response.Content);
            var items = document.RootElement.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(Map).Where(item => item is not null).Cast<AuthenticationMethodItem>().ToArray()
                : [];
            return GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Succeeded(items);
        }
        catch (JsonException)
        {
            return GraphReadResult<IReadOnlyList<AuthenticationMethodItem>>.Failed(new GraphOperationResult(false, "invalid_response"));
        }
    }

    private static AuthenticationMethodItem? Map(JsonElement element)
    {
        var id = Optional(element, "id");
        if (string.IsNullOrWhiteSpace(id)) return null;

        var rawType = Optional(element, "@odata.type") ?? "microsoft.graph.authenticationMethod";
        var type = rawType.StartsWith("#microsoft.graph.", StringComparison.OrdinalIgnoreCase) ? rawType[17..] : rawType;
        var displayName = Optional(element, "displayName") ?? DisplayNameFor(type);
        return new AuthenticationMethodItem(id, type, displayName, Date(element, "createdDateTime"), Optional(element, "model"), Optional(element, "attestationLevel"));
    }

    private static string DisplayNameFor(string type) => type switch
    {
        "passwordAuthenticationMethod" => "Password",
        "microsoftAuthenticatorAuthenticationMethod" => "Microsoft Authenticator",
        "phoneAuthenticationMethod" => "Phone",
        "fido2AuthenticationMethod" => "Passkey / FIDO2 security key",
        "emailAuthenticationMethod" => "Email",
        "temporaryAccessPassAuthenticationMethod" => "Temporary Access Pass",
        _ => "Authentication method"
    };

    private static string? Optional(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null ? value.GetString() : null;
    private static DateTimeOffset? Date(JsonElement element, string property) => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), out var result) ? result : null;
}
