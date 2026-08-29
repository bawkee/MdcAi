using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class Effort : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Effort",
                table: "Messages",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Effort",
                table: "ChatSettings",
                type: "TEXT",
                nullable: true);

            migrationBuilder.UpdateData(
                table: "ChatSettings",
                keyColumn: "IdSettings",
                keyValue: "general",
                column: "Effort",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Effort",
                table: "Messages");

            migrationBuilder.DropColumn(
                name: "Effort",
                table: "ChatSettings");
        }
    }
}
