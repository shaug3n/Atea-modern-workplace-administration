using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.AuthenticationCampaigns;
using Atea.UnifiedWorkplace.Api.Infrastructure.Graph;
using FluentAssertions;

namespace Atea.UnifiedWorkplace.Api.UnitTests.AuthenticationCampaigns;

public sealed class AuthenticationCampaignsServiceTests
{
    private static readonly WorkspaceContext Workspace = new(
        new AuthenticatedUser(Guid.NewGuid(), Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
        new WorkspaceMembership(Guid.NewGuid(), "Contoso Workplace", "member"));

    [Fact]
    public async Task Deduplicates_by_object_id_using_latest_non_null_timestamp_and_keeps_first_on_ties()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult(
        [
            Record("user-1", "Old", "2026-10-01T00:00:00Z"),
            Record("user-2", "Tie winner", "2026-10-02T00:00:00Z"),
            Record("user-1", "Newest", "2026-10-03T00:00:00Z"),
            Record("user-2", "Tie loser", "2026-10-02T00:00:00Z"),
            Record("user-1", "No timestamp", null)
        ], null));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(), null));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Response!.ObservedRecordCount.Should().Be(5);
        result.Response.DuplicateRecordCount.Should().Be(3);
        result.Response.Items.Select(item => item.DisplayName).Should().Equal("Newest", "Tie winner");
        result.Response.SourceLastUpdatedFrom.Should().Be(DateTimeOffset.Parse("2026-10-02T00:00:00Z"));
        result.Response.SourceLastUpdatedTo.Should().Be(DateTimeOffset.Parse("2026-10-03T00:00:00Z"));
    }

    [Fact]
    public async Task Matched_null_attributes_are_distinct_from_unavailable_directory_joins()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult(
        [
            Record("matched", "Matched"),
            Record("absent", "Absent")
        ], null));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>
            {
                ["matched"] = new("matched", null, null, null)
            }, null));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Response!.Items[0].DirectoryJoinState.Should().Be("matched");
        result.Response.Items[0].Department.Should().BeNull();
        result.Response.Items[1].DirectoryJoinState.Should().Be("unavailable");
        result.Response.DirectoryEnrichmentState.Should().Be("complete");
        result.Response.EnrichedAccountCount.Should().Be(1);
    }

    [Fact]
    public async Task Failed_report_without_rows_returns_error_and_no_response()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult(
            [], new GraphOperationResult(false, "not_authorized")));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(), null));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Response.Should().BeNull();
        result.Error!.Category.Should().Be("not_authorized");
    }

    [Fact]
    public async Task Report_partial_failure_returns_observed_rows_and_error_category()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult(
            [Record("user-1", "Ada")], new GraphOperationResult(false, "temporarily_unavailable"), PartialData: true));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(), null));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Response!.PartialData.Should().BeTrue();
        result.Response.Items.Should().ContainSingle().Which.DisplayName.Should().Be("Ada");
        result.Response.ReportErrorCategory.Should().Be("temporarily_unavailable");
    }

    [Fact]
    public async Task Report_partial_failure_after_empty_page_returns_partial_response_not_fatal_empty_error()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult(
            [], new GraphOperationResult(false, "temporarily_unavailable"), PartialData: true));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(), null));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Error.Should().BeNull();
        result.Response!.Items.Should().BeEmpty();
        result.Response.PartialData.Should().BeTrue();
        result.Response.ReportErrorCategory.Should().Be("temporarily_unavailable");
    }

    [Fact]
    public async Task Directory_failure_keeps_report_rows_and_marks_unmatched_attributes_unavailable()
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult([Record("user-1", "Ada")], null));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(),
            new GraphOperationResult(false, "not_authorized")));

        var result = await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None);

        result.Response!.Items.Should().ContainSingle().Which.DirectoryJoinState.Should().Be("unavailable");
        result.Response.DirectoryEnrichmentState.Should().Be("unavailable");
        result.Response.PartialData.Should().BeTrue();
        result.Response.DirectoryErrorCategory.Should().Be("not_authorized");
    }

    [Theory]
    [InlineData("passKeyDeviceBound", "registered", false)]
    [InlineData("fido2", "not_reported", true)]
    [InlineData("microsoftAuthenticatorPasswordless", "not_reported", false)]
    [InlineData("windowsHelloForBusiness", "not_reported", false)]
    [InlineData("futureMethod", "unknown", null)]
    public async Task Normalizes_passkeys_without_conflating_other_methods(
        string method,
        string expectedPasskeyState,
        bool? expectedGenericFido)
    {
        var item = await ReadOne(Record("user-1", "Ada", methods: [method]));

        item.PasskeyRegistrationState.Should().Be(expectedPasskeyState);
        item.IsGenericFido2Registered.Should().Be(expectedGenericFido);
        item.MethodsRegistered.Should().Equal(method);
    }

    [Fact]
    public async Task Missing_and_empty_method_collections_remain_distinct()
    {
        var missing = await ReadOne(Record("missing", "Missing", methods: null));
        var empty = await ReadOne(Record("empty", "Empty", methods: []));

        missing.MethodsRegistered.Should().BeNull();
        missing.PasskeyRegistrationState.Should().Be("unknown");
        empty.MethodsRegistered.Should().BeEmpty();
        empty.PasskeyRegistrationState.Should().Be("not_reported");
    }

    [Fact]
    public async Task Phone_registration_and_preference_states_follow_reported_facts()
    {
        var registered = await ReadOne(Record("registered", "Registered", methods: ["mobilePhone"]));
        var phonePreference = await ReadOne(Record(
            "preferred", "Preferred", methods: [],
            systemPreferredEnabled: true,
            systemPreferredMethods: ["voiceMobile"],
            userPreferredMethod: "none"));
        var nonPhonePreference = await ReadOne(Record(
            "not-preferred", "Not preferred", methods: [],
            systemPreferredEnabled: true,
            systemPreferredMethods: ["push"],
            userPreferredMethod: "sms"));

        registered.PhoneRegistrationState.Should().Be("registered");
        phonePreference.PhonePreferenceState.Should().Be("phone");
        nonPhonePreference.PhonePreferenceState.Should().Be("not_phone");
    }

    [Theory]
    [InlineData("sms")]
    [InlineData("voiceMobile")]
    [InlineData("voiceAlternateMobile")]
    [InlineData("voiceOffice")]
    public async Task Recognizes_documented_phone_preferences(string preference)
    {
        var item = await ReadOne(Record("user-1", "Ada", methods: [], systemPreferredEnabled: false, userPreferredMethod: preference));

        item.PhonePreferenceState.Should().Be("phone");
    }

    [Theory]
    [InlineData("push")]
    [InlineData("oath")]
    [InlineData("none")]
    public async Task Recognizes_documented_non_phone_preferences(string preference)
    {
        var item = await ReadOne(Record("user-1", "Ada", methods: [], systemPreferredEnabled: false, userPreferredMethod: preference));

        item.PhonePreferenceState.Should().Be("not_phone");
    }

    [Fact]
    public async Task Missing_empty_and_unknown_preferences_do_not_fall_back()
    {
        var missing = await ReadOne(Record("missing", "Missing", methods: [], systemPreferredEnabled: false, userPreferredMethod: null));
        var empty = await ReadOne(Record("empty", "Empty", methods: [], systemPreferredEnabled: false, userPreferredMethod: ""));
        var unknown = await ReadOne(Record("unknown", "Unknown", methods: [], systemPreferredEnabled: false, userPreferredMethod: "unknownFutureValue"));
        var systemEmpty = await ReadOne(Record(
            "system-empty", "System empty", methods: [], systemPreferredEnabled: true,
            systemPreferredMethods: [], userPreferredMethod: "sms"));

        missing.PhonePreferenceState.Should().Be("unknown");
        empty.PhonePreferenceState.Should().Be("unknown");
        unknown.PhonePreferenceState.Should().Be("unknown");
        systemEmpty.PhonePreferenceState.Should().Be("unknown");
    }

    private static async Task<AuthenticationCampaignsRegistration> ReadOne(AuthenticationCampaignsReportRecord record)
    {
        var report = new StubReportReader(new AuthenticationCampaignsReportReadResult([record], null));
        var directory = new StubDirectoryReader(new AuthenticationCampaignsDirectoryReadResult(
            new Dictionary<string, AuthenticationCampaignsDirectoryEntry>(), null));
        return (await new AuthenticationCampaignsService(report, directory).ReadAsync(Workspace, CancellationToken.None))
            .Response!.Items.Single();
    }

    private static AuthenticationCampaignsReportRecord Record(
        string id,
        string displayName,
        string? lastUpdated = null,
        IReadOnlyList<string>? methods = null,
        bool? systemPreferredEnabled = null,
        IReadOnlyList<string>? systemPreferredMethods = null,
        string? userPreferredMethod = null) =>
        new(
            id,
            displayName,
            $"{id}@example.com",
            "Member",
            methods,
            true,
            true,
            false,
            systemPreferredEnabled,
            systemPreferredMethods,
            userPreferredMethod,
            lastUpdated is null ? null : DateTimeOffset.Parse(lastUpdated));

    private sealed class StubReportReader(AuthenticationCampaignsReportReadResult result) : IAuthenticationCampaignsRegistrationReportReader
    {
        public Task<AuthenticationCampaignsReportReadResult> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }

    private sealed class StubDirectoryReader(AuthenticationCampaignsDirectoryReadResult result) : IAuthenticationCampaignsDirectoryReader
    {
        public Task<AuthenticationCampaignsDirectoryReadResult> ReadAsync(WorkspaceContext context, CancellationToken cancellationToken) =>
            Task.FromResult(result);
    }
}
