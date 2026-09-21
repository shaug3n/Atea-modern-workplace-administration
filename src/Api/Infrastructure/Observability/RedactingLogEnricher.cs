using System.Text.RegularExpressions;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Observability;

public static partial class RedactingLogEnricher
{
    public static string Redact(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var redacted = AuthorizationHeaderRegex().Replace(value, "$1[REDACTED]");
        redacted = CookieRegex().Replace(redacted, "$1[REDACTED]");
        redacted = SecretKeyValueRegex().Replace(redacted, "$1[REDACTED]");
        redacted = JsonSecretRegex().Replace(redacted, "$1\"[REDACTED]\"");
        redacted = RequestBodyRegex().Replace(redacted, "\"requestBody\":\"[REDACTED]\"");
        return redacted;
    }

    [GeneratedRegex(@"(?i)\b(Authorization\s*:\s*Bearer\s+)[^\s,;]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(?i)\b(Cookie\s*:\s*)[^\r\n]+")]
    private static partial Regex CookieRegex();

    [GeneratedRegex(@"(?i)\b((?:access_token|refresh_token|client_secret|password|temporaryPassword|mfa_code|authorization|cookie)\s*=\s*)[^&\s,;""}]+")]
    private static partial Regex SecretKeyValueRegex();

    [GeneratedRegex(@"(?i)""(?:access_token|refresh_token|client_secret|password|temporaryPassword|mfa|mfaCode|cookie|authorization)""\s*:\s*(""[^""]*""|\{[^}]*\}|\[[^]]*\])")]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(@"""requestBody""\s*:\s*(\{[^}]*\}|""[^""]*"")")]
    private static partial Regex RequestBodyRegex();
}
