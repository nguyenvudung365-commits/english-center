using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseworkGradingAndPractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "feedback",
                schema: "academic",
                table: "submissions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "grade_version",
                schema: "academic",
                table: "submissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "graded_by",
                schema: "academic",
                table: "submissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "score",
                schema: "academic",
                table: "submissions",
                type: "numeric(3,1)",
                precision: 3,
                scale: 1,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                schema: "academic",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by",
                schema: "academic",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_legacy",
                schema: "academic",
                table: "assignments",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "kind",
                schema: "academic",
                table: "assignments",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                // Bài có sẵn được coi là "Về nhà"; giá trị rỗng sẽ vi phạm ck_assignments_kind.
                defaultValue: "Homework");

            migrationBuilder.AddColumn<string>(
                name: "lesson",
                schema: "academic",
                table: "assignments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "obligations_resolved_at",
                schema: "academic",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_at",
                schema: "academic",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "published_by",
                schema: "academic",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "published_event_sent_at",
                schema: "academic",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "session_id",
                schema: "academic",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit",
                schema: "academic",
                table: "assignments",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                schema: "academic",
                table: "assignments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "version",
                schema: "academic",
                table: "assignments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // Mọi bài có trước bản nâng cấp là legacy: học viên vẫn xem/nộp theo quyền và hạn cũ,
            // không có snapshot người nhận nên worker không suy diễn missed hồi tố (Q-07, AC-05). Không xóa/ẩn dữ liệu cũ.
            migrationBuilder.Sql("UPDATE academic.assignments SET is_legacy = TRUE;");

            migrationBuilder.CreateTable(
                name: "assignment_recipients",
                schema: "academic",
                columns: table => new
                {
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    missed_event_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_assignment_recipients", x => new { x.assignment_id, x.student_id });
                    table.CheckConstraint("ck_assignment_recipients_outcome", "outcome IN ('Pending', 'Submitted', 'Missed', 'Exempted')");
                    table.ForeignKey(
                        name: "FK_assignment_recipients_assignments_assignment_id",
                        column: x => x.assignment_id,
                        principalSchema: "academic",
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "practice_exercises",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_by_role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_practice_exercises", x => x.id);
                    table.ForeignKey(
                        name: "FK_practice_exercises_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "submission_grades",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: false),
                    previous_score = table.Column<decimal>(type: "numeric(3,1)", precision: 3, scale: 1, nullable: true),
                    feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    graded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    graded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_submission_grades", x => x.id);
                    table.CheckConstraint("ck_submission_grades_score", "CAST(score AS REAL) >= 0 AND CAST(score AS REAL) <= 10");
                    table.CheckConstraint("ck_submission_grades_version", "version >= 1");
                    table.ForeignKey(
                        name: "FK_submission_grades_submissions_submission_id",
                        column: x => x.submission_id,
                        principalSchema: "academic",
                        principalTable: "submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_submissions_grade_version",
                schema: "academic",
                table: "submissions",
                sql: "grade_version >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_submissions_score",
                schema: "academic",
                table: "submissions",
                sql: "score IS NULL OR (CAST(score AS REAL) >= 0 AND CAST(score AS REAL) <= 10)");

            migrationBuilder.CreateIndex(
                name: "IX_assignments_session_id",
                schema: "academic",
                table: "assignments",
                column: "session_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments",
                sql: "kind IN ('InClass', 'Homework')");

            migrationBuilder.CreateIndex(
                name: "IX_assignment_recipients_student_id",
                schema: "academic",
                table: "assignment_recipients",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "IX_practice_exercises_class_id",
                schema: "academic",
                table: "practice_exercises",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "IX_submission_grades_event_sent_at",
                schema: "academic",
                table: "submission_grades",
                column: "event_sent_at");

            migrationBuilder.CreateIndex(
                name: "IX_submission_grades_submission_id_version",
                schema: "academic",
                table: "submission_grades",
                columns: new[] { "submission_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "assignment_recipients",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "practice_exercises",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "submission_grades",
                schema: "academic");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submissions_grade_version",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_submissions_score",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropIndex(
                name: "IX_assignments_session_id",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "feedback",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "grade_version",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "graded_by",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "score",
                schema: "academic",
                table: "submissions");

            migrationBuilder.DropColumn(
                name: "created_at",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "created_by",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "is_legacy",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "kind",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "lesson",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "obligations_resolved_at",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "published_at",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "published_by",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "published_event_sent_at",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "session_id",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "unit",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "updated_at",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "version",
                schema: "academic",
                table: "assignments");
        }
    }
}
