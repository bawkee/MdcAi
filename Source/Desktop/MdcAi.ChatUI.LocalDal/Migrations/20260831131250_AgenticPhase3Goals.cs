using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class AgenticPhase3Goals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Goals",
                columns: table => new
                {
                    IdGoal = table.Column<string>(type: "TEXT", nullable: false),
                    IdConversation = table.Column<string>(type: "TEXT", nullable: true),
                    Objective = table.Column<string>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    Revision = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxRounds = table.Column<int>(type: "INTEGER", nullable: false),
                    RoundsStarted = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenLimit = table.Column<long>(type: "INTEGER", nullable: true),
                    TokensConsumed = table.Column<long>(type: "INTEGER", nullable: true),
                    CostLimit = table.Column<decimal>(type: "TEXT", nullable: true),
                    CostConsumed = table.Column<decimal>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    StartedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EndedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    BlockedCode = table.Column<string>(type: "TEXT", nullable: true),
                    BlockedReason = table.Column<string>(type: "TEXT", nullable: true),
                    FinalSummary = table.Column<string>(type: "TEXT", nullable: true),
                    EvidenceJson = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Goals", x => x.IdGoal);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Goals_IdConversation_Status",
                table: "Goals",
                columns: new[] { "IdConversation", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Goals");
        }
    }
}
