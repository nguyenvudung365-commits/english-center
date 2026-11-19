using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TrackAttendanceRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "revision",
                schema: "enrollment",
                table: "attendance_snapshots",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "revision",
                schema: "enrollment",
                table: "attendance_snapshots");
        }
    }
}
