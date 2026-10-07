using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public sealed class DelegatedScopeAvailabilityReader(IDelegatedGraphClientFactory clientFactory)
    : IDelegatedScopeAvailabilityReader
{
    private const int MaximumConcurrentProbes = 4;

    public async Task<IReadOnlyCollection<DelegatedScopeResult>> ReadAsync(
        IReadOnlyCollection<string> scopes,
        CancellationToken cancellationToken = default)
    {
        var requestedScopes = scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (requestedScopes.Length == 0) return [];

        using var concurrency = new SemaphoreSlim(MaximumConcurrentProbes, MaximumConcurrentProbes);
        var probes = requestedScopes.Select(scope => ProbeAsync(scope, concurrency, cancellationToken));
        return await Task.WhenAll(probes);
    }

    private async Task<DelegatedScopeResult> ProbeAsync(
        string scope,
        SemaphoreSlim concurrency,
        CancellationToken cancellationToken)
    {
        await concurrency.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await using var lease = await clientFactory.CreateForCurrentUserAsync([scope], cancellationToken);
                return new DelegatedScopeResult(scope, ScopeAvailability.Available, null);
            }
            catch (MicrosoftIdentityWebChallengeUserException exception)
            {
                return MapFailure(scope, GraphTokenAcquisitionErrorMapper.Map(exception));
            }
            catch (MsalUiRequiredException exception)
            {
                return MapFailure(scope, GraphTokenAcquisitionErrorMapper.Map(exception));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
                return new DelegatedScopeResult(scope, ScopeAvailability.Unknown, "temporarily_unavailable");
            }
        }
        finally
        {
            concurrency.Release();
        }
    }

    private static DelegatedScopeResult MapFailure(string scope, GraphOperationResult result) =>
        string.Equals(result.Category, "consent_required", StringComparison.OrdinalIgnoreCase)
            ? new DelegatedScopeResult(scope, ScopeAvailability.MissingConsent, "consent_required")
            : new DelegatedScopeResult(scope, ScopeAvailability.Unknown, result.Category);
}
