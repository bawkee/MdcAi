using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MdcAi.ChatUI.LocalDal.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    IdCategory = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    SystemMessage = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.IdCategory);
                });

            migrationBuilder.CreateTable(
                name: "Conversations",
                columns: table => new
                {
                    IdConversation = table.Column<string>(type: "TEXT", nullable: false),
                    IdCategory = table.Column<string>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", nullable: true),
                    IsTrash = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedTs = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Conversations", x => x.IdConversation);
                    table.ForeignKey(
                        name: "FK_Conversations_Categories_IdCategory",
                        column: x => x.IdCategory,
                        principalTable: "Categories",
                        principalColumn: "IdCategory");
                });

            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    IdMessage = table.Column<string>(type: "TEXT", nullable: false),
                    IdMessageParent = table.Column<string>(type: "TEXT", nullable: true),
                    IdConversation = table.Column<string>(type: "TEXT", nullable: true),
                    Version = table.Column<int>(type: "INTEGER", nullable: false),
                    IsCurrentVersion = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedTs = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: true),
                    Content = table.Column<string>(type: "TEXT", nullable: true),
                    IsTrash = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.IdMessage);
                    table.ForeignKey(
                        name: "FK_Messages_Conversations_IdConversation",
                        column: x => x.IdConversation,
                        principalTable: "Conversations",
                        principalColumn: "IdConversation");
                });

            migrationBuilder.InsertData(
                table: "Categories",
                columns: new[] { "IdCategory", "Description", "Name", "SystemMessage" },
                values: new object[] { "default", "General Purpose AI Assistant", "General", "You are a helpful but cynical and humorous assistant (but not over the top). You give short answers, straight, to the point answers. Use md syntax and be sure to specify language for code blocks." });

            migrationBuilder.CreateIndex(
                name: "IX_Conversations_IdCategory",
                table: "Conversations",
                column: "IdCategory");

            migrationBuilder.CreateIndex(
                name: "IX_Messages_IdConversation",
                table: "Messages",
                column: "IdConversation");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Messages");

            migrationBuilder.DropTable(
                name: "Conversations");

            migrationBuilder.DropTable(
                name: "Categories");
        }
    }
}
