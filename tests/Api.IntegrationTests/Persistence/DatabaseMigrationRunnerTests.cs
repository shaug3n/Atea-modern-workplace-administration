using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using DotNet.Testcontainers.Builders;
using Xunit;
using Npgsql;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Persistence;

public sealed class DatabaseMigrationRunnerTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task RunAsync_AppliesMigrationsToFreshDatabase_AndIsIdempotent()
    {
        using var services = CreateServices(postgres.GetConnectionString());

        var first = await DatabaseMigrationRunner.RunAsync(services, CancellationToken.None);
        var second = await DatabaseMigrationRunner.RunAsync(services, CancellationToken.None);

        first.Should().Be(0);
        second.Should().Be(0);
        var options = new DbContextOptionsBuilder<WorkplaceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;
        await using var db = new WorkplaceDbContext(options);
        (await db.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WithInvalidConnection_ReturnsFailure()
    {
        using var services = CreateServices("Host=127.0.0.1;Port=1;Database=missing;Username=missing;Password=missing;Timeout=1;Command Timeout=1");

        var result = await DatabaseMigrationRunner.RunAsync(services, CancellationToken.None);

        result.Should().NotBe(0);
    }

    [Fact]
    public async Task RuntimeCredential_CannotApplySchemaMigrations()
    {
        var databaseName = $"runtime_test_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await admin.OpenAsync();
            await using var createRole = admin.CreateCommand();
            createRole.CommandText = "CREATE ROLE workplace_migrator LOGIN PASSWORD 'migration-test-password'; CREATE ROLE workplace_runtime LOGIN PASSWORD 'runtime-test-password';";
            await createRole.ExecuteNonQueryAsync();
            await using var createDatabase = admin.CreateCommand();
            createDatabase.CommandText = $"CREATE DATABASE {databaseName};";
            await createDatabase.ExecuteNonQueryAsync();
            await using var grantConnect = admin.CreateCommand();
            grantConnect.CommandText = $"GRANT CONNECT ON DATABASE {databaseName} TO workplace_migrator, workplace_runtime;";
            await grantConnect.ExecuteNonQueryAsync();
        }

        var adminBuilder = new NpgsqlConnectionStringBuilder(postgres.GetConnectionString()) { Database = databaseName };
        await using (var admin = new NpgsqlConnection(adminBuilder.ConnectionString))
        {
            await admin.OpenAsync();
            await using var privileges = admin.CreateCommand();
            privileges.CommandText = "REVOKE CREATE ON SCHEMA public FROM PUBLIC; GRANT USAGE, CREATE ON SCHEMA public TO workplace_migrator; GRANT USAGE ON SCHEMA public TO workplace_runtime;";
            await privileges.ExecuteNonQueryAsync();
        }
        var runtimeBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            Username = "workplace_runtime",
            Password = "runtime-test-password"
        };
        var migrationBuilder = new NpgsqlConnectionStringBuilder(adminBuilder.ConnectionString)
        {
            Username = "workplace_migrator",
            Password = "migration-test-password"
        };
        await using (var migrator = new NpgsqlConnection(migrationBuilder.ConnectionString))
        {
            await migrator.OpenAsync();
            await using var defaults = migrator.CreateCommand();
            defaults.CommandText = "ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO workplace_runtime; ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO workplace_runtime;";
            await defaults.ExecuteNonQueryAsync();
        }
        using var runtimeServices = CreateServices(runtimeBuilder.ConnectionString);

        var runtimeResult = await DatabaseMigrationRunner.RunAsync(runtimeServices, CancellationToken.None);
        runtimeResult.Should().NotBe(0);

        using var migrationServices = CreateServices(migrationBuilder.ConnectionString);
        var migrationResult = await DatabaseMigrationRunner.RunAsync(migrationServices, CancellationToken.None);
        var repeatedMigrationResult = await DatabaseMigrationRunner.RunAsync(migrationServices, CancellationToken.None);

        migrationResult.Should().Be(0);
        repeatedMigrationResult.Should().Be(0);
        await using var runtimeConnection = new NpgsqlConnection(runtimeBuilder.ConnectionString);
        await runtimeConnection.OpenAsync();
        await using var query = runtimeConnection.CreateCommand();
        query.CommandText = "SELECT count(*) FROM \"Workspaces\";";
        (await query.ExecuteScalarAsync()).Should().Be(0L);
    }

    private static ServiceProvider CreateServices(string connectionString) => new ServiceCollection()
        .AddSingleton<IConfiguration>(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:WorkplaceMigrationDb"] = connectionString
        }).Build())
        .BuildServiceProvider();
}
