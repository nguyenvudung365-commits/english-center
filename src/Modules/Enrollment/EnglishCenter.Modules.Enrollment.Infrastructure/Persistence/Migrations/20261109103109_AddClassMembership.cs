using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "enrollment");

            migrationBuilder.CreateTable(
                name: "class_enrollments",
                schema: "enrollment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ClassId = table.Column<Guid>(type: "uuid", nullable: false),
                    StudentId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_enrollments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_enrollments_ClassId_StudentId",
                schema: "enrollment",
                table: "class_enrollments",
                columns: new[] { "ClassId", "StudentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_class_enrollments_StudentId",
                schema: "enrollment",
                table: "class_enrollments",
                column: "StudentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_enrollments",
                schema: "enrollment");
        }
    }
}
