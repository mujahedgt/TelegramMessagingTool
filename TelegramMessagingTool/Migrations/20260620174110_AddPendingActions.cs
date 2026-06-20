using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramMessagingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddPendingActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PendingActions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectedUserId = table.Column<int>(type: "int", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    ToolName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PayloadJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    RiskLevel = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PendingActions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PendingActions_Users_ConnectedUserId",
                        column: x => x.ConnectedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_ChatId",
                table: "PendingActions",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_ConnectedUserId",
                table: "PendingActions",
                column: "ConnectedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_ExpiresAt",
                table: "PendingActions",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PendingActions_Status",
                table: "PendingActions",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PendingActions");
        }
    }
}
