using System.Text.Json;
using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Overview;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using FluentAssertions;
using WorkspaceMembership = Atea.UnifiedWorkplace.Api.Authorization.WorkspaceMembership;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Overview;

public sealed class OverviewActivityReaderTests
{
    [Fact]
    public void Overview_activity_query_is_isolated_bounded_stable_and_projects_only_safe_fields()
    {
        var tenantId = Guid.NewGuid();
        var workspaceId = Guid.NewGuid();
        var context = new WorkspaceContext(
            new AuthenticatedUser(tenantId, Guid.NewGuid(), "alex@example.com", "Alex Example", "Member"),
            new WorkspaceMembership(workspaceId, "Contoso Workplace"));
        var tiedTimestamp = DateTimeOffset.Parse("2026-10-08T10:00:00Z");
        var events = new[]
        {
            Event(workspaceId, tenantId, tenantId, "older", tiedTimestamp.AddMinutes(-1)),
            Event(workspaceId, tenantId, tenantId, "tie-low", tiedTimestamp, Guid.Parse("00000000-0000-0000-0000-000000000001")),
            Event(workspaceId, tenantId, tenantId, "tie-high", tiedTimestamp, Guid.Parse("00000000-0000-0000-0000-000000000002")),
            Event(workspaceId, tenantId, tenantId, "newer-1", tiedTimestamp.AddMinutes(1)),
            Event(workspaceId, tenantId, tenantId, "newer-2", tiedTimestamp.AddMinutes(2)),
            Event(workspaceId, tenantId, tenantId, "newer-3", tiedTimestamp.AddMinutes(3)),
            Event(workspaceId, tenantId, tenantId, "newer-4", tiedTimestamp.AddMinutes(4)),
            Event(Guid.NewGuid(), tenantId, tenantId, "wrong-workspace", tiedTimestamp.AddMinutes(10)),
            Event(workspaceId, Guid.NewGuid(), tenantId, "wrong-tenant", tiedTimestamp.AddMinutes(11)),
            Event(workspaceId, tenantId, Guid.NewGuid(), "wrong-actor-tenant", tiedTimestamp.AddMinutes(12))
        };

        var result = OverviewActivityReader.BuildRecentQuery(events.AsQueryable(), context).ToArray();

        result.Should().HaveCount(5);
        result.Select(item => item.Action).Should().Equal("newer-4", "newer-3", "newer-2", "newer-1", "tie-high");
        result.Should().Contain(item => item.Action == "tie-high");
        result.Should().NotContain(item => item.Action.StartsWith("wrong-", StringComparison.Ordinal));
        result.Should().OnlyContain(item => item.Outcome == "succeeded");
        result[0].Timestamp.Should().Be(tiedTimestamp.AddMinutes(4));

        var serialized = JsonSerializer.Serialize(result);
        serialized.Should().Contain("\"Action\"").And.Contain("\"Outcome\"").And.Contain("\"Timestamp\"");
        serialized.Should().NotContain("\"Id\"")
            .And.NotContain("Target")
            .And.NotContain("Correlation")
            .And.NotContain("Failure")
            .And.NotContain("Metadata");
    }

    private static AuditEvent Event(
        Guid workspaceId,
        Guid tenantId,
        Guid actorTenantId,
        string action,
        DateTimeOffset timestamp,
        Guid? id = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            WorkspaceId = workspaceId,
            TenantId = tenantId,
            ActorTenantId = actorTenantId,
            ActorObjectId = Guid.NewGuid(),
            Action = action,
            TargetType = "user",
            TargetId = "private-target",
            Outcome = "succeeded",
            Timestamp = timestamp,
            CorrelationId = "private-correlation",
            FailureCategory = "private-failure",
            SafeMetadataJson = "{\"private\":\"value\"}"
        };
}
