using Microsoft.Identity.Client;
using Microsoft.Identity.Web;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

internal static class GraphTokenAcquisitionErrorMapper
{
    public static GraphOperationResult Map(MicrosoftIdentityWebChallengeUserException exception) => Map(exception.MsalUiRequiredException);

    public static GraphOperationResult Map(MsalUiRequiredException exception)
    {
        var consentRequired = string.Equals(exception.ErrorCode, "consent_required", StringComparison.OrdinalIgnoreCase)
            || exception.Message.Contains("AADSTS65001", StringComparison.OrdinalIgnoreCase);
        return new GraphOperationResult(false, consentRequired ? "consent_required" : "temporarily_unavailable");
    }
}
