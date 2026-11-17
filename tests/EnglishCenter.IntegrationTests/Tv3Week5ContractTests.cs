using System.Text.Json;
using EnglishCenter.Modules.Finance.Contracts.IntegrationEvents;
using EnglishCenter.Modules.Enrollment.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace EnglishCenter.IntegrationTests;

public sealed class Tv3Week5ContractTests
{
    [Fact]
    public void InvoicePaidPayload_PreservesRecipientSourceAndUtcTimestamp()
    {
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var paid = new InvoicePaidIntegrationEvent(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow);
        var restored = JsonSerializer.Deserialize<InvoicePaidIntegrationEvent>(JsonSerializer.Serialize(paid, json), json)!;
        Assert.Equal(paid, restored);
        Assert.Equal(DateTimeKind.Utc, restored.PaidAtUtc.Kind);
    }

    [Fact]
    public void Week5Migration_HasLocalReferencesAndDurableUniqueness()
    {
        using var db = new EnrollmentDesignTimeFactory().CreateDbContext([]);
        var sql = db.GetService<IMigrator>().GenerateScript(options: MigrationsSqlGenerationOptions.Idempotent);
        foreach (var table in new[] { "attendance_snapshots", "missed_assignments", "warnings", "placement_tests", "test_teachers" })
            Assert.Contains("CREATE TABLE enrollment." + table, sql);
        Assert.Contains("PRIMARY KEY (session_id, student_id)", sql);
        Assert.Contains("PRIMARY KEY (assignment_id, enrollment_id)", sql);
        Assert.Contains("CREATE UNIQUE INDEX \"IX_warnings_enrollment_id_warning_type\"", sql);
        Assert.Contains("REFERENCES enrollment.enrollments", sql);
        Assert.DoesNotContain("academic.", sql);
        Assert.DoesNotContain("identity.", sql);
        Assert.DoesNotContain("finance.", sql);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
