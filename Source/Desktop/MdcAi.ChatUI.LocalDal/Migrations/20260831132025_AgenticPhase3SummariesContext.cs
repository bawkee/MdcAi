using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class AgenticPhase3SummariesContext : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    IdArtifact = table.Column<string>(type: "TEXT", nullable: false),
                    OwnerConversationId = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerTurnId = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerToolCallId = table.Column<string>(type: "TEXT", nullable: true),
                    OwnerJobId = table.Column<string>(type: "TEXT", nullable: true),
                    StorageName = table.Column<string>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", nullable: true),
                    MimeType = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiryUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.IdArtifact);
                });

            migrationBuilder.CreateTable(
                name: "ConversationSummaries",
                columns: table => new
                {
                    IdSummary = table.Column<string>(type: "TEXT", nullable: false),
                    IdConversation = table.Column<string>(type: "TEXT", nullable: true),
                    BranchAnchorMessageId = table.Column<string>(type: "TEXT", nullable: true),
                    CoveredThroughMessageId = table.Column<string>(type: "TEXT", nullable: true),
                    SourceHash = table.Column<string>(type: "TEXT", nullable: true),
                    SummaryText = table.Column<string>(type: "TEXT", nullable: true),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: true),
                    SummarizerPromptVersion = table.Column<string>(type: "TEXT", nullable: true),
                    TokenEstimate = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: true),
                    SupersedesSummaryId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConversationSummaries", x => x.IdSummary);
                });

            migrationBuilder.CreateTable(
                name: "WorkspaceContexts",
                columns: table => new
                {
                    IdWorkspaceContext = table.Column<string>(type: "TEXT", nullable: false),
                    IdConversation = table.Column<string>(type: "TEXT", nullable: true),
                    SourceKind = table.Column<string>(type: "TEXT", nullable: true),
                    SourcePath = table.Column<string>(type: "TEXT", nullable: true),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    DiscoveredUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<string>(type: "TEXT", nullable: true),
                    FirstTurnId = table.Column<string>(type: "TEXT", nullable: true),
                    LastTurnId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkspaceContexts", x => x.IdWorkspaceContext);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_OwnerConversationId",
                table: "Artifacts",
                column: "OwnerConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_ConversationSummaries_IdConversation_Status",
                table: "ConversationSummaries",
                columns: new[] { "IdConversation", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkspaceContexts_IdConversation_SourceKind_State",
                table: "WorkspaceContexts",
                columns: new[] { "IdConversation", "SourceKind", "State" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "ConversationSummaries");

            migrationBuilder.DropTable(
                name: "WorkspaceContexts");
        }
    }
}
