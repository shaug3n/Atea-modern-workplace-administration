using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

public partial class AddWorkspaceDefaultFilters : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.AddColumn<string>(
        name: "DefaultFiltersJson",
        table: "WorkspaceSettings",
        type: "jsonb",
        nullable: false,
        defaultValue: "{}");

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropColumn(
        name: "DefaultFiltersJson",
        table: "WorkspaceSettings");
}
