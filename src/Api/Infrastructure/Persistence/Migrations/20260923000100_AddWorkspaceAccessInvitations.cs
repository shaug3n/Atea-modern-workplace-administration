using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20260923000100_AddWorkspaceAccessInvitations")]
public partial class AddWorkspaceAccessInvitations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "Role",
            table: "PlatformInvitations",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "customer_admin");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "RevokedAt",
            table: "PlatformInvitations",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.Sql("UPDATE \"WorkspaceMemberships\" SET \"PlatformRole\" = 'customer_admin' WHERE lower(\"PlatformRole\") = 'customeradmin'");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"WorkspaceMemberships\" SET \"PlatformRole\" = 'CustomerAdmin' WHERE lower(\"PlatformRole\") = 'customer_admin'");
        migrationBuilder.DropColumn(name: "Role", table: "PlatformInvitations");
        migrationBuilder.DropColumn(name: "RevokedAt", table: "PlatformInvitations");
    }
}
