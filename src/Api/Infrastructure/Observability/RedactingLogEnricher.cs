using System.Text.Json;
using System.Text.Json.Nodes;
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

        var redacted = TryRedactJson(value) ?? value;
        redacted = AuthorizationHeaderRegex().Replace(redacted, "$1[REDACTED]");
        redacted = CookieRegex().Replace(redacted, "$1[REDACTED]");
        redacted = SecretKeyValueRegex().Replace(redacted, "$1[REDACTED]");
        redacted = JsonSecretRegex().Replace(redacted, "$1\"[REDACTED]\"");
        redacted = RequestBodyRegex().Replace(redacted, "\"requestBody\":\"[REDACTED]\"");
        return redacted;
    }

    private static string? TryRedactJson(string value)
    {
        try
        {
            var node = JsonNode.Parse(value);
            if (node is null)
            {
                return null;
            }

            RedactNode(node);
            return node.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            foreach (var property in jsonObject.ToArray())
            {
                if (IsSensitiveKey(property.Key))
                {
                    jsonObject[property.Key] = "[REDACTED]";
                }
                else if (property.Value is not null)
                {
                    RedactNode(property.Value);
                }
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray)
            {
                if (child is not null)
                {
                    RedactNode(child);
                }
            }
        }
    }

    private static bool IsSensitiveKey(string key)
    {
        var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
        return normalized.Contains("token", StringComparison.Ordinal)
            || normalized.Contains("secret", StringComparison.Ordinal)
            || normalized.Contains("password", StringComparison.Ordinal)
            || normalized.Contains("mfacode", StringComparison.Ordinal)
            || normalized is "mfa" or "authorization" or "cookie" or "requestbody";
    }

    [GeneratedRegex(@"(?i)\b(Authorization\s*:\s*Bearer\s+)[^\s,;]+")]
    private static partial Regex AuthorizationHeaderRegex();

    [GeneratedRegex(@"(?i)\b(Cookie\s*:\s*)[^\r\n]+")]
    private static partial Regex CookieRegex();

    [GeneratedRegex(@"(?i)\b((?:access[_-]?token|refresh[_-]?token|client[_-]?secret|token|secret|password|temporaryPassword|mfa(?:[_-]?code)?|authorization|cookie)\s*=\s*)[^&\s,;""}]+")]
    private static partial Regex SecretKeyValueRegex();

    [GeneratedRegex(@"(?i)(""[^""]*(?:token|secret|password|mfa|authorization|cookie)[^""]*""\s*:\s*)(""(?:\\.|[^""\\])*""|[^,}\]]+)")]
    private static partial Regex JsonSecretRegex();

    [GeneratedRegex(@"""requestBody""\s*:\s*(\{[^}]*\}|""[^""]*"")")]
    private static partial Regex RequestBodyRegex();
}
