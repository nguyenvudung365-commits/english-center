using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using EnglishCenter.Modules.Finance.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class Tv3Week3ConfigurationTests
{
    [Fact]
    public void EnrollmentMigration_HasLocalForeignKeysUniquePairAndChecks()
    {
        using var db = new EnrollmentDesignTimeFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE enrollment.students", sql);
        Assert.Contains("CREATE TABLE enrollment.class_snapshots", sql);
        Assert.Contains("CREATE TABLE enrollment.enrollments", sql);
        Assert.Contains("REFERENCES enrollment.students", sql);
        Assert.Contains("REFERENCES enrollment.class_snapshots", sql);
        Assert.Contains("(student_id, class_id)", sql);
        Assert.Contains("current_students <= max_students", sql);
        Assert.Contains("max_absences >= 0", sql);
        Assert.DoesNotContain("academic.", sql);
        Assert.DoesNotContain("identity.", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public void FinanceMigration_EnforcesUniqueEnrollmentAndHasNoForeignModuleDependency()
    {
        using var db = new FinanceDesignTimeFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        Assert.Contains("CREATE TABLE finance.invoices", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_invoices_enrollment_id\"", sql);
        Assert.Contains("amount numeric(12,0) NOT NULL", sql);
        Assert.DoesNotContain("REFERENCES", sql);
        Assert.DoesNotContain("enrollment.", sql);
        Assert.DoesNotContain("academic.", sql);
        Assert.DoesNotContain("identity.", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
