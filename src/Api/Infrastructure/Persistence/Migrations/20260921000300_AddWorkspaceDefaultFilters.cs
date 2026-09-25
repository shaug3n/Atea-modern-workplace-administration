using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20260921000300_AddWorkspaceDefaultFilters")]
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
