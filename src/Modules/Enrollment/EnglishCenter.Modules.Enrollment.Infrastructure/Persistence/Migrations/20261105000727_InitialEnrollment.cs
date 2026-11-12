using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEnrollment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "enrollment");

            migrationBuilder.CreateTable(
                name: "class_snapshots",
                schema: "enrollment",
                columns: table => new
                {
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    end_date = table.Column<DateOnly>(type: "date", nullable: false),
                    max_students = table.Column<int>(type: "integer", nullable: false),
                    current_students = table.Column<int>(type: "integer", nullable: false),
                    tuition_fee = table.Column<decimal>(type: "numeric(12,0)", precision: 12, scale: 0, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_snapshots", x => x.class_id);
                    table.CheckConstraint("ck_class_capacity", "max_students > 0 AND current_students >= 0 AND current_students <= max_students");
                    table.CheckConstraint("ck_class_dates", "end_date > start_date");
                    table.CheckConstraint("ck_class_tuition", "tuition_fee >= 0");
                });

            migrationBuilder.CreateTable(
                name: "students",
                schema: "enrollment",
                columns: table => new
                {
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_students", x => x.student_id);
                });

            migrationBuilder.CreateTable(
                name: "enrollments",
                schema: "enrollment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    enrolled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    tuition_fee = table.Column<decimal>(type: "numeric(12,0)", precision: 12, scale: 0, nullable: false),
                    max_absences = table.Column<int>(type: "integer", nullable: false),
                    max_lates = table.Column<int>(type: "integer", nullable: false),
                    max_skipped_assignments = table.Column<int>(type: "integer", nullable: false),
                    absence_count = table.Column<int>(type: "integer", nullable: false),
                    late_count = table.Column<int>(type: "integer", nullable: false),
                    skipped_assignment_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_enrollments", x => x.id);
                    table.CheckConstraint("ck_enrollment_limits", "max_absences >= 0 AND max_lates >= 0 AND max_skipped_assignments >= 0 AND absence_count >= 0 AND late_count >= 0 AND skipped_assignment_count >= 0");
                    table.CheckConstraint("ck_enrollment_status", "status IN ('Active', 'Cancelled')");
                    table.CheckConstraint("ck_enrollment_tuition", "tuition_fee >= 0");
                    table.ForeignKey(
                        name: "FK_enrollments_class_snapshots_class_id",
                        column: x => x.class_id,
                        principalSchema: "enrollment",
                        principalTable: "class_snapshots",
                        principalColumn: "class_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_enrollments_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "enrollment",
                        principalTable: "students",
                        principalColumn: "student_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_class_id",
                schema: "enrollment",
                table: "enrollments",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_student_id_class_id",
                schema: "enrollment",
                table: "enrollments",
                columns: new[] { "student_id", "class_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollments",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "class_snapshots",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "students",
                schema: "enrollment");
        }
    }
}
