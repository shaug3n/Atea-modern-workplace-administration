using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20260921000400_AddConsentChallenges")]
public partial class AddConsentChallenges : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ConsentChallenges",
            columns: table => new
            {
                StateHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ConsentChallenges", x => x.StateHash);
                table.ForeignKey("FK_ConsentChallenges_Workspaces_WorkspaceId", x => x.WorkspaceId, "Workspaces", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ConsentChallenges_WorkspaceId_TenantId_ExpiresAt",
            table: "ConsentChallenges",
            columns: new[] { "WorkspaceId", "TenantId", "ExpiresAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable(name: "ConsentChallenges");
}
