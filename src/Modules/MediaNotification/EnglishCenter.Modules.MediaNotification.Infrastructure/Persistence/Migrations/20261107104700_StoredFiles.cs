using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StoredFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "media_notification");

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "media_notification",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OwnerId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    Purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    StagingKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FileKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UploadedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Revision = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stored_files", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_FileKey",
                schema: "media_notification",
                table: "stored_files",
                column: "FileKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_OwnerId_Id",
                schema: "media_notification",
                table: "stored_files",
                columns: new[] { "OwnerId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_OwnerId_UploadedAt",
                schema: "media_notification",
                table: "stored_files",
                columns: new[] { "OwnerId", "UploadedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_stored_files_StagingKey",
                schema: "media_notification",
                table: "stored_files",
                column: "StagingKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "media_notification");
        }
    }
}
