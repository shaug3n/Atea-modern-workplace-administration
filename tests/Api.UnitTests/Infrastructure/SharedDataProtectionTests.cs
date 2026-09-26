using Atea.UnifiedWorkplace.Api.Authorization;
using Atea.UnifiedWorkplace.Api.Features.Audit;
using Atea.UnifiedWorkplace.Api.Features.Devices;
using Atea.UnifiedWorkplace.Api.Infrastructure.Configuration;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;

namespace Atea.UnifiedWorkplace.Api.UnitTests.Infrastructure;

public sealed class SharedDataProtectionTests
{
    [Fact]
    public void Audit_and_device_tokens_are_shared_only_by_providers_using_the_same_key_ring()
    {
        var root = Directory.CreateTempSubdirectory("atea-dp-");
        var shared = Directory.CreateDirectory(Path.Combine(root.FullName, "shared"));
        var isolated = Directory.CreateDirectory(Path.Combine(root.FullName, "isolated"));
        try
        {
            using var first = CreateProvider(shared);
            using var second = CreateProvider(shared);
            using var unrelated = CreateProvider(isolated);
            var workspaceId = Guid.NewGuid();
            var userId = Guid.NewGuid();
            var context = new WorkspaceContext(
                new AuthenticatedUser(Guid.NewGuid(), userId, "operator@example.test", "Operator", "Member"),
                new WorkspaceMembership(workspaceId, "Test workspace"));
            var auditProtector = new AuditContinuationTokenProtector(first.GetRequiredService<IDataProtectionProvider>());
            var deviceProtector = new DeviceContinuationTokenProtector(first.GetRequiredService<IDataProtectionProvider>());
            var otherAuditProtector = new AuditContinuationTokenProtector(second.GetRequiredService<IDataProtectionProvider>());
            var otherDeviceProtector = new DeviceContinuationTokenProtector(second.GetRequiredService<IDataProtectionProvider>());
            var isolatedAuditProtector = new AuditContinuationTokenProtector(unrelated.GetRequiredService<IDataProtectionProvider>());
            var isolatedDeviceProtector = new DeviceContinuationTokenProtector(unrelated.GetRequiredService<IDataProtectionProvider>());
            var now = DateTimeOffset.UtcNow;
            var auditToken = auditProtector.Protect(new AuditContinuationCursor(workspaceId, userId, null, null, null, 25, now, Guid.NewGuid()));
            var deviceQuery = new DeviceSearchQuery("laptop", 50, "compliant", "Windows");
            var deviceToken = deviceProtector.Protect(context, deviceQuery, "/v1.0/deviceManagement/managedDevices?$skiptoken=opaque", now);

            otherAuditProtector.TryUnprotect(auditToken, out var auditCursor).Should().BeTrue();
            auditCursor!.WorkspaceId.Should().Be(workspaceId);
            otherDeviceProtector.TryUnprotect(deviceToken, context, deviceQuery, out var continuationPath).Should().BeTrue();
            continuationPath.Should().Be("/v1.0/deviceManagement/managedDevices?$skiptoken=opaque");
            isolatedAuditProtector.TryUnprotect(auditToken, out _).Should().BeFalse();
            isolatedDeviceProtector.TryUnprotect(deviceToken, context, deviceQuery, out _).Should().BeFalse();
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static ServiceProvider CreateProvider(DirectoryInfo keyDirectory) => new ServiceCollection()
        .AddDataProtection()
        .SetApplicationName(DataProtectionConfiguration.ApplicationName)
        .PersistKeysToFileSystem(keyDirectory)
        .Services.BuildServiceProvider();
}
