using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddMonitoringAndPlacementTests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_snapshots",
                schema: "enrollment",
                columns: table => new
                {
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_snapshots", x => new { x.session_id, x.student_id });
                    table.CheckConstraint("ck_attendance_status", "status IN ('Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly')");
                    table.ForeignKey(
                        name: "FK_attendance_snapshots_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "enrollment",
                        principalTable: "enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "missed_assignments",
                schema: "enrollment",
                columns: table => new
                {
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_missed_assignments", x => new { x.assignment_id, x.enrollment_id });
                    table.ForeignKey(
                        name: "FK_missed_assignments_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "enrollment",
                        principalTable: "enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "test_teachers",
                schema: "enrollment",
                columns: table => new
                {
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_test_teachers", x => x.teacher_id);
                });

            migrationBuilder.CreateTable(
                name: "warnings",
                schema: "enrollment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    warning_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    issued_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_warnings", x => x.id);
                    table.CheckConstraint("ck_warning_type", "warning_type IN ('Absence', 'Late', 'SkippedAssignment')");
                    table.ForeignKey(
                        name: "FK_warnings_enrollments_enrollment_id",
                        column: x => x.enrollment_id,
                        principalSchema: "enrollment",
                        principalTable: "enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "placement_tests",
                schema: "enrollment",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    scheduled_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    test_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    teacher_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_placement_tests", x => x.id);
                    table.CheckConstraint("ck_placement_mode", "mode IN ('Offline', 'Online')");
                    table.CheckConstraint("ck_placement_status", "status IN ('Pending', 'Done', 'Cancelled')");
                    table.ForeignKey(
                        name: "FK_placement_tests_students_student_id",
                        column: x => x.student_id,
                        principalSchema: "enrollment",
                        principalTable: "students",
                        principalColumn: "student_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_placement_tests_test_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalSchema: "enrollment",
                        principalTable: "test_teachers",
                        principalColumn: "teacher_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_snapshots_enrollment_id",
                schema: "enrollment",
                table: "attendance_snapshots",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "IX_missed_assignments_enrollment_id",
                schema: "enrollment",
                table: "missed_assignments",
                column: "enrollment_id");

            migrationBuilder.CreateIndex(
                name: "IX_placement_tests_student_id_scheduled_at",
                schema: "enrollment",
                table: "placement_tests",
                columns: new[] { "student_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "IX_placement_tests_teacher_id",
                schema: "enrollment",
                table: "placement_tests",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "IX_warnings_enrollment_id_warning_type",
                schema: "enrollment",
                table: "warnings",
                columns: new[] { "enrollment_id", "warning_type" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_snapshots",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "missed_assignments",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "placement_tests",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "warnings",
                schema: "enrollment");

            migrationBuilder.DropTable(
                name: "test_teachers",
                schema: "enrollment");
        }
    }
}
