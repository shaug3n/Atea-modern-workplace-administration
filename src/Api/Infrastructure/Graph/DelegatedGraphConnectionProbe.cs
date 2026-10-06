using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class DelegatedGraphConnectionProbe(IDelegatedGraphClientFactory clientFactory) : IDelegatedConnectionProbe
{
    public async Task<DelegatedConnectionProbeResult> ProbeAsync(Guid tenantId, IReadOnlyCollection<string> scopes, CancellationToken cancellationToken = default)
    {
        GraphClientLease lease;
        try
        {
            lease = await clientFactory.CreateForCurrentUserAsync(scopes, cancellationToken);
        }
        catch (MicrosoftIdentityWebChallengeUserException exception)
        {
            return MapFailure(GraphTokenAcquisitionErrorMapper.Map(exception));
        }
        catch (MsalUiRequiredException exception)
        {
            return MapFailure(GraphTokenAcquisitionErrorMapper.Map(exception));
        }

        await using (lease)
        {
            var response = await lease.Transport.SendAsync(
                new GraphRequest(HttpMethod.Get, "/v1.0/me?$select=id,userPrincipalName,displayName"),
                cancellationToken);

            if (response.Result.IsSuccess)
            {
                return new DelegatedConnectionProbeResult(true, lease.Scopes);
            }

            return MapFailure(response.Result);
        }
    }

    private static DelegatedConnectionProbeResult MapFailure(GraphOperationResult result) => result.Category switch
    {
        "consent_required" => new DelegatedConnectionProbeResult(false, [], ConsentRequired: true, ProblemCategory: result.Category),
        "not_authorized" => new DelegatedConnectionProbeResult(false, [], PermissionIncomplete: true, ProblemCategory: result.Category),
        "unauthenticated" => new DelegatedConnectionProbeResult(false, [], ConsentRevoked: true, ProblemCategory: result.Category),
        _ => new DelegatedConnectionProbeResult(false, [], ProblemCategory: result.Category)
    };
}
