using System;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[Migration("20260921000100_AddIdempotencyRecords")]
[DbContext(typeof(WorkplaceDbContext))]
public partial class AddIdempotencyRecords : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "IdempotencyRecords",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Operation = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                RequestFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                StatusCode = table.Column<int>(type: "integer", nullable: false),
                ResultCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                SafeResultJson = table.Column<string>(type: "jsonb", nullable: false),
                GraphCorrelationId = table.Column<string>(type: "text", nullable: true),
                GraphRequestId = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_IdempotencyRecords", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_IdempotencyRecords_WorkspaceId_ActorObjectId_Operation_TargetId_Key",
            table: "IdempotencyRecords",
            columns: new[] { "WorkspaceId", "ActorObjectId", "Operation", "TargetId", "Key" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "IdempotencyRecords");
    }
}
