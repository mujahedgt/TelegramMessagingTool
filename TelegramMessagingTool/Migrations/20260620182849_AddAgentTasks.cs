using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramMessagingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ConnectedUserId = table.Column<int>(type: "int", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    Goal = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTasks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTasks_Users_ConnectedUserId",
                        column: x => x.ConnectedUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentTaskSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AgentTaskId = table.Column<int>(type: "int", nullable: false),
                    StepNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IsDone = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskSteps_AgentTasks_AgentTaskId",
                        column: x => x.AgentTaskId,
                        principalTable: "AgentTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ChatId",
                table: "AgentTasks",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_ConnectedUserId",
                table: "AgentTasks",
                column: "ConnectedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_Status",
                table: "AgentTasks",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTasks_UpdatedAt",
                table: "AgentTasks",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskSteps_AgentTaskId",
                table: "AgentTaskSteps",
                column: "AgentTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskSteps_AgentTaskId_StepNumber",
                table: "AgentTaskSteps",
                columns: new[] { "AgentTaskId", "StepNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskSteps");

            migrationBuilder.DropTable(
                name: "AgentTasks");
        }
    }
}
