using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class MessagesReasoning : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Reasoning",
                table: "Messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Reasoning",
                table: "Messages");
        }
    }
}
