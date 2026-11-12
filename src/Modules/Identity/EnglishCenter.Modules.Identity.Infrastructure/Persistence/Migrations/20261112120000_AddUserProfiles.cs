using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Identity.Infrastructure.Persistence.Migrations;

public partial class AddUserProfiles : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(name: "AvatarFileId", schema: "identity", table: "users", type: "uuid", nullable: true);
        migrationBuilder.AddColumn<string>(name: "Phone", schema: "identity", table: "users", type: "character varying(32)", maxLength: 32, nullable: true);
        migrationBuilder.AddColumn<long>(name: "ProfileVersion", schema: "identity", table: "users", type: "bigint", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<string>(name: "StudentCode", schema: "identity", table: "users", type: "character varying(11)", maxLength: 11, nullable: true);
        migrationBuilder.CreateSequence<long>(name: "student_code_sequence", schema: "identity", minValue: 1L, maxValue: 999999L);
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF (SELECT count(*) FROM identity.users WHERE "Role" = 'Student') > 999999 THEN
                    RAISE EXCEPTION 'Student code capacity exceeded; migration aborted';
                END IF;
            END $$;

            WITH numbered AS (
                SELECT "Id", row_number() OVER (ORDER BY "CreatedAt", "Id") AS number
                FROM identity.users WHERE "Role" = 'Student'
            )
            UPDATE identity.users AS u
            SET "StudentCode" = 'HV' || lpad(n.number::text, 6, '0') || '-' || to_char(u."CreatedAt" AT TIME ZONE 'UTC', 'YY')
            FROM numbered AS n WHERE u."Id" = n."Id";

            SELECT setval('identity.student_code_sequence', greatest(count(*), 1), count(*) > 0)
            FROM identity.users WHERE "Role" = 'Student';
            """);
        migrationBuilder.CreateIndex(name: "IX_users_StudentCode", schema: "identity", table: "users",
            column: "StudentCode", unique: true, filter: "\"StudentCode\" IS NOT NULL");
        migrationBuilder.AddCheckConstraint(name: "ck_users_student_code", schema: "identity", table: "users",
            sql: "(\"Role\" = 'Student' AND \"StudentCode\" IS NOT NULL AND \"StudentCode\" ~ '^HV[0-9]{6}-[0-9]{2}$' AND substring(\"StudentCode\" from 3 for 6) <> '000000' AND right(\"StudentCode\", 2) = to_char(\"CreatedAt\" AT TIME ZONE 'UTC', 'YY')) OR (\"Role\" <> 'Student' AND \"StudentCode\" IS NULL)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(name: "ck_users_student_code", schema: "identity", table: "users");
        migrationBuilder.DropIndex(name: "IX_users_StudentCode", schema: "identity", table: "users");
        migrationBuilder.DropSequence(name: "student_code_sequence", schema: "identity");
        migrationBuilder.DropColumn(name: "AvatarFileId", schema: "identity", table: "users");
        migrationBuilder.DropColumn(name: "Phone", schema: "identity", table: "users");
        migrationBuilder.DropColumn(name: "ProfileVersion", schema: "identity", table: "users");
        migrationBuilder.DropColumn(name: "StudentCode", schema: "identity", table: "users");
    }
}
