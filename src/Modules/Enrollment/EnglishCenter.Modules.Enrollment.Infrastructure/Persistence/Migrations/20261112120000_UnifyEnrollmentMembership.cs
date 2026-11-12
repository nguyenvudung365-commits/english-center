using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace EnglishCenter.Modules.Enrollment.Infrastructure.Persistence.Migrations;

[DbContext(typeof(EnrollmentDbContext))]
[Migration("20261112120000_UnifyEnrollmentMembership")]
public sealed partial class UnifyEnrollmentMembership : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "is_active", schema: "enrollment", table: "enrollments",
            type: "boolean", nullable: false, defaultValue: true);

        // Never manufacture financial registrations from legacy coursework-only records.
        migrationBuilder.Sql("""
            DO $migration$
            BEGIN
                IF EXISTS (
                    SELECT 1 FROM enrollment.class_enrollments AS m
                    LEFT JOIN enrollment.enrollments AS e
                        ON e.class_id = m."ClassId" AND e.student_id = m."StudentId"
                    WHERE e.id IS NULL
                ) THEN
                    RAISE EXCEPTION 'Legacy coursework memberships require explicit financial reconciliation before UnifyEnrollmentMembership. Create matching registrations with approved tuition and violation limits, preserving capacity and replaying enrollment events; no legacy records were removed.';
                END IF;

                UPDATE enrollment.enrollments AS e
                SET is_active = m."IsActive"
                FROM enrollment.class_enrollments AS m
                WHERE e.class_id = m."ClassId" AND e.student_id = m."StudentId";
            END;
            $migration$;
            """);

        // Keep original identifiers and timestamps for audit; only enrollments remains authoritative.
        migrationBuilder.RenameTable(name: "class_enrollments", schema: "enrollment",
            newName: "legacy_class_memberships", newSchema: "enrollment");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        throw new NotSupportedException("Membership reconciliation is forward-only. Restore a pre-migration backup to roll back coursework access changes.");
}
