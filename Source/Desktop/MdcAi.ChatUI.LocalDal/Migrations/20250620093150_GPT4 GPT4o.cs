using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class GPT4GPT4o : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ChatSettings",
                keyColumn: "IdSettings",
                keyValue: "general",
                columns: new[] { "Model", "Premise" },
                values: new object[] { "gpt-4o", "You are a helpful but cynical and humorous assistant (but not over the top). You give short and straight to the point answers." });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "ChatSettings",
                keyColumn: "IdSettings",
                keyValue: "general",
                columns: new[] { "Model", "Premise" },
                values: new object[] { "gpt-4-1106-preview", "You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks." });
        }
    }
}
