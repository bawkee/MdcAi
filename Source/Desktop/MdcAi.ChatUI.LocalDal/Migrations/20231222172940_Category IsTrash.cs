using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class CategoryIsTrash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsTrash",
                table: "Categories",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.UpdateData(
                table: "Categories",
                keyColumn: "IdCategory",
                keyValue: "default",
                column: "IsTrash",
                value: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsTrash",
                table: "Categories");
        }
    }
}
