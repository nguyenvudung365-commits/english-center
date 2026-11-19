using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance", x => x.id);
                    table.CheckConstraint("ck_attendance_revision", "revision >= 1");
                    table.CheckConstraint("ck_attendance_status", "status IN ('Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly')");
                    table.ForeignKey(
                        name: "FK_attendance_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "academic",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_revisions",
                schema: "academic",
                columns: table => new
                {
                    attendance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    session_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<Guid>(type: "uuid", nullable: false),
                    previous_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    changed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    changed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_revisions", x => new { x.attendance_id, x.revision });
                    table.CheckConstraint("ck_attendance_revisions_previous", "previous_status IS NULL OR previous_status IN ('Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly')");
                    table.CheckConstraint("ck_attendance_revisions_revision", "revision >= 1");
                    table.CheckConstraint("ck_attendance_revisions_status", "status IN ('Present', 'AbsentUnexcused', 'AbsentExcused', 'Late', 'LateOver20Min', 'LeftEarly')");
                    table.ForeignKey(
                        name: "FK_attendance_revisions_attendance_attendance_id",
                        column: x => x.attendance_id,
                        principalSchema: "academic",
                        principalTable: "attendance",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_revisions_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_attendance_revisions_sessions_session_id",
                        column: x => x.session_id,
                        principalSchema: "academic",
                        principalTable: "sessions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_replay_attempts",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attendance_id = table.Column<Guid>(type: "uuid", nullable: false),
                    revision = table.Column<int>(type: "integer", nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    dispatch_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_attendance_replay_attempts", x => x.id);
                    table.CheckConstraint("ck_attendance_replay_completion", "(dispatch_status = 'Pending' AND completed_at_utc IS NULL) OR (dispatch_status <> 'Pending' AND completed_at_utc IS NOT NULL)");
                    table.CheckConstraint("ck_attendance_replay_reason", "length(trim(reason)) BETWEEN 1 AND 500");
                    table.CheckConstraint("ck_attendance_replay_status", "dispatch_status IN ('Pending', 'Dispatched', 'Failed')");
                    table.ForeignKey(
                        name: "FK_attendance_replay_attempts_attendance_revisions_attendance_~",
                        columns: x => new { x.attendance_id, x.revision },
                        principalSchema: "academic",
                        principalTable: "attendance_revisions",
                        principalColumns: new[] { "attendance_id", "revision" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_class_id",
                schema: "academic",
                table: "attendance",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_session_id_student_id",
                schema: "academic",
                table: "attendance",
                columns: new[] { "session_id", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_attendance_student_id_class_id",
                schema: "academic",
                table: "attendance",
                columns: new[] { "student_id", "class_id" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_replay_attempts_attendance_id_requested_at_utc",
                schema: "academic",
                table: "attendance_replay_attempts",
                columns: new[] { "attendance_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_replay_attempts_attendance_id_revision",
                schema: "academic",
                table: "attendance_replay_attempts",
                columns: new[] { "attendance_id", "revision" });

            migrationBuilder.CreateIndex(
                name: "IX_attendance_revisions_class_id",
                schema: "academic",
                table: "attendance_revisions",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "IX_attendance_revisions_session_id",
                schema: "academic",
                table: "attendance_revisions",
                column: "session_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_replay_attempts",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "attendance_revisions",
                schema: "academic");

            migrationBuilder.DropTable(
                name: "attendance",
                schema: "academic");
        }
    }
}
