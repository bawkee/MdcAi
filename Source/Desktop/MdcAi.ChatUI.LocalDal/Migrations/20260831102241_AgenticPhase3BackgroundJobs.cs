using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class AgenticPhase3BackgroundJobs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BackgroundJobs",
                columns: table => new
                {
                    IdJob = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerConversationId = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerTurnId = table.Column<string>(type: "TEXT", nullable: true),
                    ToolCallId = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerToolName = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CommandPresentationHash = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    OutputBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    OutputTruncated = table.Column<bool>(type: "INTEGER", nullable: false),
                    ArtifactId = table.Column<string>(type: "TEXT", nullable: true),
                    FailureSummary = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BackgroundJobs", x => x.IdJob);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_OwnerConversationId",
                table: "BackgroundJobs",
                column: "OwnerConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_BackgroundJobs_Status",
                table: "BackgroundJobs",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BackgroundJobs");
        }
    }
}
