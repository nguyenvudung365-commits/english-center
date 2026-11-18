using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.MediaNotification.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FeedAuthorRateLimitIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_post_comments_AuthorId_CreatedAt",
                schema: "media_notification",
                table: "post_comments",
                columns: new[] { "AuthorId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_class_posts_AuthorId_CreatedAt",
                schema: "media_notification",
                table: "class_posts",
                columns: new[] { "AuthorId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_post_comments_AuthorId_CreatedAt",
                schema: "media_notification",
                table: "post_comments");

            migrationBuilder.DropIndex(
                name: "IX_class_posts_AuthorId_CreatedAt",
                schema: "media_notification",
                table: "class_posts");
        }
    }
}
