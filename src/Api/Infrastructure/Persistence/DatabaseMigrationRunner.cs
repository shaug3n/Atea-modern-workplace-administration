using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;

public static class DatabaseMigrationRunner
{
    public static async Task<int> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString("WorkplaceMigrationDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            services.GetService<ILoggerFactory>()?.CreateLogger(nameof(DatabaseMigrationRunner))
                .LogError("Database migration failed: ConnectionStrings:WorkplaceMigrationDb is not configured.");
            return 1;
        }

        var options = new DbContextOptionsBuilder<WorkplaceDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        try
        {
            await using var db = new WorkplaceDbContext(options);
            await db.Database.MigrateAsync(cancellationToken);
            return 0;
        }
        catch
        {
            // Driver exceptions may include server or credential details; keep them out of deployment logs.
            services.GetService<ILoggerFactory>()?.CreateLogger(nameof(DatabaseMigrationRunner))
                .LogError("Database migration failed. Review the database server logs and migration-job identity.");
            return 1;
        }
    }
}
