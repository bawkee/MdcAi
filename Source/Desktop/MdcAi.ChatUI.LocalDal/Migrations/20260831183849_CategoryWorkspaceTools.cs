using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class CategoryWorkspaceTools : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ToolsEnabled",
                table: "ChatSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WorkspacePath",
                table: "ChatSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ChatSettings",
                keyColumn: "IdSettings",
                keyValue: "general",
                columns: new[] { "ToolsEnabled", "WorkspacePath" },
                values: new object[] { null, null });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ToolsEnabled",
                table: "ChatSettings");

            migrationBuilder.DropColumn(
                name: "WorkspacePath",
                table: "ChatSettings");
        }
    }
}
