namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class DelegatedGraphConnectionProbe(IDelegatedGraphClientFactory clientFactory) : IDelegatedConnectionProbe
{
    public async Task<DelegatedConnectionProbeResult> ProbeAsync(Guid tenantId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
    {
        await using var lease = await clientFactory.CreateForCurrentUserAsync(scopes, cancellationToken);
        var response = await lease.Transport.SendAsync(
            new GraphRequest(HttpMethod.Get, "/v1.0/me?$select=id,userPrincipalName,displayName"),
            cancellationToken);

        if (response.Result.IsSuccess)
        {
            return new DelegatedConnectionProbeResult(true, lease.Scopes);
        }

        return response.Result.Category switch
        {
            "consent_required" => new DelegatedConnectionProbeResult(false, [], ConsentRequired: true, ProblemCategory: response.Result.Category),
            "not_authorized" => new DelegatedConnectionProbeResult(false, [], PermissionIncomplete: true, ProblemCategory: response.Result.Category),
            "unauthenticated" => new DelegatedConnectionProbeResult(false, [], ConsentRevoked: true, ProblemCategory: response.Result.Category),
            _ => new DelegatedConnectionProbeResult(false, [], ProblemCategory: response.Result.Category)
        };
    }
}
