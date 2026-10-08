using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atea.UnifiedWorkplace.Api.Infrastructure.Persistence.Migrations;

public partial class AddFeedbackSubmissions : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "FeedbackSubmissions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                SubmitterObjectId = table.Column<Guid>(type: "uuid", nullable: false),
                Category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Subject = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                Message = table.Column<string>(type: "character varying(4000)", maxLength: 4_000, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetryKeyHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                PayloadFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_FeedbackSubmissions", submission => submission.Id);
                table.ForeignKey(
                    name: "FK_FeedbackSubmissions_Workspaces_WorkspaceId",
                    column: submission => submission.WorkspaceId,
                    principalTable: "Workspaces",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_FeedbackSubmissions_ExpiresAt",
            table: "FeedbackSubmissions",
            column: "ExpiresAt");

        migrationBuilder.CreateIndex(
            name: "IX_FeedbackSubmissions_RetryKey",
            table: "FeedbackSubmissions",
            columns: new[] { "WorkspaceId", "SubmitterObjectId", "RetryKeyHash" },
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "FeedbackSubmissions");
}
