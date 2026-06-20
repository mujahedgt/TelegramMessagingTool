using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TelegramMessagingTool.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentChunks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DocumentChunks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UploadedFileId = table.Column<int>(type: "int", nullable: false),
                    ConnectedUserId = table.Column<int>(type: "int", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    ChunkNumber = table.Column<int>(type: "int", nullable: false),
                    OriginalFileName = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CharacterCount = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentChunks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_UploadedFiles_UploadedFileId",
                        column: x => x.UploadedFileId,
                        principalTable: "UploadedFiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DocumentChunks_Users_ConnectedUserId",
                        column: x => x.ConnectedUserId,
                        principalTable: "Users",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ChatId",
                table: "DocumentChunks",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ConnectedUserId",
                table: "DocumentChunks",
                column: "ConnectedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_ConnectedUserId_UploadedFileId_ChunkNumber",
                table: "DocumentChunks",
                columns: new[] { "ConnectedUserId", "UploadedFileId", "ChunkNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DocumentChunks_UploadedFileId",
                table: "DocumentChunks",
                column: "UploadedFileId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentChunks");
        }
    }
}
