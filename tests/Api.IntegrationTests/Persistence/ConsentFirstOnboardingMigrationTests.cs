using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Entities;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Atea.UnifiedWorkplace.Api.IntegrationTests.Persistence;

public sealed class ConsentFirstOnboardingMigrationTests : IAsyncLifetime
{
    private static readonly Guid WorkspaceId = Guid.NewGuid();
    private static readonly Guid TenantId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ObjectId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").Build();

    public Task InitializeAsync() => postgres.StartAsync();

    public Task DisposeAsync() => postgres.DisposeAsync().AsTask();

    [Fact]
    public async Task Migration_defaults_old_challenges_and_keeps_new_bindings_nullable()
    {
        var options = Options();
        await using var db = new WorkplaceDbContext(options);
        var migrator = db.Database.GetService<IMigrator>();
        await migrator.MigrateAsync("20260926000100_AddPlatformWorkspaceGrants");
        db.Workspaces.Add(NewWorkspace("awaiting_invitation"));
        await db.SaveChangesAsync();

        var invitationId = Guid.NewGuid();
        var stateHash = "legacy-state-hash";
        await using (var connection = new NpgsqlConnection(postgres.GetConnectionString()))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO "PlatformInvitations"
                    ("Id", "WorkspaceId", "Email", "DisplayName", "ApprovedTenantObjectId", "Role", "ModuleKeysJson", "NonceHash", "ExpiresAt", "RedeemedAt", "RevokedAt", "CreatedAt")
                VALUES
                    (@invitationId, @workspaceId, 'admin@example.com', 'Admin', NULL, 'customer_admin', '[]', 'legacy-nonce-hash', @expiresAt, NULL, NULL, @createdAt);
                INSERT INTO "ConsentChallenges"
                    ("StateHash", "WorkspaceId", "TenantId", "CorrelationId", "ExpiresAt", "ConsumedAt")
                VALUES
                    (@stateHash, @workspaceId, @tenantId, 'legacy-correlation', @expiresAt, NULL);
                """;
            command.Parameters.AddWithValue("invitationId", invitationId);
            command.Parameters.AddWithValue("workspaceId", WorkspaceId);
            command.Parameters.AddWithValue("tenantId", TenantId);
            command.Parameters.AddWithValue("stateHash", stateHash);
            command.Parameters.AddWithValue("expiresAt", DateTimeOffset.UtcNow.AddHours(1));
            command.Parameters.AddWithValue("createdAt", DateTimeOffset.UtcNow);
            await command.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync("20261007000100_AddConsentFirstOnboarding");

        await using var verify = new NpgsqlConnection(postgres.GetConnectionString());
        await verify.OpenAsync();
        await using var challengeQuery = verify.CreateCommand();
        challengeQuery.CommandText = """SELECT "Purpose", "InvitationId" FROM "ConsentChallenges" WHERE "StateHash" = @stateHash""";
        challengeQuery.Parameters.AddWithValue("stateHash", stateHash);
        await using var challenge = await challengeQuery.ExecuteReaderAsync();
        (await challenge.ReadAsync()).Should().BeTrue();
        challenge.GetString(0).Should().Be("workspace");
        challenge.IsDBNull(1).Should().BeTrue();
        await challenge.DisposeAsync();

        await using var invitationQuery = verify.CreateCommand();
        invitationQuery.CommandText = """SELECT "RedeemedByTenantObjectId" FROM "PlatformInvitations" WHERE "Id" = @invitationId""";
        invitationQuery.Parameters.AddWithValue("invitationId", invitationId);
        (await invitationQuery.ExecuteScalarAsync()).Should().Be(DBNull.Value);

        var challengeType = db.Model.FindEntityType(typeof(ConsentChallenge))!;
        challengeType.FindPrimaryKey()!.Properties.Select(property => property.Name).Should().Equal(nameof(ConsentChallenge.StateHash));
        challengeType.GetIndexes().Should().Contain(index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(ConsentChallenge.InvitationId), nameof(ConsentChallenge.ExpiresAt) }));
        challengeType.GetForeignKeys().Should().Contain(foreignKey =>
            foreignKey.Properties.Single().Name == nameof(ConsentChallenge.InvitationId) &&
            foreignKey.DeleteBehavior == DeleteBehavior.Restrict);

        db.ConsentChallenges.Add(new ConsentChallenge
        {
            StateHash = "invitation-bound-state-hash",
            WorkspaceId = WorkspaceId,
            InvitationId = invitationId,
            TenantId = TenantId,
            Purpose = "invitation",
            CorrelationId = "invitation-correlation",
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(20)
        });
        await db.SaveChangesAsync();
        await using var deleteConnection = new NpgsqlConnection(postgres.GetConnectionString());
        await deleteConnection.OpenAsync();
        await using var deleteInvitation = deleteConnection.CreateCommand();
        deleteInvitation.CommandText = """DELETE FROM "PlatformInvitations" WHERE "Id" = @invitationId""";
        deleteInvitation.Parameters.AddWithValue("invitationId", invitationId);
        var foreignKeyFailure = await FluentActions.Invoking(() => deleteInvitation.ExecuteNonQueryAsync())
            .Should().ThrowAsync<PostgresException>();
        foreignKeyFailure.Which.SqlState.Should().Be("23503");
    }

    [Fact]
    public async Task RedeemAsync_records_object_id_and_preserves_an_existing_connection_state()
    {
        await using (var setup = new WorkplaceDbContext(Options()))
        {
            await setup.Database.MigrateAsync();
            setup.Workspaces.Add(NewWorkspace("connected"));
            setup.PlatformInvitations.Add(new PlatformInvitation
            {
                Id = Guid.NewGuid(),
                WorkspaceId = WorkspaceId,
                Email = "admin@example.com",
                DisplayName = "Admin",
                Role = "customer_admin",
                ModuleKeysJson = "[]",
                NonceHash = "redeemer-test-hash",
                ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
                CreatedAt = DateTimeOffset.UtcNow
            });
            await setup.SaveChangesAsync();
        }

        await using (var redeem = new WorkplaceDbContext(Options()))
        {
            var repository = new WorkspaceOnboardingRepository(redeem);
            var result = await repository.RedeemAsync(
                "redeemer-test-hash",
                TenantId,
                ObjectId,
                "admin@example.com",
                "Admin");
            result.Should().NotBeNull();
            result!.Workspace.ConnectionStatus.Should().Be("connected");
        }

        await using var verify = new WorkplaceDbContext(Options());
        var invitation = await verify.PlatformInvitations.SingleAsync(x => x.NonceHash == "redeemer-test-hash");
        invitation.RedeemedAt.Should().NotBeNull();
        (await verify.WorkspaceMemberships.CountAsync(x => x.WorkspaceId == WorkspaceId && x.TenantObjectId == ObjectId)).Should().Be(1);
        var redeemerObjectId = await verify.Database.SqlQuery<Guid?>(
            $"""SELECT "RedeemedByTenantObjectId" AS "Value" FROM "PlatformInvitations" WHERE "Id" = {invitation.Id}""")
            .SingleAsync();
        redeemerObjectId.Should().Be(ObjectId);
    }

    private DbContextOptions<WorkplaceDbContext> Options() =>
        new DbContextOptionsBuilder<WorkplaceDbContext>().UseNpgsql(postgres.GetConnectionString()).Options;

    private static Workspace NewWorkspace(string status) => new()
    {
        Id = WorkspaceId,
        TenantId = TenantId,
        DisplayName = "Consent test workspace",
        ConnectionStatus = status,
        CreatedAt = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow
    };
}
