using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20260926000100_AddPlatformWorkspaceGrants")]
public sealed class AddPlatformWorkspaceGrants : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "PlatformWorkspaceGrants",
            columns: table => new
            {
                OperatorTenantId = table.Column<Guid>(type: "uuid", nullable: false),
                OperatorObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PlatformWorkspaceGrants", x => new { x.OperatorTenantId, x.OperatorObjectId, x.WorkspaceId });
                table.ForeignKey(
                    name: "FK_PlatformWorkspaceGrants_Workspaces_WorkspaceId",
                    column: x => x.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PlatformWorkspaceGrants_OperatorTenantId_OperatorObjectId",
            table: "PlatformWorkspaceGrants",
            columns: new[] { "OperatorTenantId", "OperatorObjectId" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "PlatformWorkspaceGrants");
}
