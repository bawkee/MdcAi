using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class MessagesModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Model",
                table: "Messages",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Model",
                table: "Messages");
        }
    }
}
