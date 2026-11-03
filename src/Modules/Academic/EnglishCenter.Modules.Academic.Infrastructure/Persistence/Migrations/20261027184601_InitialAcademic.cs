using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAcademic : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "academic");

            migrationBuilder.CreateTable(
                name: "courses",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_courses", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "teachers",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_teachers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "classes",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    class_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    max_students = table.Column<int>(type: "integer", nullable: false),
                    tuition_fee = table.Column<decimal>(type: "numeric(12,0)", precision: 12, scale: 0, nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_classes", x => x.id);
                    table.CheckConstraint("ck_classes_end_date_after_start_date", "end_date > start_date");
                    table.CheckConstraint("ck_classes_max_students_positive", "max_students > 0");
                    table.CheckConstraint("ck_classes_tuition_fee_non_negative", "tuition_fee >= 0");
                    table.ForeignKey(
                        name: "FK_classes_courses_course_id",
                        column: x => x.course_id,
                        principalSchema: "academic",
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_classes_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalSchema: "academic",
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                schema: "academic",
                table: "courses",
                columns: new[] { "id", "code", "description", "name" },
                values: new object[,]
                {
                    { new Guid("34d802e7-5319-446a-9104-c6ff10cd6a00"), "IELTS-FOUNDATION", "Nền tảng 4 kỹ năng, mục tiêu band 4.0-4.5", "IELTS Foundation" },
                    { new Guid("1ae06bfd-eed6-476b-b63d-ef71138ee3b0"), "IELTS-INTERMEDIATE", "Mục tiêu band 5.0-5.5", "IELTS Intermediate" },
                    { new Guid("3dd0ae5a-0547-42dc-b834-9f4b0d502752"), "IELTS-ADVANCED", "Mục tiêu band 6.5 trở lên", "IELTS Advanced" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_classes_code",
                schema: "academic",
                table: "classes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_classes_course_id",
                schema: "academic",
                table: "classes",
                column: "course_id");

            migrationBuilder.CreateIndex(
                name: "IX_classes_teacher_id",
                schema: "academic",
                table: "classes",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_courses_code",
                schema: "academic",
                table: "courses",
                column: "code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "classes",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "courses",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "teachers",
                schema: "academic");
        }
    }
}
