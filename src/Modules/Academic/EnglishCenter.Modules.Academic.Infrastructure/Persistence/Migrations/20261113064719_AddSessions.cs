using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sessions",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    unit = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    lesson = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    skills = table.Column<int>(type: "integer", nullable: false),
                    session_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    exam_slot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    starts_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ends_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sessions", x => x.id);
                    table.CheckConstraint("ck_sessions_exam_slot", "exam_slot IS NULL OR (session_type <> 'Regular' AND length(trim(exam_slot)) BETWEEN 1 AND 100)");
                    table.CheckConstraint("ck_sessions_lesson", "length(trim(lesson)) BETWEEN 1 AND 200");
                    table.CheckConstraint("ck_sessions_skills", "skills >= 1 AND skills <= 15");
                    table.CheckConstraint("ck_sessions_time", "ends_at_utc > starts_at_utc");
                    table.CheckConstraint("ck_sessions_type", "session_type IN ('Regular', 'MockTest', 'FinalTest')");
                    table.CheckConstraint("ck_sessions_unit", "length(trim(unit)) BETWEEN 1 AND 100");
                    table.ForeignKey(
                        name: "FK_sessions_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_class_id_starts_at_utc",
                schema: "academic",
                table: "sessions",
                columns: new[] { "class_id", "starts_at_utc" });

            migrationBuilder.CreateIndex(
                name: "IX_sessions_starts_at_utc",
                schema: "academic",
                table: "sessions",
                column: "starts_at_utc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sessions",
                schema: "academic");
        }
    }
}
