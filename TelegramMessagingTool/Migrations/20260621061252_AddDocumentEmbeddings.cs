using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramMessagingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentEmbeddings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmbeddingJson",
                table: "DocumentChunks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmbeddingModel",
                table: "DocumentChunks",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "EmbeddingUpdatedAt",
                table: "DocumentChunks",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmbeddingJson",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingModel",
                table: "DocumentChunks");

            migrationBuilder.DropColumn(
                name: "EmbeddingUpdatedAt",
                table: "DocumentChunks");
        }
    }
}
