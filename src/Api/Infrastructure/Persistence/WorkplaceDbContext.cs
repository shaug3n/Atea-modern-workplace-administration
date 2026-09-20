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
            entity.HasOne(x => x.Workspace).WithOne(x => x.Settings).HasForeignKey<WorkspaceSettings>(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        });
        modelBuilder.Entity<PlatformInvitation>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.HasIndex(x => x.NonceHash).IsUnique();
            entity.Property(x => x.NonceHash).HasMaxLength(128).IsRequired();
            entity.Property(x => x.ApprovedTenantObjectId);
            ConfigureUtc(entity.Property(x => x.ExpiresAt));
            ConfigureUtc(entity.Property(x => x.RedeemedAt));
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
    }

    private static void ConfigureUtc(PropertyBuilder<DateTimeOffset> property) => property.HasColumnType("timestamp with time zone");
    private static void ConfigureUtc(PropertyBuilder<DateTimeOffset?> property) => property.HasColumnType("timestamp with time zone");
}
