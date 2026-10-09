using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;

public sealed class WorkplaceDbContext(DbContextOptions<WorkplaceDbContext> options) : DbContext(options)
{
    public DbSet<Workspace> Workspaces => Set<Workspace>();
    public DbSet<WorkspaceMembership> WorkspaceMemberships => Set<WorkspaceMembership>();
    public DbSet<TenantConnection> TenantConnections => Set<TenantConnection>();
    public DbSet<WorkspaceSettings> WorkspaceSettings => Set<WorkspaceSettings>();
    public DbSet<PlatformInvitation> PlatformInvitations => Set<PlatformInvitation>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<ConsentChallenge> ConsentChallenges => Set<ConsentChallenge>();
    public DbSet<PlatformWorkspaceGrant> PlatformWorkspaceGrants => Set<PlatformWorkspaceGrant>();
    public DbSet<FeedbackSubmission> FeedbackSubmissions => Set<FeedbackSubmission>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workspace>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.TenantId).IsUnique();
            entity.Property(x => x.DisplayName).HasMaxLength(200).IsRequired();
            entity.Property(x => x.ConnectionStatus).HasMaxLength(64).IsRequired();
            ConfigureUtc(entity.Property(x => x.CreatedAt));
            ConfigureUtc(entity.Property(x => x.UpdatedAt));
        });
        modelBuilder.Entity<WorkspaceMembership>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.TenantObjectId }).IsUnique();
            entity.Property(x => x.Email).HasMaxLength(320).IsRequired();
            entity.Property(x => x.PlatformRole).HasMaxLength(100).IsRequired();
            entity.Property(x => x.ModuleGrantsJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
            ConfigureUtc(entity.Property(x => x.CreatedAt));
            entity.HasOne(x => x.Workspace).WithMany(x => x.Memberships).HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<TenantConnection>(entity =>
        {
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.ConsentScopesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.Status).HasMaxLength(64).IsRequired();
            ConfigureUtc(entity.Property(x => x.LastVerifiedAt));
            ConfigureUtc(entity.Property(x => x.UpdatedAt));
            entity.HasOne(x => x.Workspace).WithOne(x => x.TenantConnection).HasForeignKey<TenantConnection>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<WorkspaceSettings>(entity =>
        {
            entity.HasKey(x => x.WorkspaceId);
            entity.Property(x => x.EnabledModulesJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.DefaultColumnsJson).HasColumnType("jsonb").IsRequired();
            entity.Property(x => x.DefaultFiltersJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne(x => x.Workspace).WithOne(x => x.Settings).HasForeignKey<WorkspaceSettings>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PlatformInvitation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NonceHash).IsUnique();
            entity.Property(x => x.NonceHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ApprovedTenantObjectId);
            entity.Property(x => x.Role).HasMaxLength(32).HasDefaultValue("customer_admin").IsRequired();
            entity.Property(x => x.ModuleKeysJson).HasColumnType("jsonb").HasDefaultValueSql("'[]'::jsonb").IsRequired();
            ConfigureUtc(entity.Property(x => x.ExpiresAt));
            ConfigureUtc(entity.Property(x => x.RedeemedAt));
            entity.Property(x => x.RedeemedByTenantObjectId);
            ConfigureUtc(entity.Property(x => x.RevokedAt));
            ConfigureUtc(entity.Property(x => x.CreatedAt));
            entity.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(x => new { x.TenantId, x.UserObjectId });
            entity.Property(x => x.Theme).HasMaxLength(5).IsRequired();
            ConfigureUtc(entity.Property(x => x.UpdatedAt));
        });
        modelBuilder.Entity<IdempotencyRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.ActorObjectId, x.Operation, x.TargetId, x.Key }).IsUnique();
            entity.Property(x => x.Operation).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Key).HasMaxLength(200).IsRequired();
            entity.Property(x => x.RequestFingerprint).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ResultCategory).HasMaxLength(100).IsRequired();
            entity.Property(x => x.SafeResultJson).HasColumnType("jsonb").IsRequired();
            ConfigureUtc(entity.Property(x => x.CreatedAt));
        });
        modelBuilder.Entity<AuditEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.Timestamp });
            entity.HasIndex(x => new { x.WorkspaceId, x.TargetType, x.TargetId, x.Timestamp });
            entity.Property(x => x.Action).HasMaxLength(128).IsRequired();
            entity.Property(x => x.TargetType).HasMaxLength(64).IsRequired();
            entity.Property(x => x.TargetId).HasMaxLength(256).IsRequired();
            entity.Property(x => x.Outcome).HasMaxLength(32).IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100);
            entity.Property(x => x.GraphCorrelationId).HasMaxLength(100);
            entity.Property(x => x.GraphRequestId).HasMaxLength(100);
            entity.Property(x => x.PimRequestId).HasMaxLength(100);
            entity.Property(x => x.FailureCategory).HasMaxLength(100);
            entity.Property(x => x.SafeMetadataJson).HasColumnType("jsonb").IsRequired();
            ConfigureUtc(entity.Property(x => x.Timestamp));
        });
        modelBuilder.Entity<ConsentChallenge>(entity =>
        {
            entity.HasKey(x => x.StateHash);
            entity.Property(x => x.StateHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.InvitationId);
            entity.Property(x => x.Purpose).HasMaxLength(32).HasDefaultValue("workspace").IsRequired();
            entity.Property(x => x.CorrelationId).HasMaxLength(100).IsRequired();
            ConfigureUtc(entity.Property(x => x.ExpiresAt));
            ConfigureUtc(entity.Property(x => x.ConsumedAt));
            entity.HasIndex(x => new { x.WorkspaceId, x.TenantId, x.ExpiresAt });
            entity.HasIndex(x => new { x.InvitationId, x.ExpiresAt });
            entity.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Invitation).WithMany().HasForeignKey(x => x.InvitationId).OnDelete(DeleteBehavior.Restrict);
        });
        modelBuilder.Entity<PlatformWorkspaceGrant>(entity =>
        {
            entity.HasKey(x => new { x.OperatorTenantId, x.OperatorObjectId, x.WorkspaceId });
            entity.HasIndex(x => new { x.OperatorTenantId, x.OperatorObjectId });
            ConfigureUtc(entity.Property(x => x.CreatedAt));
            entity.HasOne(x => x.Workspace).WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<FeedbackSubmission>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => new { x.WorkspaceId, x.SubmitterObjectId, x.RetryKeyHash })
                .HasDatabaseName("IX_FeedbackSubmissions_RetryKey")
                .IsUnique();
            entity.HasIndex(x => x.ExpiresAt);
            entity.Property(x => x.Category).HasMaxLength(32).IsRequired();
            entity.Property(x => x.Subject).HasMaxLength(120).IsRequired();
            entity.Property(x => x.Message).HasMaxLength(4_000).IsRequired();
            entity.Property(x => x.RetryKeyHash).HasMaxLength(64).IsRequired();
            entity.Property(x => x.PayloadFingerprint).HasMaxLength(64).IsRequired();
            ConfigureUtc(entity.Property(x => x.CreatedAt));
            ConfigureUtc(entity.Property(x => x.ExpiresAt));
            entity.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureUtc(PropertyBuilder<DateTimeOffset> property) => property.HasColumnType("timestamp with time zone");
    private static void ConfigureUtc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("timestamp with time zone");
}
