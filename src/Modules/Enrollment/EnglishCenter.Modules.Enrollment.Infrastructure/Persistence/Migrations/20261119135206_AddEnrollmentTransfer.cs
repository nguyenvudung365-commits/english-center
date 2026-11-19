using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentTransfer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_enrollments_transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments",
                column: "transferred_from_enrollment_id");

            migrationBuilder.AddForeignKey(
                name: "FK_enrollments_enrollments_transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments",
                column: "transferred_from_enrollment_id",
                principalSchema: "enrollment",
                principalTable: "enrollments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_enrollments_enrollments_transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments");

            migrationBuilder.DropIndex(
                name: "IX_enrollments_transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments");

            migrationBuilder.DropColumn(
                name: "transferred_from_enrollment_id",
                schema: "enrollment",
                table: "enrollments");
        }
    }
}
