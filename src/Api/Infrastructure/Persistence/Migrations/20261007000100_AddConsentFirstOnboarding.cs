using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WorkplaceDbContext))]
[Migration("20261007000100_AddConsentFirstOnboarding")]
public sealed class AddConsentFirstOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "InvitationId",
            table: "ConsentChallenges",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "Purpose",
            table: "ConsentChallenges",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "workspace");

        migrationBuilder.AddColumn<Guid>(
            name: "RedeemedByTenantObjectId",
            table: "PlatformInvitations",
            type: "uuid",
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "IX_ConsentChallenges_InvitationId_ExpiresAt",
            table: "ConsentChallenges",
            columns: new[] { "InvitationId", "ExpiresAt" });

        migrationBuilder.AddForeignKey(
            name: "FK_ConsentChallenges_PlatformInvitations_InvitationId",
            table: "ConsentChallenges",
            column: "InvitationId",
            principalTable: "PlatformInvitations",
            principalColumn: "Id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_ConsentChallenges_PlatformInvitations_InvitationId",
            table: "ConsentChallenges");

        migrationBuilder.DropIndex(
            name: "IX_ConsentChallenges_InvitationId_ExpiresAt",
            table: "ConsentChallenges");

        migrationBuilder.DropColumn(name: "InvitationId", table: "ConsentChallenges");
        migrationBuilder.DropColumn(name: "Purpose", table: "ConsentChallenges");
        migrationBuilder.DropColumn(name: "RedeemedByTenantObjectId", table: "PlatformInvitations");
    }
}
