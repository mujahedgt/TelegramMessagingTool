using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramMessagingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskStepScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReminderSentAtUtc",
                table: "AgentTaskSteps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduleNote",
                table: "AgentTaskSteps",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAtUtc",
                table: "AgentTaskSteps",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskSteps_ScheduledAtUtc",
                table: "AgentTaskSteps",
                column: "ScheduledAtUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AgentTaskSteps_ScheduledAtUtc",
                table: "AgentTaskSteps");

            migrationBuilder.DropColumn(
                name: "ReminderSentAtUtc",
                table: "AgentTaskSteps");

            migrationBuilder.DropColumn(
                name: "ScheduleNote",
                table: "AgentTaskSteps");

            migrationBuilder.DropColumn(
                name: "ScheduledAtUtc",
                table: "AgentTaskSteps");
        }
    }
}
