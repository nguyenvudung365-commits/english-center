using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddClassMaterialsAndExamPrompts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments");

            migrationBuilder.AddColumn<string>(
                name: "prompt_content_type",
                schema: "academic",
                table: "assignments",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "prompt_file_id",
                schema: "academic",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "prompt_file_name",
                schema: "academic",
                table: "assignments",
                type: "character varying(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "prompt_owner_id",
                schema: "academic",
                table: "assignments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "prompt_size_bytes",
                schema: "academic",
                table: "assignments",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "class_materials",
                schema: "academic",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    content_type = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_class_materials", x => x.id);
                    table.CheckConstraint("ck_class_materials_size", "size_bytes > 0 AND size_bytes <= 20971520");
                    table.ForeignKey(
                        name: "FK_class_materials_classes_class_id",
                        column: x => x.class_id,
                        principalSchema: "academic",
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_exam_prompt",
                schema: "academic",
                table: "assignments",
                sql: "kind <> 'Exam' OR prompt_file_id IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments",
                sql: "kind IN ('InClass', 'Homework', 'Exam')");

            migrationBuilder.CreateIndex(
                name: "IX_class_materials_class_id_created_at",
                schema: "academic",
                table: "class_materials",
                columns: new[] { "class_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_class_materials_file_id",
                schema: "academic",
                table: "class_materials",
                column: "file_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_materials",
                schema: "academic");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_exam_prompt",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "prompt_content_type",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "prompt_file_id",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "prompt_file_name",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "prompt_owner_id",
                schema: "academic",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "prompt_size_bytes",
                schema: "academic",
                table: "assignments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_kind",
                schema: "academic",
                table: "assignments",
                sql: "kind IN ('InClass', 'Homework')");
        }
    }
}
