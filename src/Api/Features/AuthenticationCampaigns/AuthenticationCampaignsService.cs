using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;

namespace Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;

public sealed class AuthenticationCampaignsService(
    IAuthenticationCampaignsRegistrationReportReader reportReader,
    IAuthenticationCampaignsDirectoryReader directoryReader) : IAuthenticationCampaignsService
{
    private static readonly HashSet<string> PhonePreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "sms",
        "voiceMobile",
        "voiceAlternateMobile",
        "voiceOffice"
    };

    private static readonly HashSet<string> NonPhonePreferences = new(StringComparer.OrdinalIgnoreCase)
    {
        "push",
        "oath",
        "none"
    };

    private static readonly HashSet<string> KnownRegistrationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        "passKeyDeviceBound",
        "fido2",
        "mobilePhone",
        "alternateMobilePhone",
        "officePhone",
        "email",
        "microsoftAuthenticator",
        "microsoftAuthenticatorPasswordless",
        "windowsHelloForBusiness",
        "softwareOneTimePasscode",
        "temporaryAccessPass"
    };

    public async Task<AuthenticationCampaignsServiceResult> ReadAsync(
        WorkspaceContext context,
        CancellationToken cancellationToken)
    {
        var report = await reportReader.ReadAsync(context, cancellationToken);
        if (report.Error is not null && !report.PartialData)
        {
            return new AuthenticationCampaignsServiceResult(null, report.Error);
        }

        var normalized = Deduplicate(report.Records);
        AuthenticationCampaignsDirectoryReadResult directory;
        try
        {
            directory = normalized.Count == 0
                ? new AuthenticationCampaignsDirectoryReadResult(
                    new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(StringComparer.OrdinalIgnoreCase),
                    null)
                : await directoryReader.ReadAsync(context, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            directory = new AuthenticationCampaignsDirectoryReadResult(
                new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(StringComparer.OrdinalIgnoreCase),
                new GraphOperationResult(false, "temporarily_unavailable"));
        }

        var items = normalized
            .Select(record => ToRegistration(record, directory.Users))
            .ToArray();
        var knownSourceTimestamps = normalized
            .Select(record => record.LastUpdatedDateTime)
            .Where(timestamp => timestamp.HasValue)
            .Select(timestamp => timestamp!.Value)
            .ToArray();
        var directoryState = directory.Error is null
            ? "complete"
            : directory.Users.Count > 0
                ? "partial"
                : "unavailable";
        var partialData = report.PartialData || directory.PartialData || directory.Error is not null;

        return new AuthenticationCampaignsServiceResult(
            new AuthenticationCampaignsResponse(
                items,
                DateTimeOffset.UtcNow,
                knownSourceTimestamps.Length == 0 ? null : knownSourceTimestamps.Min(),
                knownSourceTimestamps.Length == 0 ? null : knownSourceTimestamps.Max(),
                partialData,
                report.Records.Count,
                report.Records.Count - normalized.Count,
                directoryState,
                items.Count(item => item.DirectoryJoinState == "matched"),
                report.Error?.Category,
                directory.Error?.Category),
            null);
    }

    private static List<AuthenticationCampaignsReportRecord> Deduplicate(IReadOnlyList<AuthenticationCampaignsReportRecord> records)
    {
        var byId = new Dictionary<string, AuthenticationCampaignsReportRecord>(StringComparer.OrdinalIgnoreCase);
        var withoutId = new List<AuthenticationCampaignsReportRecord>();

        foreach (var record in records)
        {
            if (string.IsNullOrWhiteSpace(record.Id))
            {
                withoutId.Add(record);
                continue;
            }

            if (!byId.TryGetValue(record.Id, out var current)
                || (record.LastUpdatedDateTime.HasValue
                    && (!current.LastUpdatedDateTime.HasValue || record.LastUpdatedDateTime > current.LastUpdatedDateTime)))
            {
                byId[record.Id] = record;
            }
        }

        return byId.Values.Concat(withoutId).ToList();
    }

    private static AuthenticationCampaignsRegistration ToRegistration(
        AuthenticationCampaignsReportRecord record,
        IReadOnlyDictionary<string, AuthenticationCampaignsDirectoryEntry> directory)
    {
        directory.TryGetValue(record.Id, out var directoryEntry);
        var methods = record.MethodsRegistered;
        var hasPasskey = methods?.Any(method => method.Equals("passKeyDeviceBound", StringComparison.OrdinalIgnoreCase)) == true;
        var hasGenericFido = methods?.Any(method => method.Equals("fido2", StringComparison.OrdinalIgnoreCase)) == true;
        var hasPhone = methods?.Any(method => method.Equals("mobilePhone", StringComparison.OrdinalIgnoreCase)) == true;
        var hasUnknownMethod = methods?.Any(method => !KnownRegistrationMethods.Contains(method)) == true;

        return new AuthenticationCampaignsRegistration(
            record.Id,
            record.DisplayName,
            record.UserPrincipalName,
            record.UserType,
            methods,
            record.IsMfaRegistered,
            record.IsMfaCapable,
            record.IsPasswordlessCapable,
            record.IsSystemPreferredAuthenticationMethodEnabled,
            record.SystemPreferredAuthenticationMethods,
            record.UserPreferredMethodForSecondaryAuthentication,
            record.LastUpdatedDateTime,
            methods is null || (hasUnknownMethod && !hasPasskey) ? "unknown" : hasPasskey ? "registered" : "not_reported",
            methods is null || (hasUnknownMethod && !hasGenericFido) ? null : hasGenericFido,
            methods is null || (hasUnknownMethod && !hasPhone) ? "unknown" : hasPhone ? "registered" : "not_registered",
            GetPhonePreferenceState(record),
            directoryEntry?.Department,
            directoryEntry?.OfficeLocation,
            directoryEntry?.CompanyName,
            directoryEntry is null ? "unavailable" : "matched");
    }

    private static string GetPhonePreferenceState(AuthenticationCampaignsReportRecord record)
    {
        if (record.IsSystemPreferredAuthenticationMethodEnabled is null)
        {
            return "unknown";
        }

        if (record.IsSystemPreferredAuthenticationMethodEnabled == true)
        {
            if (record.SystemPreferredAuthenticationMethods is null)
            {
                return "unknown";
            }

            return record.SystemPreferredAuthenticationMethods.Any(PhonePreferences.Contains)
                ? "phone"
                : record.SystemPreferredAuthenticationMethods.Count > 0
                    && record.SystemPreferredAuthenticationMethods.All(NonPhonePreferences.Contains)
                    ? "not_phone"
                    : "unknown";
        }

        var preference = record.UserPreferredMethodForSecondaryAuthentication;
        if (string.IsNullOrWhiteSpace(preference) || preference.Equals("unknownFutureValue", StringComparison.OrdinalIgnoreCase))
        {
            return "unknown";
        }

        if (PhonePreferences.Contains(preference))
        {
            return "phone";
        }

        return NonPhonePreferences.Contains(preference) ? "not_phone" : "unknown";
    }
}
