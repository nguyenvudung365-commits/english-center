using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassStudents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "class_students",
                schema: "academic",
                columns: table => new
                {
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_students", x => x.enrollment_id);
                    table.ForeignKey(
                        name: "FK_class_students_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_class_students_class_id",
                schema: "academic",
                table: "class_students",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "IX_class_students_student_id_class_id",
                schema: "academic",
                table: "class_students",
                columns: new[] { "student_id", "class_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_students",
                schema: "academic");
        }
    }
}
