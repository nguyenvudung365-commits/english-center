using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Academic.Infrastructure.Persistence.Migrations;

public partial class AddTeacherProfileVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<long>(name: "profile_version", schema: "academic", table: "teachers",
            type: "bigint", nullable: false, defaultValue: 0L);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "profile_version", schema: "academic", table: "teachers");
}
