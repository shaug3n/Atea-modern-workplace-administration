using System;
using Atea.UnifiedWorkplace.Api.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

[Migration("20260919000100_AddInvitationApprovedTenantObjectId")]
[DbContext(typeof(WorkplaceDbContext))]
public partial class AddInvitationApprovedTenantObjectId : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "ApprovedTenantObjectId",
            table: "PlatformInvitations",
            type: "uuid",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "ApprovedTenantObjectId", table: "PlatformInvitations");
    }
}
