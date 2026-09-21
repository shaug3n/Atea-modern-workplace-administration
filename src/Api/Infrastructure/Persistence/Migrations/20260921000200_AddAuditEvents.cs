using System;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[Migration("20260921000200_AddAuditEvents")]
[DbContext(typeof(WorkplaceDbContext))]
public partial class AddAuditEvents : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                ActorObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Action = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                TargetType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TargetId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                Outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                GraphCorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                GraphRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                PimRequestId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                FailureCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SafeMetadataJson = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditEvents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_WorkspaceId_Timestamp",
            table: "AuditEvents",
            columns: new[] { "WorkspaceId", "Timestamp" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditEvents_WorkspaceId_TargetType_TargetId_Timestamp",
            table: "AuditEvents",
            columns: new[] { "WorkspaceId", "TargetType", "TargetId", "Timestamp" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "AuditEvents");
    }
}
