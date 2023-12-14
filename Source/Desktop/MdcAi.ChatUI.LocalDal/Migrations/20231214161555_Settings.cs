using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class Settings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdSettingsOverride",
                table: "Conversations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdSettings",
                table: "Categories",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ChatSettings",
                columns: table => new
                {
                    IdSettings = table.Column<string>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: true),
                    Streaming = table.Column<bool>(type: "INTEGER", nullable: false),
                    Temperature = table.Column<decimal>(type: "TEXT", nullable: false),
                    TopP = table.Column<decimal>(type: "TEXT", nullable: false),
                    FrequencyPenalty = table.Column<decimal>(type: "TEXT", nullable: false),
                    PresencePenalty = table.Column<decimal>(type: "TEXT", nullable: false),
                    Premise = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSettings", x => x.IdSettings);
                });

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "IdCategory",
                keyValue: "default",
                columns: new[] { "IdSettings", "SystemMessage" },
                values: new object[] { "general", null });

            migrationBuilder.InsertData(
                table: "ChatSettings",
                columns: new[] { "IdSettings", "FrequencyPenalty", "Model", "Premise", "PresencePenalty", "Streaming", "Temperature", "TopP" },
                values: new object[] { "general", 1m, "gpt-4-1106-preview", "You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks.", 1m, true, 1m, 1m });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_IdSettingsOverride",
                table: "Conversations",
                column: "IdSettingsOverride");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_IdSettings",
                table: "Categories",
                column: "IdSettings");

            migrationBuilder.AddForeignKey(
                name: "FK_Categories_ChatSettings_IdSettings",
                table: "Categories",
                column: "IdSettings",
                principalTable: "ChatSettings",
                principalColumn: "IdSettings",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Conversations_ChatSettings_IdSettingsOverride",
                table: "Conversations",
                column: "IdSettingsOverride",
                principalTable: "ChatSettings",
                principalColumn: "IdSettings");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Categories_ChatSettings_IdSettings",
                table: "Categories");

            migrationBuilder.DropForeignKey(
                name: "FK_Conversations_ChatSettings_IdSettingsOverride",
                table: "Conversations");

            migrationBuilder.DropTable(
                name: "ChatSettings");

            migrationBuilder.DropIndex(
                name: "IX_Conversations_IdSettingsOverride",
                table: "Conversations");

            migrationBuilder.DropIndex(
                name: "IX_Categories_IdSettings",
                table: "Categories");

            migrationBuilder.DropColumn(
                name: "IdSettingsOverride",
                table: "Conversations");

            migrationBuilder.DropColumn(
                name: "IdSettings",
                table: "Categories");

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "IdCategory",
                keyValue: "default",
                column: "SystemMessage",
                value: "You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks.");
        }
    }
}
