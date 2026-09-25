using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20260925000100_AddWorkspaceModuleAccess")]
public sealed class AddWorkspaceModuleAccess : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "ModuleGrantsJson", table: "WorkspaceMemberships", type: "jsonb", nullable: false,
            defaultValueSql: "'[]'::jsonb");
        migrationBuilder.AddColumn<string>(
            name: "ModuleKeysJson", table: "PlatformInvitations", type: "jsonb", nullable: false,
            defaultValueSql: "'[]'::jsonb");

        migrationBuilder.Sql("""
            WITH ranked AS (
                SELECT "Id", ROW_NUMBER() OVER (PARTITION BY "WorkspaceId" ORDER BY "CreatedAt", "Id") AS position
                FROM "WorkspaceMemberships"
                WHERE NOT "IsAteaOperator" AND lower("PlatformRole") IN ('customer_admin', 'customeradmin', 'admin', 'workspace-manager', 'owner')
            )
            UPDATE "WorkspaceMemberships" AS membership
            SET "PlatformRole" = CASE WHEN ranked.position = 1 THEN 'workspace_owner' ELSE 'customer_admin' END
            FROM ranked
            WHERE membership."Id" = ranked."Id";
            """);

        migrationBuilder.Sql("""
            UPDATE "WorkspaceMemberships" AS membership
            SET "ModuleGrantsJson" = COALESCE((
                SELECT jsonb_agg(lower(module.value))
                FROM "WorkspaceSettings" AS settings,
                     jsonb_array_elements_text(settings."EnabledModulesJson") AS module(value)
                WHERE settings."WorkspaceId" = membership."WorkspaceId"
                  AND lower(module.value) IN ('users', 'devices', 'licenses', 'exchange')
            ), '["users", "devices", "licenses"]'::jsonb);

            UPDATE "PlatformInvitations" AS invitation
            SET "ModuleKeysJson" = COALESCE((
                SELECT jsonb_agg(lower(module.value))
                FROM "WorkspaceSettings" AS settings,
                     jsonb_array_elements_text(settings."EnabledModulesJson") AS module(value)
                WHERE settings."WorkspaceId" = invitation."WorkspaceId"
                  AND lower(module.value) IN ('users', 'devices', 'licenses', 'exchange')
            ), '["users", "devices", "licenses"]'::jsonb);

            UPDATE "WorkspaceSettings" AS settings
            SET "EnabledModulesJson" = COALESCE((
                SELECT jsonb_agg(lower(module.value))
                FROM jsonb_array_elements_text(settings."EnabledModulesJson") AS module(value)
                WHERE lower(module.value) IN ('users', 'devices', 'licenses', 'exchange')
            ), '["users", "devices", "licenses"]'::jsonb);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("UPDATE \"WorkspaceMemberships\" SET \"PlatformRole\" = 'CustomerAdmin' WHERE lower(\"PlatformRole\") = 'workspace_owner'");
        migrationBuilder.DropColumn(name: "ModuleGrantsJson", table: "WorkspaceMemberships");
        migrationBuilder.DropColumn(name: "ModuleKeysJson", table: "PlatformInvitations");
    }
}
