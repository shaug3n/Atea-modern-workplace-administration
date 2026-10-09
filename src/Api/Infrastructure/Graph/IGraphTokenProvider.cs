using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

public interface IGraphTokenProvider
{
    Task<string> GetAccessTokenForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken);
}

public sealed class MicrosoftIdentityGraphTokenProvider(ITokenAcquisition tokenAcquisition) : IGraphTokenProvider
{
    public Task<string> GetAccessTokenForCurrentUserAsync(IReadOnlyCollection<string> scopes, CancellationToken cancellationToken)
    {
        if (scopes.Count == 0)
        {
            throw new ArgumentException("At least one Graph delegated scope is required.", nameof(scopes));
        }

        return tokenAcquisition.GetAccessTokenForUserAsync(
            scopes,
            tokenAcquisitionOptions: new TokenAcquisitionOptions { CancellationToken = cancellationToken });
    }
}
